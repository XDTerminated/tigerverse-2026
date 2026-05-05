using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Visual ground that extends past the existing 40x40 Floor out to the
    /// mountain ring, plus four invisible barrier walls right at the Floor's
    /// edge so players can't walk into the unplayable extension area.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperGroundExtension : MonoBehaviour
    {
        [SerializeField] private float playableHalfExtent = 25f;
        [SerializeField] private float extensionHalfExtent = 250f;
        [SerializeField] private float barrierHeight = 4f;
        [SerializeField] private float groundY = -0.06f;

        private static readonly Color GroundCream = new Color(0.92f, 0.90f, 0.84f, 1f);

        // If true, the extension plane reuses the Floor's exact material so
        // the spawn baseplate reads as one continuous surface from origin to
        // the mountain ring (no visible seam at the playable boundary).
        [SerializeField] private bool matchFloorMaterial = true;

        public static GameObject Spawn(Vector3 center)
        {
            var go = new GameObject("PaperGroundExtension");
            go.transform.position = center;
            go.AddComponent<PaperGroundExtension>();
            return go;
        }

        private void Start()
        {
            BuildExtensionPlane();
            BuildBarriers();
        }

        private void BuildExtensionPlane()
        {
            // One large quad sits below the existing Floor; the original
            // 40x40 Floor renders on top within its bounds, and the extension
            // is only visible past that edge out to the mountain ring.
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "GroundExtension_Plane";
            var col = plane.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }

            plane.transform.SetParent(transform, worldPositionStays: false);
            plane.transform.localPosition = new Vector3(0f, groundY, 0f);
            plane.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plane.transform.localScale = new Vector3(extensionHalfExtent * 2f, extensionHalfExtent * 2f, 1f);

            var mr = plane.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material mat = null;
                if (matchFloorMaterial)
                {
                    var floor = GameObject.Find("Floor");
                    if (floor != null)
                    {
                        var floorRend = floor.GetComponent<Renderer>();
                        if (floorRend != null) mat = floorRend.sharedMaterial;
                    }
                }
                if (mat == null) mat = MakeUnlitOpaque(GroundCream);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private void BuildBarriers()
        {
            // Four walls flush with the Floor's outer edge. Slightly tucked
            // inside (offset 0.05) so a player at exactly playableHalfExtent
            // doesn't nudge through the wall plane.
            float t = 0.5f;
            float h = barrierHeight;
            float e = playableHalfExtent;
            float spanInner = (e * 2f) + (t * 2f);

            BuildWall("Barrier_North", new Vector3(0f,  h * 0.5f,  e + t * 0.5f), new Vector3(spanInner, h, t));
            BuildWall("Barrier_South", new Vector3(0f,  h * 0.5f, -e - t * 0.5f), new Vector3(spanInner, h, t));
            BuildWall("Barrier_East",  new Vector3( e + t * 0.5f, h * 0.5f, 0f),  new Vector3(t, h, spanInner));
            BuildWall("Barrier_West",  new Vector3(-e - t * 0.5f, h * 0.5f, 0f),  new Vector3(t, h, spanInner));
        }

        private void BuildWall(string name, Vector3 localPos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.isTrigger = false;
        }

        private static Material MakeUnlitOpaque(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            return mat;
        }
    }
}
