using Unity.XR.CoreUtils;
using UnityEngine;

namespace TigerVerse.Core
{
    /// <summary>
    /// Auto-spawns a default BoundaryWalls instance whenever a scene containing
    /// an XR Origin loads, unless one already exists in the scene.
    /// </summary>
    public static class BoundaryWallsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            // If the scene author has placed/configured one, leave it alone.
#if UNITY_2023_1_OR_NEWER
            BoundaryWalls existing = Object.FindFirstObjectByType<BoundaryWalls>(FindObjectsInactive.Include);
#else
            BoundaryWalls existing = Object.FindObjectOfType<BoundaryWalls>(true);
#endif
            if (existing != null) return;

            // Only spawn boundaries in scenes that actually have an XR rig.
#if UNITY_2023_1_OR_NEWER
            XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
#else
            XROrigin xrOrigin = Object.FindObjectOfType<XROrigin>(true);
#endif
            if (xrOrigin == null) return;

            GameObject go = new GameObject("RuntimeBoundaryWalls");
            // Place at world origin (XR rig spawn area).
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            BoundaryWalls walls = go.AddComponent<BoundaryWalls>();
            walls.Configure(Vector3.zero, new Vector2(8f, 8f), 4f);
        }
    }
}
