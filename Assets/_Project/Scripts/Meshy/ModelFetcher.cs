using System;
using System.Collections;
using System.Threading.Tasks;
using GLTFast;
using Tigerverse.Combat;
using Tigerverse.Drawing;
using Tigerverse.Net;
using Tigerverse.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace Tigerverse.Meshy
{
    /// <summary>
    /// Fetches a player's monster bundle (GLB + drawing image + cry audio) and
    /// instantiates it under a parent transform with auto-scale, drawing projection,
    /// cry audio, and either a humanoid animator or a procedural punch attacker.
    /// </summary>
    public class ModelFetcher : MonoBehaviour
    {
        public enum FetchError
        {
            None,
            NetworkGlb,
            NetworkImage,
            NetworkCry,
            GltfImport
        }

        private static Color ParseElementColor(string element)
        {
            switch ((element ?? "neutral").ToLowerInvariant())
            {
                case "fire":     return new Color(1.0f, 0.45f, 0.2f);
                case "water":    return new Color(0.3f, 0.6f, 1.0f);
                case "electric": return new Color(1.0f, 0.95f, 0.3f);
                case "earth":    return new Color(0.55f, 0.4f, 0.25f);
                case "grass":    return new Color(0.4f, 0.85f, 0.4f);
                case "ice":      return new Color(0.7f, 0.95f, 1.0f);
                case "dark":     return new Color(0.45f, 0.25f, 0.55f);
                default:         return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        [Tooltip("If false, skip humanoid avatar setup even when one is present (use procedural fallback).")]
        public bool runHumanoidImport = true;

        [Tooltip("Target longest-axis size in metres after auto-scale.")]
        public float targetSizeMeters = 0.6f;

        [Tooltip("Web request timeout in seconds for GLB / image / cry downloads.")]
        public int requestTimeoutSec = 30;

        [Tooltip("If true, paint the player's drawing across the GLB via flat planar DrawingProjector. " +
                 "Looks like a sticker — usually leave OFF.")]
        public bool applyDrawingProjection = false;

        [Tooltip("If true (recommended), tint the GLB by the drawing's dominant color and triplanar-project " +
                 "the drawing as a subtle overlay so the monster matches your doodle's color without flat-sticker artifacts.")]
        public bool applyDrawingColorize = true;

        [Tooltip("Drawing watermark hint on the front of the stylized shader. " +
                 "0 = pure tint+rim+noise (clean creature look). " +
                 "0.18 (default) = subtle doodle visible on front faces. " +
                 ">0.5 routes to the legacy triplanar wrap (busy).")]
        [Range(0,1)] public float drawingDetailStrength = 0.18f;

        [Header("Hatching Egg (3D wobble + crack + burst while monster loads)")]
        [Tooltip("If true, spawn a procedural paper egg that wobbles + cracks while the GLB loads, then explodes open with the monster popping out.")]
        public bool spawnHatchingEgg = true;
        [Tooltip("Egg hover height above the spawn pivot in metres.")]
        public float eggHoverHeight = 0.55f;
        [Tooltip("Minimum total seconds the egg shows before hatching. If the GLB loads faster than this, we wait so judges actually get to enjoy the wobble. Set 0 to hatch immediately when ready.")]
        public float eggMinShowSeconds = 3.0f;

        // Dev override: when set, ModelFetcher uses this as drawingTex instead
        // of fetching data.imageUrl. Cleared after one use. Set by editor-only
        // dev menus that want to test the egg+hatch flow without uploading
        // a real image to UploadThing.
        [System.NonSerialized] public Texture2D devOverrideDrawingTex;

        public IEnumerator Fetch(PlayerData data, Transform parent, Action<GameObject, FetchError> onComplete)
        {
            if (data == null)
            {
                onComplete?.Invoke(null, FetchError.NetworkGlb);
                yield break;
            }

            // Mock-mode shortcut: skip URL downloads, spawn a colored placeholder cube.
            // Triggered when BackendConfig.useMock is true OR when the URL is the example.com placeholder.
            var cfg = BackendConfig.Load();
            bool isMock = (cfg != null && cfg.useMock)
                          || (data.glbUrl != null && data.glbUrl.Contains("example.com"));
            if (isMock)
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.name = $"MonsterPlaceholder ({data.name ?? "monster"})";
                placeholder.transform.SetParent(parent, false);
                placeholder.transform.localPosition = Vector3.zero;
                placeholder.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);

                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Color tint = ParseElementColor(data.stats != null ? data.stats.element : "neutral");
                mat.color = tint;
                placeholder.GetComponent<Renderer>().sharedMaterial = mat;

                // Add MonsterCry stub so BattleManager.cryA/cryB references resolve cleanly.
                placeholder.AddComponent<Tigerverse.Combat.MonsterCry>();

                Debug.Log($"[ModelFetcher] MOCK: spawned placeholder for '{data.name}' under '{parent.name}'");
                onComplete?.Invoke(placeholder, FetchError.None);
                yield break;
            }

            if (string.IsNullOrEmpty(data.glbUrl))
            {
                Debug.LogError("[ModelFetcher] glbUrl missing.");
                onComplete?.Invoke(null, FetchError.NetworkGlb);
                yield break;
            }

            // 0) Pre-fetch drawing image first (small, ~50–200KB) so we can
            //    spawn the floating "drawing reveal" card immediately and
            //    have something visual happening during the slow GLB load.
            Texture2D drawingTex = devOverrideDrawingTex;
            if (drawingTex != null)
            {
                Debug.Log("[ModelFetcher] Using devOverrideDrawingTex (skipping imageUrl fetch).");
                devOverrideDrawingTex = null; // single-shot
            }
            if (drawingTex == null && !string.IsNullOrEmpty(data.imageUrl))
            {
                using (var imgReq = UnityWebRequestTexture.GetTexture(data.imageUrl))
                {
                    imgReq.timeout = requestTimeoutSec;
                    yield return imgReq.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                    bool imgErr = imgReq.result != UnityWebRequest.Result.Success;
#else
                    bool imgErr = imgReq.isNetworkError || imgReq.isHttpError;
#endif
                    if (imgErr || imgReq.responseCode >= 400)
                        Debug.LogWarning($"[ModelFetcher] Pre-fetch drawing image failed: HTTP {imgReq.responseCode} {imgReq.error}");
                    else
                        drawingTex = DownloadHandlerTexture.GetContent(imgReq);
                }
            }

            // 0b) Hatching egg: locate an existing one (spawned early by
            //     MonsterSpawnSlotPresenter the moment the player submitted)
            //     or spawn a new one now. Search broadly so we always find
            //     the slot-presenter's egg, even when the parent transform
            //     differs from the slot pivot it was spawned under.
            Tigerverse.UI.HatchingEggSequence egg = null;
            float eggSpawnTime = Time.time;
            if (parent != null)
            {
                // (a) Direct descendants of parent.
                egg = parent.GetComponentInChildren<Tigerverse.UI.HatchingEggSequence>(includeInactive: true);
                // (b) Walk up — slot presenter usually spawns under a sibling/grandparent of `parent`.
                Transform p = parent.parent;
                while (egg == null && p != null)
                {
                    egg = p.GetComponentInChildren<Tigerverse.UI.HatchingEggSequence>(includeInactive: true);
                    p = p.parent;
                }
                // (c) Fallback: closest egg in the scene within 2 m of parent.
                if (egg == null)
                {
                    var allEggs = UnityEngine.Object.FindObjectsByType<Tigerverse.UI.HatchingEggSequence>(FindObjectsSortMode.None);
                    float bestDist = 2f;
                    foreach (var e in allEggs)
                    {
                        if (e == null) continue;
                        float d = Vector3.Distance(e.transform.position, parent.position);
                        if (d < bestDist) { bestDist = d; egg = e; }
                    }
                }
            }

            if (egg != null)
            {
                // Refresh the drawing on the existing egg (slot presenter
                // didn't have a drawing texture yet when it spawned).
                egg.Configure(drawingTex);
                Debug.Log($"[ModelFetcher] Reusing existing egg '{egg.gameObject.name}' (under '{egg.transform.parent?.name ?? "<root>"}').");
            }
            else if (spawnHatchingEgg && parent != null)
            {
                var eggHost = new GameObject("HatchingEgg");
                eggHost.transform.SetParent(parent, worldPositionStays: false);
                eggHost.transform.localPosition = Vector3.up * eggHoverHeight;
                eggHost.transform.localRotation = Quaternion.identity;
                egg = eggHost.AddComponent<Tigerverse.UI.HatchingEggSequence>();
                egg.Configure(drawingTex);
                egg.progress01 = 0f;
                StartCoroutine(egg.PlayPopInAnimation());
                Debug.Log($"[ModelFetcher] Spawned NEW hatching egg under '{parent.name}'.");
            }
            else
            {
                Debug.LogWarning("[ModelFetcher] Hatching egg SKIPPED — spawnHatchingEgg=false or parent=null.");
            }

            // 1) Download the GLB binary.
            byte[] glbBytes = null;
            using (var req = UnityWebRequest.Get(data.glbUrl))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = requestTimeoutSec;
                yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                bool isError = req.result != UnityWebRequest.Result.Success;
#else
                bool isError = req.isNetworkError || req.isHttpError;
#endif
                if (isError || req.responseCode >= 400)
                {
                    Debug.LogError($"[ModelFetcher] GLB download failed: HTTP {req.responseCode} {req.error}");
                    onComplete?.Invoke(null, FetchError.NetworkGlb);
                    yield break;
                }

                glbBytes = req.downloadHandler.data;
            }

            if (glbBytes == null || glbBytes.Length == 0)
            {
                onComplete?.Invoke(null, FetchError.NetworkGlb);
                yield break;
            }

            if (egg != null) egg.progress01 = 0.40f;

            // 2) Parse with glTFast.
            var gltf = new GltfImport();
            var loadTask = gltf.LoadGltfBinary(glbBytes);
            yield return new WaitUntil(() => loadTask.IsCompleted);

            if (loadTask.IsFaulted || (loadTask.IsCompleted && loadTask.Result == false))
            {
                Debug.LogError("[ModelFetcher] glTFast LoadGltfBinary failed.");
                onComplete?.Invoke(null, FetchError.GltfImport);
                yield break;
            }

            if (egg != null) egg.progress01 = 0.65f;

            // 3) Create a container and instantiate the GLB scene under it.
            var container = new GameObject(string.IsNullOrEmpty(data.name) ? "Monster" : data.name);
            container.transform.SetParent(parent, worldPositionStays: false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;

            var instTask = gltf.InstantiateMainSceneAsync(container.transform);
            yield return new WaitUntil(() => instTask.IsCompleted);

            if (instTask.IsFaulted || (instTask.IsCompleted && instTask.Result == false))
            {
                Debug.LogError("[ModelFetcher] glTFast InstantiateMainSceneAsync failed.");
                Destroy(container);
                onComplete?.Invoke(null, FetchError.GltfImport);
                yield break;
            }

            // 3b) If we're using the egg, hide all monster renderers from the
            //     moment they exist. Otherwise the freshly-instantiated GLB is
            //     visible alongside (and inside) the egg during the entire
            //     wobble, ruining the surprise. The egg's BeginHatchSequence
            //     re-enables them at the moment of the burst.
            if (egg != null)
            {
                foreach (var r in container.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = false;
            }

            // 4) Auto-scale + recenter pivot to bottom-middle.
            AutoScaleAndCenter(container.transform, targetSizeMeters);
            if (egg != null) egg.progress01 = 0.85f;

            // 5) (Drawing image was pre-fetched at step 0 so the reveal card
            //    could spawn during the slow GLB load.)

            // 6) Apply drawing onto the mesh.
            // Preferred: triplanar colorize — uses the drawing's dominant color as the tint
            // and softly wraps drawing detail across 3 axes. Looks like the doodle, not a sticker.
            // Optional: flat planar projection (legacy DrawingProjector) for the original "sticker" look.
            try
            {
                if (drawingTex != null)
                {
                    if (applyDrawingColorize)
                    {
                        DrawingColorize.Apply(container, drawingTex, drawingDetailStrength);
                    }
                    else if (applyDrawingProjection)
                    {
                        var projector = container.GetComponent<DrawingProjector>();
                        if (projector == null) projector = container.AddComponent<DrawingProjector>();
                        projector.ApplyDrawing(drawingTex);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // 7) Cry audio: prefer the server-provided cryUrl, fall back to client-side generation.
            AudioClip cryClip = null;
            if (!string.IsNullOrEmpty(data.cryUrl))
            {
                using (var aReq = UnityWebRequestMultimedia.GetAudioClip(data.cryUrl, AudioType.MPEG))
                {
                    aReq.timeout = requestTimeoutSec;
                    yield return aReq.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                    bool aErr = aReq.result != UnityWebRequest.Result.Success;
#else
                    bool aErr = aReq.isNetworkError || aReq.isHttpError;
#endif
                    if (aErr || aReq.responseCode >= 400)
                    {
                        Debug.LogWarning($"[ModelFetcher] Cry audio download failed: HTTP {aReq.responseCode} {aReq.error}");
                    }
                    else
                    {
                        cryClip = DownloadHandlerAudioClip.GetContent(aReq);
                    }
                }
            }

            // Fallback: if no cry yet and we have a name + element, generate locally via ElevenLabs.
            if (cryClip == null && !string.IsNullOrEmpty(data.name))
            {
                string element = data.stats != null && !string.IsNullOrEmpty(data.stats.element) ? data.stats.element : "neutral";
                yield return Tigerverse.Voice.CryGenerator.Generate(data.name, element, c => cryClip = c);
                if (cryClip != null) Debug.Log($"[ModelFetcher] Generated cry locally for {data.name}.");
            }

            // 8) MonsterCry: get-or-add and set clip.
            var cry = container.GetComponent<MonsterCry>();
            if (cry == null) cry = container.AddComponent<MonsterCry>();
            try
            {
                if (cryClip != null) cry.SetClip(cryClip);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // 9 / 10) Animator setup or procedural fallback.
            bool humanoidConfigured = false;
            if (runHumanoidImport)
            {
                var animator = container.GetComponentInChildren<Animator>(true);
                if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                {
                    var ctrl = Resources.Load<RuntimeAnimatorController>("MonsterController");
                    if (ctrl != null)
                    {
                        animator.runtimeAnimatorController = ctrl;
                        humanoidConfigured = true;
                    }
                    else
                    {
                        Debug.LogWarning("[ModelFetcher] Humanoid avatar found but Resources/MonsterController controller missing.");
                    }
                }
            }

            if (!humanoidConfigured)
            {
                if (container.GetComponent<ProceduralPunchAttacker>() == null)
                {
                    container.AddComponent<ProceduralPunchAttacker>();
                }
            }

            // 9.5) Floating stats card — appears when local player hovers near
            //      the monster. Read-only display of HP/ATK/SPD/element/moves.
            if (container.GetComponent<Tigerverse.Combat.MonsterHoverStats>() == null)
            {
                var hover = container.AddComponent<Tigerverse.Combat.MonsterHoverStats>();
                hover.SetStats(data.stats);
            }

            // 11) Hatch the egg → reveal monster.
            if (egg != null)
            {
                // Hold a beat so judges see the egg idle/wobble before it
                // starts to crack. This eats up any leftover wait time.
                float elapsed = Time.time - eggSpawnTime;
                float remaining = Mathf.Max(0f, eggMinShowSeconds - elapsed);
                if (remaining > 0f) yield return new WaitForSeconds(remaining);

                // Animate cracks from 0 → 1 over 1.5s right before the
                // burst, so the egg visibly cracks open at the climactic
                // moment instead of having cracks slowly grow during the
                // entire wait.
                const float crackRampSec = 1.5f;
                float crampT = 0f;
                while (crampT < crackRampSec)
                {
                    crampT += Time.deltaTime;
                    egg.progress01 = Mathf.Clamp01(crampT / crackRampSec);
                    yield return null;
                }
                egg.progress01 = 1.0f;

                bool hatched = false;
                Vector3 spawnAt = parent.position + Vector3.up * 0.05f;
                StartCoroutine(egg.BeginHatchSequence(container, spawnAt, () => hatched = true));
                while (!hatched) yield return null;
            }

            // 12) Done.
            onComplete?.Invoke(container, FetchError.None);
        }

        private static void AutoScaleAndCenter(Transform root, float targetMaxAxisMeters)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            // Compute combined world-space bounds.
            Bounds combined = new Bounds(renderers[0].bounds.center, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (first)
                {
                    combined = r.bounds;
                    first = false;
                }
                else
                {
                    combined.Encapsulate(r.bounds);
                }
            }

            if (combined.size == Vector3.zero) return;

            float longest = Mathf.Max(combined.size.x, Mathf.Max(combined.size.y, combined.size.z));
            if (longest <= 1e-6f) return;

            float scale = targetMaxAxisMeters / longest;
            root.localScale = root.localScale * scale;

            // Recompute bounds after scaling and re-center pivot to bottom-middle.
            combined = new Bounds(renderers[0].bounds.center, Vector3.zero);
            first = true;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (first) { combined = r.bounds; first = false; }
                else combined.Encapsulate(r.bounds);
            }

            // Determine the world-space offset that would put the bottom-middle at the parent's origin.
            Vector3 bottomMiddleWorld = new Vector3(combined.center.x, combined.min.y, combined.center.z);
            Vector3 worldOffset = root.position - bottomMiddleWorld;
            root.position += worldOffset;
        }
    }
}
