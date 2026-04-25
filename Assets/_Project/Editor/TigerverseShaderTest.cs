#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Tigerverse.Drawing;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Spawns a test sphere/capsule/cube in the active scene with a
    /// procedurally-generated test drawing applied via DrawingColorize.
    /// Lets you iterate on the paper/pencil shader without going through
    /// the full Meshy pipeline.
    /// </summary>
    public static class TigerverseShaderTest
    {
        [MenuItem("Tigerverse/Test -> Spawn Shader Test Sphere (red drawing)")]
        public static void SpawnSphereRed() => Spawn(PrimitiveType.Sphere, MakeBlobDrawing(Color.red), "TestSphere_Red");

        [MenuItem("Tigerverse/Test -> Spawn Shader Test Capsule (blue drawing)")]
        public static void SpawnCapsuleBlue() => Spawn(PrimitiveType.Capsule, MakeBlobDrawing(new Color(0.2f, 0.4f, 1f)), "TestCapsule_Blue");

        [MenuItem("Tigerverse/Test -> Spawn Shader Test Cube (green drawing)")]
        public static void SpawnCubeGreen() => Spawn(PrimitiveType.Cube, MakeBlobDrawing(new Color(0.2f, 0.85f, 0.35f)), "TestCube_Green");

        [MenuItem("Tigerverse/Test -> Spawn Shader Test Sphere (yellow drawing)")]
        public static void SpawnSphereYellow() => Spawn(PrimitiveType.Sphere, MakeBlobDrawing(new Color(1f, 0.92f, 0.2f)), "TestSphere_Yellow");

        [MenuItem("Tigerverse/Test -> Clear All Test Spawns")]
        public static void ClearAll()
        {
            int n = 0;
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go != null && go.name.StartsWith("Test"))
                {
                    Object.DestroyImmediate(go);
                    n++;
                }
            }
            Debug.Log($"[Tigerverse] Cleared {n} test spawn(s).");
        }

        private static void Spawn(PrimitiveType prim, Texture2D drawing, string name)
        {
            // Place ~1.2m in front of the scene-view camera so it's immediately visible.
            Vector3 spawnPos = Vector3.zero + Vector3.up * 1.2f + Vector3.forward * 1.5f;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                var cam = sceneView.camera;
                spawnPos = cam.transform.position + cam.transform.forward * 1.5f;
            }

            var go = GameObject.CreatePrimitive(prim);
            go.name = name;
            go.transform.position = spawnPos;
            go.transform.localScale = Vector3.one * 0.6f;
            // Drop the collider — not needed for visual testing.
            Object.DestroyImmediate(go.GetComponent<Collider>());

            DrawingColorize.Apply(go, drawing, drawingStrength: 0.22f);

            Selection.activeObject = go;
            Debug.Log($"[Tigerverse] Spawned '{name}' at {spawnPos} with paper-craft material. Pick another color from the Tigerverse → Test menu, or play with the material's properties live.");
        }

        // Cheap procedural "drawing" — a soft-edged blob of the chosen ink color
        // on a white background. Looks plausible to the dominant-color sampler.
        private static Texture2D MakeBlobDrawing(Color ink)
        {
            const int SIZE = 256;
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[SIZE * SIZE];

            float cx = SIZE * 0.5f, cy = SIZE * 0.5f;
            float blobR = SIZE * 0.32f;

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Soft falloff disc + a few "details" via sine ripples.
                    float blob = Mathf.Clamp01(1f - d / blobR);
                    float ripple = 0.5f + 0.5f * Mathf.Sin(x * 0.07f) * Mathf.Cos(y * 0.06f);
                    float alpha = Mathf.Clamp01(blob * blob + 0.08f * ripple * blob);
                    Color c = Color.Lerp(Color.white, ink, alpha);
                    px[y * SIZE + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
#endif
