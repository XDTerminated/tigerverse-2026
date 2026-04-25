#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using GLTFast;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Tigerverse.Drawing;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Edit-time tool: paste an UploadThing (or any) GLB URL, click Download,
    /// and the tool fetches the GLB, imports it via glTFast, and applies the
    /// Tigerverse paper-craft shader.
    /// </summary>
    public class TigerverseFetchPaperize : EditorWindow
    {
        private const string PrefsKey_Url  = "Tigerverse.FetchPaperize.Url";
        private const string PrefsKey_Code = "Tigerverse.FetchPaperize.SessionCode";
        private const string SessionApiBase = "https://tigerverse-2026.vercel.app/api/session/";

        private string url = "";
        private string sessionCode = "";
        private string status = "Idle.";
        private bool busy;

        [MenuItem("Tigerverse/Test -> Fetch GLB from URL + Apply Paper Shader")]
        public static void Open()
        {
            var win = GetWindow<TigerverseFetchPaperize>(false, "GLB Paper Test", true);
            win.minSize = new Vector2(560, 280);
            win.Show();
        }

        private void OnEnable()
        {
            url         = EditorPrefs.GetString(PrefsKey_Url, "");
            sessionCode = EditorPrefs.GetString(PrefsKey_Code, "");
        }

        private void OnGUI()
        {
            GUILayout.Label("Fetch a GLB and apply the white-paper + ink-outline shader.", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Option A — paste an UploadThing GLB URL:");
            url = EditorGUILayout.TextField("GLB URL", url);

            EditorGUI.BeginDisabledGroup(busy || string.IsNullOrWhiteSpace(url));
            if (GUILayout.Button("Download GLB & Apply Paper Shader"))
            {
                EditorPrefs.SetString(PrefsKey_Url, url);
                EditorCoroutineRunner.Run(FetchAndApply(url.Trim(), null, this));
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Option B — pull from your live session by code:");
            EditorGUILayout.HelpBox($"Hits {SessionApiBase}CODE and downloads the first non-null glbUrl.", MessageType.None);
            sessionCode = EditorGUILayout.TextField("Session Code", sessionCode);

            EditorGUI.BeginDisabledGroup(busy || string.IsNullOrWhiteSpace(sessionCode));
            if (GUILayout.Button("Fetch from Session API & Apply Paper Shader"))
            {
                EditorPrefs.SetString(PrefsKey_Code, sessionCode);
                EditorCoroutineRunner.Run(FetchFromSessionAndApply(sessionCode.Trim().ToUpperInvariant(), this));
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        // Set status from inside coroutines (forces a window repaint).
        private void SetStatus(string s)
        {
            status = s;
            Debug.Log("[FetchPaperize] " + s);
            Repaint();
        }

        // ─────────────────────────────────────────────────────────────────
        private IEnumerator FetchFromSessionAndApply(string code, TigerverseFetchPaperize owner)
        {
            owner.busy = true;
            owner.SetStatus($"GET {SessionApiBase}{code}");

            string apiUrl = SessionApiBase + code;
            using var req = UnityWebRequest.Get(apiUrl);
            req.timeout = 15;
            var op = req.SendWebRequest();
            while (!op.isDone) yield return null;

#if UNITY_2020_1_OR_NEWER
            bool err = req.result != UnityWebRequest.Result.Success;
#else
            bool err = req.isNetworkError || req.isHttpError;
#endif
            if (err)
            {
                owner.SetStatus($"Session fetch failed: HTTP {req.responseCode} {req.error}");
                owner.busy = false; yield break;
            }

            string json = req.downloadHandler.text;
            string glb = ExtractFirstField(json, "glbUrl");
            string img = ExtractFirstField(json, "imageUrl");
            owner.SetStatus($"Session API replied. glbUrl={(glb ?? "<null>")}");

            if (string.IsNullOrEmpty(glb))
            {
                owner.SetStatus("No glbUrl in session — did either player submit a drawing yet?");
                owner.busy = false; yield break;
            }

            // Pump the nested coroutine inline.
            var inner = FetchAndApply(glb, img, owner);
            while (inner.MoveNext()) yield return null;
            owner.busy = false;
        }

        // ─────────────────────────────────────────────────────────────────
        private IEnumerator FetchAndApply(string glbUrl, string imageUrl, TigerverseFetchPaperize owner)
        {
            owner.busy = true;
            owner.SetStatus($"Downloading GLB ({glbUrl.Substring(0, System.Math.Min(60, glbUrl.Length))}…)");

            byte[] glbBytes = null;
            using (var req = UnityWebRequest.Get(glbUrl))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 60;
                var op = req.SendWebRequest();
                while (!op.isDone) yield return null;

#if UNITY_2020_1_OR_NEWER
                bool err = req.result != UnityWebRequest.Result.Success;
#else
                bool err = req.isNetworkError || req.isHttpError;
#endif
                if (err || req.responseCode >= 400)
                {
                    owner.SetStatus($"GLB download failed: HTTP {req.responseCode} {req.error}");
                    owner.busy = false; yield break;
                }
                glbBytes = req.downloadHandler.data;
            }
            owner.SetStatus($"Downloaded {(glbBytes?.Length ?? 0) / 1024} KB. Parsing with glTFast…");

            var gltf = new GltfImport();
            var loadTask = gltf.LoadGltfBinary(glbBytes);
            while (!loadTask.IsCompleted) yield return null;
            if (loadTask.IsFaulted || (loadTask.IsCompleted && loadTask.Result == false))
            {
                owner.SetStatus("glTFast LoadGltfBinary failed.");
                owner.busy = false; yield break;
            }

            var container = new GameObject("PaperTest_FetchedGLB");
            Vector3 spawnPos = new Vector3(0, 0, 1.5f);
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
                spawnPos = sv.camera.transform.position + sv.camera.transform.forward * 1.5f;
            container.transform.position = spawnPos;

            owner.SetStatus("Instantiating main scene…");
            var instTask = gltf.InstantiateMainSceneAsync(container.transform);
            while (!instTask.IsCompleted) yield return null;
            if (instTask.IsFaulted || (instTask.IsCompleted && instTask.Result == false))
            {
                owner.SetStatus("glTFast InstantiateMainSceneAsync failed.");
                Object.DestroyImmediate(container);
                owner.busy = false; yield break;
            }

            AutoScale(container.transform, 0.8f);
            owner.SetStatus("Auto-scaled. Fetching drawing image…");

            Texture2D drawingTex = MakeFallbackTexture();
            if (!string.IsNullOrEmpty(imageUrl))
            {
                using var imgReq = UnityWebRequestTexture.GetTexture(imageUrl);
                imgReq.timeout = 30;
                var iop = imgReq.SendWebRequest();
                while (!iop.isDone) yield return null;
#if UNITY_2020_1_OR_NEWER
                bool ie = imgReq.result != UnityWebRequest.Result.Success;
#else
                bool ie = imgReq.isNetworkError || imgReq.isHttpError;
#endif
                if (!ie && imgReq.responseCode < 400)
                    drawingTex = DownloadHandlerTexture.GetContent(imgReq);
            }

            DrawingColorize.Apply(container, drawingTex, drawingStrength: 0.55f);

            Selection.activeObject = container;
            owner.SetStatus($"Done. Spawned '{container.name}' at {spawnPos}. Tweak its material in the Inspector.");
            owner.busy = false;
        }

        // Simple non-regex JSON field extractor — matches "key":"value" on first occurrence.
        private static string ExtractFirstField(string json, string key)
        {
            string needle = "\"" + key + "\":\"";
            int idx = json.IndexOf(needle, System.StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + needle.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        private static Texture2D MakeFallbackTexture()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var px = new Color32[64 * 64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static void AutoScale(Transform root, float targetMaxAxisMeters)
        {
            var rends = root.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            Bounds b = rends[0].bounds; bool first = false;
            foreach (var r in rends) { if (first) b.Encapsulate(r.bounds); else { b = r.bounds; first = true; } }
            if (b.size == Vector3.zero) return;
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (longest < 1e-6f) return;
            root.localScale *= targetMaxAxisMeters / longest;
        }
    }

    /// <summary>
    /// Editor coroutine pump. Calls MoveNext() every editor update tick.
    /// </summary>
    internal static class EditorCoroutineRunner
    {
        private static readonly List<IEnumerator> _running = new List<IEnumerator>();
        private static bool _hooked;

        public static void Run(IEnumerator co)
        {
            if (co == null) return;
            _running.Add(co);
            if (!_hooked) { EditorApplication.update += Tick; _hooked = true; }
        }

        private static void Tick()
        {
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                bool keep = false;
                try { keep = _running[i].MoveNext(); }
                catch (System.Exception e) { Debug.LogException(e); }
                if (!keep) _running.RemoveAt(i);
            }
            if (_running.Count == 0) { EditorApplication.update -= Tick; _hooked = false; }
        }
    }
}
#endif
