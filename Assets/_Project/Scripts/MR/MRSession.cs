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
            ArenaAnchor = arenaAnchor;

#if UNITY_XR_ARFOUNDATION || UNITY_XR_META_OPENXR
            // Without the Meta-OpenXR package present we have no real
            // passthrough source, so flipping the skybox + LobbyEnv off
            // would leave the player staring at a black void instead of
            // their living room. Skip the entire MR transition in that
            // build — combat just continues in the existing VR lobby. The
            // package is OFF for the demo build because v1.0.3's native
            // plugin stack-overflows on Quest startup; re-add it (and
            // remove the guard) once a stable version ships.
            var cam = Camera.main;
            if (cam != null)
            {
                _savedClearFlags = cam.clearFlags;
                _savedBackground = cam.backgroundColor;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            _savedSkyboxState = RenderSettings.skybox != null;
            var oldSkybox = RenderSettings.skybox;
            RenderSettings.skybox = null;
            if (oldSkybox != null) Resources.UnloadAsset(oldSkybox);

            _savedLobbyEnv = GameObject.Find("LobbyEnv");
            if (_savedLobbyEnv != null) _savedLobbyEnv.SetActive(false);

            // The Meta-OpenXR package exposes passthrough through AR
            // Foundation's ARCameraManager + ARCameraBackground. Our rig
            // isn't an AR rig, so we add those components at runtime when
            // entering MR. (No-op if they're already present.)
            if (cam != null)
            {
                if (cam.GetComponent<ARCameraManager>() == null)
                    cam.gameObject.AddComponent<ARCameraManager>();
                if (cam.GetComponent<ARCameraBackground>() == null)
                    cam.gameObject.AddComponent<ARCameraBackground>();
            }
            // ARSession singleton — required by AR Foundation to actually
            // start any subsystem.
            if (Object.FindFirstObjectByType<ARSession>() == null)
            {
                var sess = new GameObject("ARSession", typeof(ARSession), typeof(ARInputManager));
                Object.DontDestroyOnLoad(sess);
            }

            InMR = true;
            Debug.Log($"[MRSession] Passthrough ON. ArenaAnchor at {(arenaAnchor != null ? arenaAnchor.position.ToString("F2") : "null")}.");
#else
            Debug.Log("[MRSession] Enter() called without Meta-OpenXR package — staying in VR (no passthrough source available).");
#endif
        }

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
