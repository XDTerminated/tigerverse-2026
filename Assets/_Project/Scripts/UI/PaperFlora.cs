using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Procedurally scatters paper-craft flora (grass tufts, simple flowers,
    /// small mushrooms) on the ground around its position. All built from
    /// Unity primitives with URP/Unlit materials so the scene feels populated
    /// without shipping any external models.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperFlora : MonoBehaviour
    {
        [SerializeField] private float radius = 6f;
        [SerializeField] private int count = 40;
        [SerializeField] private float minDistanceFromCenter = 1.5f;

        // Greyscale paper — every part of every flora item is a varying
        // shade of grey/white. No green, no pastels, no warm tones.
        private static readonly Color GrassGreen   = new Color(0.78f, 0.78f, 0.78f, 1f);
        private static readonly Color StemGreen    = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color MushroomCream= new Color(0.93f, 0.93f, 0.93f, 1f);

        private static readonly Color[] FlowerPalette = new Color[]
        {
            new Color(0.96f, 0.96f, 0.96f, 1f),
            new Color(0.86f, 0.86f, 0.86f, 1f),
            new Color(0.74f, 0.74f, 0.74f, 1f),
            new Color(1.00f, 1.00f, 1.00f, 1f),
        };

        private static readonly Color[] MushroomCapPalette = new Color[]
        {
            new Color(0.55f, 0.55f, 0.55f, 1f),
            new Color(0.42f, 0.42f, 0.42f, 1f),
            new Color(0.68f, 0.68f, 0.68f, 1f),
        };

        public static GameObject Spawn(Vector3 center, float radius = 6f, int count = 40)
        {
            var go = new GameObject("PaperFloraCoordinator");
            go.transform.position = center;
            var pf = go.AddComponent<PaperFlora>();
            pf.radius = radius;
            pf.count = count;
            return go;
        }

        private void Start()
        {
            int n = Mathf.Clamp(count, 30, 60);
            var bin = new GameObject("Flora");
            bin.transform.SetParent(transform, worldPositionStays: false);

            for (int i = 0; i < n; i++)
            {
                Vector2 disc = Random.insideUnitCircle * radius;
                if (disc.magnitude < minDistanceFromCenter)
                {
                    Vector2 dir = disc.sqrMagnitude > 1e-4f ? disc.normalized : new Vector2(1f, 0f);
                    disc = dir * (minDistanceFromCenter + Random.value * (radius - minDistanceFromCenter));
                }
                Vector3 pos = transform.position + new Vector3(disc.x, 0f, disc.y);
                float scale = Random.Range(0.8f, 1.2f);
                int kind = Random.Range(0, 3);

                GameObject piece;
                if (kind == 0)      piece = BuildGrassTuft();
                else if (kind == 1) piece = BuildFlower();
                else                piece = BuildMushroom();

                piece.transform.SetParent(bin.transform, worldPositionStays: false);
                piece.transform.position = pos;
                piece.transform.localScale = piece.transform.localScale * scale;
                piece.transform.Rotate(0f, Random.Range(0f, 360f), 0f, Space.World);
            }
        }

        // Grass tuft: 3-5 thin Quads in a fan.
        private GameObject BuildGrassTuft()
        {
            var root = new GameObject("GrassTuft");
            int blades = Random.Range(3, 6);
            float baseY = 0f;
            Material mat = MakeUnlitTransparent(GrassGreen);
            for (int i = 0; i < blades; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                StripCollider(quad);
                quad.name = "Blade" + i;
                quad.transform.SetParent(root.transform, worldPositionStays: false);
                float h = Random.Range(0.18f, 0.32f);
                float w = Random.Range(0.04f, 0.08f);
                quad.transform.localScale = new Vector3(w, h, 1f);
                quad.transform.localPosition = new Vector3(0f, baseY + h * 0.5f, 0f);
                float yaw = (i / (float)blades) * 180f + Random.Range(-15f, 15f);
                float tilt = Random.Range(-12f, 12f);
                quad.transform.localRotation = Quaternion.Euler(tilt, yaw, Random.Range(-8f, 8f));
                ApplyMaterial(quad, mat);
            }
            return root;
        }

        // Flower: stem cube + bloom quad.
        private GameObject BuildFlower()
        {
            var root = new GameObject("Flower");

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(stem);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, worldPositionStays: false);
            stem.transform.localScale = new Vector3(0.02f, 0.20f, 0.02f);
            stem.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            ApplyMaterial(stem, MakeUnlitOpaque(StemGreen));

            var bloom = GameObject.CreatePrimitive(PrimitiveType.Quad);
            StripCollider(bloom);
            bloom.name = "Bloom";
            bloom.transform.SetParent(root.transform, worldPositionStays: false);
            bloom.transform.localScale = new Vector3(0.10f, 0.10f, 1f);
            bloom.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            bloom.transform.localRotation = Quaternion.Euler(20f, Random.Range(0f, 360f), 0f);
            Color petalColor = FlowerPalette[Random.Range(0, FlowerPalette.Length)];
            ApplyMaterial(bloom, MakeUnlitTransparent(petalColor));

            return root;
        }

        // Mushroom: cylinder stem + sphere cap (visually a half-dome).
        private GameObject BuildMushroom()
        {
            var root = new GameObject("Mushroom");

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(stem);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, worldPositionStays: false);
            // Unity cylinder is 2m tall by default; scale.y of 0.03 = 6cm tall.
            stem.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            stem.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            ApplyMaterial(stem, MakeUnlitOpaque(MushroomCream));

            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCollider(cap);
            cap.name = "Cap";
            cap.transform.SetParent(root.transform, worldPositionStays: false);
            // Half-dome look: squash Y and seat it on top of the stem.
            cap.transform.localScale = new Vector3(0.10f, 0.05f, 0.10f);
            cap.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            Color capColor = MushroomCapPalette[Random.Range(0, MushroomCapPalette.Length)];
            ApplyMaterial(cap, MakeUnlitOpaque(capColor));

            return root;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static void ApplyMaterial(GameObject go, Material mat)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Shader FindUnlit()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            return sh;
        }

        private static Material MakeUnlitOpaque(Color color)
        {
            var mat = new Material(FindUnlit());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            return mat;
        }

        private static Material MakeUnlitTransparent(Color color)
        {
            var mat = new Material(FindUnlit());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }
    }
}
