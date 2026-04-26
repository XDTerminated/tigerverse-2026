#if UNITY_EDITOR
using System.IO;
using Tigerverse.MR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// One-click setup + test harness for the MR battle scene.
    ///
    ///   Tigerverse → MR → Create / Rebuild BattleMR Scene
    ///       Generates Assets/_Project/Scenes/BattleMR.unity from scratch
    ///       with the AR rig pre-wired (XR Origin + ARSession +
    ///       ARCameraManager + ARCameraBackground + a transparent camera).
    ///       Run this once after pulling the project, or any time the AR
    ///       rig wiring needs to be regenerated.
    ///
    ///   Tigerverse → MR → Jump To MR (Test, in Play mode)
    ///       While Play mode is running in the Title scene, this loads
    ///       BattleMR additively and triggers MRSession.Enter() so you can
    ///       eyeball passthrough + the arena anchor without going through
    ///       the full lobby → hatch → ready handshake sequence.
    ///
    /// The generated scene contains:
    ///   - XR Origin (AR Rig) with Camera Offset + Main Camera
    ///   - ARSession, ARInputManager
    ///   - ARCameraManager + ARCameraBackground on the Main Camera
    ///   - Camera background = transparent, skybox = none
    ///   - BattleArenaAnchor at world origin (gets repositioned at
    ///     runtime by ReadyHandshake's bump-midpoint logic)
    ///   - Directional Light
    /// </summary>
    public static class TigerverseMRSceneSetup
    {
        private const string MRScenePath = "Assets/_Project/Scenes/BattleMR.unity";

        [MenuItem("Tigerverse/MR -> Create / Rebuild BattleMR Scene")]
        public static void CreateOrRebuildScene()
        {
            // Save current scene first if it's dirty.
            var active = EditorSceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("[TigerverseMRSceneSetup] Cancelled — current scene save was declined.");
                    return;
                }
            }

            string dir = Path.GetDirectoryName(MRScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildSceneContents();

            EditorSceneManager.MarkSceneDirty(scene);
            bool ok = EditorSceneManager.SaveScene(scene, MRScenePath);
            if (ok) Debug.Log($"[TigerverseMRSceneSetup] Wrote {MRScenePath}.");
            else    Debug.LogError($"[TigerverseMRSceneSetup] Failed to save {MRScenePath}.");

            EnsureSceneInBuildSettings(MRScenePath);
        }

        // Editor-time runtime test: while in Play mode in the Title scene,
        // this loads BattleMR additively (so the network runner survives)
        // and immediately fires the MR transition.
        [MenuItem("Tigerverse/MR -> Jump To MR (Test, in Play mode)")]
        public static void JumpToMRForTesting()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Press Play first",
                    "This shortcut only works during Play mode. Hit Play in the Title scene, then run this menu item again.",
                    "OK");
                return;
            }

            // Load additively so any DontDestroyOnLoad networking lives on.
            SceneManager.LoadSceneAsync("BattleMR", LoadSceneMode.Additive).completed += _ =>
            {
                var anchorGo = GameObject.Find("BattleArenaAnchor");
                Transform anchor = anchorGo != null ? anchorGo.transform : null;
                MRSession.Enter(anchor);
                Debug.Log("[TigerverseMRSceneSetup] Test jump: BattleMR loaded additively, MRSession.Enter() called.");
            };
        }

        // ─── Scene builder ──────────────────────────────────────────────
        private static void BuildSceneContents()
        {
            // Directional light.
            var light = new GameObject("Directional Light");
            var l = light.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.0f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Battle arena anchor — placeholder transform that ReadyHandshake
            // repositions at runtime to the bump midpoint, snapped to floor.
            new GameObject("BattleArenaAnchor");

            // XR Origin (AR-style).
            var rig = new GameObject("XR Origin (AR Rig)");
            var camOffset = new GameObject("Camera Offset");
            camOffset.transform.SetParent(rig.transform, false);
            camOffset.transform.localPosition = new Vector3(0, 0, 0); // floor-tracking → 0

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(camOffset.transform, false);

            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;

            // Try to attach AR Foundation + XR Origin components if the
            // package is present. We use AddComponent by Type lookup so
            // this script still compiles cleanly when the package isn't.
            var xrOrigin   = TryAddComponentByName(rig, "Unity.XR.CoreUtils.XROrigin");
            TryAddComponentByName(rig, "UnityEngine.XR.ARFoundation.ARSession");
            TryAddComponentByName(rig, "UnityEngine.XR.ARFoundation.ARInputManager");
            TryAddComponentByName(camGo, "UnityEngine.XR.ARFoundation.ARCameraManager");
            TryAddComponentByName(camGo, "UnityEngine.XR.ARFoundation.ARCameraBackground");

            // Wire up XROrigin's required references — camera + camera
            // offset object + Floor tracking origin. Without these the AR
            // subsystem can't find the camera to drive passthrough into,
            // ARCameraBackground.backgroundRenderingEnabled stays false,
            // and you get a black screen instead of passthrough.
            if (xrOrigin != null)
            {
                var so = new SerializedObject(xrOrigin);
                var pCam = so.FindProperty("m_Camera");
                if (pCam != null) pCam.objectReferenceValue = camGo.GetComponent<Camera>();
                var pOffset = so.FindProperty("m_CameraFloorOffsetObject");
                if (pOffset != null) pOffset.objectReferenceValue = camOffset;
                var pMode = so.FindProperty("m_RequestedTrackingOriginMode");
                if (pMode != null) pMode.intValue = 3; // Floor
                var pYOff = so.FindProperty("m_CameraYOffset");
                if (pYOff != null) pYOff.floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Skybox off — nothing should render behind passthrough.
            RenderSettings.skybox = null;
        }

        private static Component TryAddComponentByName(GameObject go, string fullTypeName)
        {
            var t = System.Type.GetType(fullTypeName)
                ?? System.Type.GetType(fullTypeName + ", Unity.XR.CoreUtils")
                ?? System.Type.GetType(fullTypeName + ", Unity.XR.ARFoundation");
            if (t == null)
            {
                // Last resort: scan loaded assemblies.
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(fullTypeName);
                    if (t != null) break;
                }
            }
            if (t == null)
            {
                Debug.LogWarning($"[TigerverseMRSceneSetup] Type '{fullTypeName}' not found — package not installed? Skipping component on '{go.name}'.");
                return null;
            }
            return go.AddComponent(t);
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            var current = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < current.Count; i++)
            {
                if (string.Equals(current[i].path, scenePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (!current[i].enabled)
                    {
                        current[i].enabled = true;
                        EditorBuildSettings.scenes = current.ToArray();
                        Debug.Log($"[TigerverseMRSceneSetup] Enabled '{scenePath}' in Build Settings.");
                    }
                    return;
                }
            }
            current.Add(new EditorBuildSettingsScene(scenePath, enabled: true));
            EditorBuildSettings.scenes = current.ToArray();
            Debug.Log($"[TigerverseMRSceneSetup] Added '{scenePath}' to Build Settings.");
        }
    }
}
#endif
