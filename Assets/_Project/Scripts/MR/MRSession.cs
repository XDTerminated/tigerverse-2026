using UnityEngine;
#if UNITY_XR_ARFOUNDATION || UNITY_XR_META_OPENXR
using UnityEngine.XR.ARFoundation;
#endif

namespace Tigerverse.MR
{
    /// <summary>
    /// Flips the headset between fully-rendered VR and Meta passthrough MR.
    /// Driven by the post-hatch ReadyHandshake: when both players fist-bump
    /// the lobby room is hidden, the camera goes transparent so passthrough
    /// shows through, and combat plays out in their real living room with
    /// monsters anchored to the bump midpoint on the floor.
    ///
    /// Reverse with Exit() if we ever want to drop back into VR.
    ///
    /// Passthrough itself is provided by com.unity.xr.meta-openxr (the
    /// "Meta Quest: Passthrough" OpenXR feature must be enabled in
    /// Project Settings → XR Plug-in Management → OpenXR → Android).
    /// We don't reference any Meta API types here directly, so this script
    /// stays compileable even before the package import finishes.
    /// </summary>
    public static class MRSession
    {
        public static bool InMR { get; private set; }
        public static Transform ArenaAnchor { get; private set; }

        // Cached so Exit() can put things back the way they were.
        private static CameraClearFlags _savedClearFlags;
        private static Color            _savedBackground;
        private static GameObject       _savedLobbyEnv;
        private static bool             _savedSkyboxState;

        /// <summary>
        /// Switch the local headset into passthrough MR. Hides LobbyEnv,
        /// makes the camera background transparent, and stores the supplied
        /// arena anchor so monster-spawn pivots can reparent under it.
        /// </summary>
        public static void Enter(Transform arenaAnchor)
        {
            if (InMR) return;

            // Flat-screen / editor-without-HMD guard. Passthrough requires an
            // actual XR display subsystem to be running — without one, the
            // DisableOtherRigs() pass below would kill the only rendering
            // camera in the scene and the player would just see Unity's
            // "No cameras rendering" placeholder. Skip cleanly so laptop
            // dev testing still works.
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                Debug.Log("[MRSession] Enter() skipped — no active XR display (flat-screen / editor without HMD).");
                return;
            }

            ArenaAnchor = arenaAnchor;

#if UNITY_XR_ARFOUNDATION || UNITY_XR_META_OPENXR
            // BattleMR is loaded additively, so the original Title-scene
            // XR rig + Main Camera are STILL ALIVE. Both cameras render
            // every frame, and the lobby camera (opaque background, no
            // ARCameraBackground) paints over the AR camera's passthrough
            // every frame → screen looks black. Solution: completely
            // disable the original rig + camera before flipping into MR.
            //
            // We disable instead of destroy so Exit() could put it back if
            // we ever wanted to drop out of passthrough mid-session.
            DisableOtherRigs();

            // The AR camera in BattleMR is the camera tagged MainCamera
            // *now* (after the original rig was disabled). Resolve it
            // fresh — Camera.main caches across frames sometimes.
            Camera arCam = ResolveARCamera();
            if (arCam != null)
            {
                _savedClearFlags = arCam.clearFlags;
                _savedBackground = arCam.backgroundColor;
                arCam.clearFlags = CameraClearFlags.SolidColor;
                arCam.backgroundColor = new Color(0f, 0f, 0f, 0f);

                // Force-enable the AR Camera Background renderer. AR
                // Foundation only flips this on if the subsystem started
                // cleanly; sometimes you have to nudge it.
                var bg = arCam.GetComponent<ARCameraBackground>();
                if (bg != null) bg.enabled = true;
                var cm = arCam.GetComponent<ARCameraManager>();
                if (cm != null) cm.enabled = true;
            }

            _savedSkyboxState = RenderSettings.skybox != null;
            RenderSettings.skybox = null;

            _savedLobbyEnv = GameObject.Find("LobbyEnv");
            if (_savedLobbyEnv != null) _savedLobbyEnv.SetActive(false);

            // Make sure an ARSession exists and is enabled. The session
            // baked into BattleMR usually covers this, but if MRSession
            // was called outside the scene (e.g. dev test), spawn one.
            var arSession = Object.FindFirstObjectByType<ARSession>();
            if (arSession == null)
            {
                var sessGo = new GameObject("ARSession (Runtime)", typeof(ARSession), typeof(ARInputManager));
                Object.DontDestroyOnLoad(sessGo);
                arSession = sessGo.GetComponent<ARSession>();
            }
            if (arSession != null) arSession.enabled = true;

            InMR = true;
            Debug.Log($"[MRSession] Passthrough ON. ArenaAnchor at {(arenaAnchor != null ? arenaAnchor.position.ToString("F2") : "null")}.");
#else
            Debug.Log("[MRSession] Enter() called without Meta-OpenXR package — staying in VR (no passthrough source available).");
#endif
        }

#if UNITY_XR_ARFOUNDATION || UNITY_XR_META_OPENXR
        // Disable only the CAMERAS outside BattleMR so the AR camera is
        // the sole renderer. Leave XR Origin GameObjects alive — disabling
        // them tears down their InputActionManager / TrackedPoseDriver
        // wiring and the Quest's controllers + head tracking go dead, which
        // is what produced the "black + white circle" symptom (the white
        // circle was the OS recenter prompt because head tracking died).
        private static void DisableOtherRigs()
        {
            var mrScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("BattleMR");
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam == null) continue;
                if (mrScene.IsValid() && cam.gameObject.scene == mrScene) continue;
                cam.enabled = false;
            }
        }

        private static Camera ResolveARCamera()
        {
            // Prefer the camera that has ARCameraManager attached.
            foreach (var cm in Object.FindObjectsByType<ARCameraManager>(FindObjectsSortMode.None))
            {
                var c = cm.GetComponent<Camera>();
                if (c != null) return c;
            }
            return Camera.main;
        }
#endif

        /// <summary>
        /// Drop back to VR — restore camera/skybox/LobbyEnv. Safe to call
        /// even if Enter never ran (no-op).
        /// </summary>
        public static void Exit()
        {
            if (!InMR) return;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = _savedClearFlags;
                cam.backgroundColor = _savedBackground;
            }
            if (_savedLobbyEnv != null) _savedLobbyEnv.SetActive(true);
            InMR = false;
            ArenaAnchor = null;
            Debug.Log("[MRSession] Passthrough OFF, back in VR.");
        }
    }
}
