using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Renders a character's head into a Texture2D so a dialogue panel can
    /// show the actual face instead of a placeholder. URP-safe: leaves the
    /// portrait camera enabled for one frame so URP's pipeline renders it
    /// (manual <c>Camera.Render()</c> is skipped by URP), then reads pixels.
    /// </summary>
    public static class HeadshotBaker
    {
        private static readonly string[] HeadBoneCandidates = {
            "Head", "head", "mixamorig:Head", "Bip01 Head", "Bone_Head"
        };

        private static readonly string[] HideMeshContains = {
            "Backpack", "Bag", "Cape", "Cloak", "Hat", "Helmet"
        };

        /// <summary>
        /// Bakes a portrait. Coroutine: yield until completion; the
        /// resulting Texture2D is delivered via <paramref name="onDone"/>.
        /// </summary>
        public static IEnumerator Bake(Transform modelRoot, System.Action<Texture2D> onDone, int size = 256)
        {
            if (modelRoot == null) { onDone?.Invoke(null); yield break; }

            // Wait long enough that the spawn animation has run and the
            // SkinnedMeshRenderer bones / bounds are valid for at least
            // one main-camera tick.
            yield return null;
            yield return null;

            Transform head = FindHead(modelRoot);
            if (head == null)
            {
                Debug.LogWarning($"[HeadshotBaker] No head bone found under '{modelRoot.name}'.");
                onDone?.Invoke(null);
                yield break;
            }

            // ---------- Disable everything else in the scene ----------
            var modelRends = modelRoot.GetComponentsInChildren<Renderer>(true);
            var modelRendSet = new HashSet<Renderer>(modelRends);
            var allRends = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var sceneToRestore = new List<Renderer>();
            foreach (var r in allRends)
            {
                if (r == null || modelRendSet.Contains(r) || !r.enabled) continue;
                sceneToRestore.Add(r);
                r.enabled = false;
            }

            // Hide accessory meshes that block the face.
            var accessoryDisabled = new List<Renderer>();
            foreach (var r in modelRends)
            {
                if (r == null || !r.enabled) continue;
                if (ShouldHide(r.name))
                {
                    accessoryDisabled.Add(r);
                    r.enabled = false;
                }
            }

            // Force scale=1 in case spawn animation has the model at 0.
            Vector3 savedScale = modelRoot.localScale;
            modelRoot.localScale = Vector3.one;

            // ---------- Render texture ----------
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                name = "PortraitRT",
                antiAliasing = 1
            };
            rt.Create();

            // ---------- Temporary key light ----------
            var lightGo = new GameObject("PortraitKeyLight");
            var keyLight = lightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = Color.white;

            // ---------- Camera ----------
            var camGo = new GameObject("PortraitCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags        = CameraClearFlags.SolidColor;
            cam.backgroundColor   = new Color(0.92f, 0.88f, 0.78f, 1f);
            cam.cullingMask       = ~0;
            cam.orthographic      = true;
            cam.orthographicSize  = 0.20f;
            cam.nearClipPlane     = 0.01f;
            cam.farClipPlane      = 5f;
            cam.targetTexture     = rt;
            cam.useOcclusionCulling = false;
            cam.allowHDR          = false;
            cam.allowMSAA         = false;
            cam.depth             = 1000; // render after main; harmless because it has its own RT

            // URP requires UniversalAdditionalCameraData on every camera
            // that's processed by the pipeline. Without it, URP silently
            // skips the camera and the RT stays at clear color.
            #if UNITY_2022_OR_NEWER || UNITY_6000_0_OR_NEWER
            // (always-true; the type exists once the package is installed)
            #endif
            var urpData = camGo.GetComponent("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (urpData == null)
            {
                // Add via reflection so the script still compiles in
                // projects that use a different render pipeline.
                var t = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (t != null) camGo.AddComponent(t);
            }

            // Position the camera in front of the figure's face.
            // Try +modelRoot.forward first; if the bake comes back empty
            // (figure was actually facing the other way), retry mirrored.
            Vector3 headWorld = head.position;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector3 faceDir = (attempt == 0 ? 1f : -1f) * modelRoot.forward;
                camGo.transform.position = headWorld + faceDir * 0.5f + Vector3.up * 0.04f;
                camGo.transform.LookAt(headWorld, Vector3.up);

                // Match the light to the camera so the face is lit.
                lightGo.transform.position = camGo.transform.position;
                lightGo.transform.rotation = camGo.transform.rotation;

                // Let URP render the camera through its pipeline by
                // waiting until end-of-frame with the camera enabled.
                yield return new WaitForEndOfFrame();

                // Read pixels.
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var probeTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                probeTex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                probeTex.Apply();
                RenderTexture.active = prevActive;

                if (HasFigureContent(probeTex))
                {
                    // Cleanup and return.
                    Object.DestroyImmediate(camGo);
                    Object.DestroyImmediate(lightGo);
                    rt.Release();
                    Object.Destroy(rt);
                    modelRoot.localScale = savedScale;
                    foreach (var r in sceneToRestore) if (r != null) r.enabled = true;
                    foreach (var r in accessoryDisabled) if (r != null) r.enabled = true;

                    onDone?.Invoke(probeTex);
                    yield break;
                }

                // Empty render — try the other direction next.
                Object.Destroy(probeTex);
            }

            // Both directions came back empty. Cleanup, return null.
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            rt.Release();
            Object.Destroy(rt);
            modelRoot.localScale = savedScale;
            foreach (var r in sceneToRestore) if (r != null) r.enabled = true;
            foreach (var r in accessoryDisabled) if (r != null) r.enabled = true;
            Debug.LogWarning("[HeadshotBaker] Both bake attempts returned empty — model may not have rendered. Falling back to no portrait.");
            onDone?.Invoke(null);
        }

        // Returns true if the texture has any pixel that's clearly not the
        // clear color (i.e., the figure rendered into it). We sample the
        // central 60% so panel edges don't fool us.
        private static bool HasFigureContent(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            int x0 = w / 5, x1 = w - x0;
            int y0 = h / 5, y1 = h - y0;
            Color clear = new Color(0.92f, 0.88f, 0.78f, 1f);
            int hits = 0, total = 0;
            for (int y = y0; y < y1; y += 4)
            {
                for (int x = x0; x < x1; x += 4)
                {
                    var c = tex.GetPixel(x, y);
                    total++;
                    float dr = c.r - clear.r;
                    float dg = c.g - clear.g;
                    float db = c.b - clear.b;
                    if (dr * dr + dg * dg + db * db > 0.015f) hits++;
                }
            }
            // > 5% of probed pixels are not the clear color → real content.
            return total > 0 && (hits * 100) / total > 5;
        }

        private static Transform FindHead(Transform root)
        {
            foreach (var name in HeadBoneCandidates)
            {
                var hit = FindByName(root, name);
                if (hit != null) return hit;
            }
            return FindContains(root, "head");
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static Transform FindContains(Transform root, string substr)
        {
            if (root.name.ToLower().Contains(substr)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindContains(root.GetChild(i), substr);
                if (hit != null) return hit;
            }
            return null;
        }

        private static bool ShouldHide(string rendererName)
        {
            foreach (var s in HideMeshContains)
                if (rendererName.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
