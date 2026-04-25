#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    public static class TigerverseLobbyEnvironment
    {
        [MenuItem("Tigerverse/Lobby -> Add Floor + Spawn Markers")]
        public static void Apply()
        {
            string[] scenes = {
                "Assets/_Project/Scenes/Title.unity",
                "Assets/_Project/Scenes/Lobby.unity",
                "Assets/_Project/Scenes/Battle.unity",
            };

            foreach (var p in scenes)
            {
                if (!System.IO.File.Exists(p)) continue;
                var s = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
                BuildEnv();
                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
                Debug.Log($"[Tigerverse] Built environment in {p}");
            }
        }

        private static void BuildEnv()
        {
            // Reuse if already present.
            if (GameObject.Find("LobbyEnv") != null) return;

            var root = new GameObject("LobbyEnv");

            // Floor: 10x10 cube, slightly below ground plane.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -0.05f, 0);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.color = new Color(0.25f, 0.28f, 0.32f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // Grid lines so players can perceive movement against the floor.
            int gridLines = 9;
            for (int i = -gridLines/2; i <= gridLines/2; i++)
            {
                if (i == 0) continue;
                var lineX = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lineX.name = $"GridX{i}";
                lineX.transform.SetParent(root.transform, false);
                lineX.transform.localPosition = new Vector3(i, 0.001f, 0);
                lineX.transform.localScale = new Vector3(0.02f, 0.02f, 10f);
                var lineMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                lineMat.color = new Color(0.4f, 0.45f, 0.5f);
                lineX.GetComponent<Renderer>().sharedMaterial = lineMat;
                Object.DestroyImmediate(lineX.GetComponent<Collider>());

                var lineZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lineZ.name = $"GridZ{i}";
                lineZ.transform.SetParent(root.transform, false);
                lineZ.transform.localPosition = new Vector3(0, 0.001f, i);
                lineZ.transform.localScale = new Vector3(10f, 0.02f, 0.02f);
                lineZ.GetComponent<Renderer>().sharedMaterial = lineMat;
                Object.DestroyImmediate(lineZ.GetComponent<Collider>());
            }

            // Two spawn markers — colored discs the players will stand on.
            CreateSpawnMarker(root.transform, "SpawnP0", new Vector3(-1.2f, 0.01f, 0), new Color(0.2f, 0.7f, 1f));
            CreateSpawnMarker(root.transform, "SpawnP1", new Vector3( 1.2f, 0.01f, 0), new Color(1f, 0.4f, 0.6f));
        }

        private static void CreateSpawnMarker(Transform parent, string name, Vector3 pos, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(0.6f, 0.005f, 0.6f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = tint;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
#endif
