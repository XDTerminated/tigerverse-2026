using System.Collections.Generic;
using UnityEngine;

namespace Tigerverse.Drawing
{
    // TODO (user): Create the companion material in Unity:
    //   Right-click in Project window -> Create -> Material -> name it "DrawingProjection"
    //   place it under Assets/_Project/Materials/, assign the "Tigerverse/DrawingProjection"
    //   shader, then drop it into the projectionMaterialTemplate slot of this component.
    public class DrawingProjector : MonoBehaviour
    {
        private const string ShaderName = "Tigerverse/DrawingProjection";

        [SerializeField] private Material projectionMaterialTemplate;
        [SerializeField] private bool includeInactiveRenderers = true;

        private readonly List<Material> _runtimeMaterials = new();

        private void Awake()
        {
            if (projectionMaterialTemplate == null)
            {
                Debug.LogWarning(
                    "[DrawingProjector] Set projectionMaterialTemplate (Material with " +
                    "Tigerverse/DrawingProjection shader). Right-click in Project -> " +
                    "Create -> Material, assign the shader, drop here.", this);
            }
        }

        public void ApplyDrawing(Texture2D drawingTex)
        {
            if (drawingTex == null)
            {
                Debug.LogWarning("[DrawingProjector] ApplyDrawing called with null texture.", this);
                return;
            }

            Material template = projectionMaterialTemplate;
            if (template == null)
            {
                Shader fallback = Shader.Find(ShaderName);
                if (fallback == null)
                {
                    Debug.LogError($"[DrawingProjector] Shader '{ShaderName}' not found and no template assigned.", this);
                    return;
                }
                template = new Material(fallback);
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[DrawingProjector] No Renderers found under this hierarchy.", this);
                return;
            }

            Bounds bounds = ComputeCombinedObjectBounds(renderers);
            Vector4 bboxMin  = new(bounds.min.x,  bounds.min.y,  bounds.min.z,  0f);
            Vector4 bboxSize = new(bounds.size.x, bounds.size.y, bounds.size.z, 0f);

            // Avoid stacking materials across repeated calls.
            ReleaseRuntimeMaterials();

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                int slotCount = Mathf.Max(1, r.sharedMaterials != null ? r.sharedMaterials.Length : 1);
                Material[] newMats = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                {
                    Material mat = new(template) { name = $"{template.name} (Runtime {r.name}#{i})" };
                    mat.SetTexture("_DrawingTex", drawingTex);
                    mat.SetVector("_BBoxMin", bboxMin);
                    mat.SetVector("_BBoxSize", bboxSize);
                    newMats[i] = mat;
                    _runtimeMaterials.Add(mat);
                }
                r.materials = newMats;
            }
        }

        private static Bounds ComputeCombinedObjectBounds(Renderer[] renderers)
        {
            bool hasAny = false;
            Bounds combined = new(Vector3.zero, Vector3.zero);

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;

                Bounds local;
                if (r is MeshRenderer mr)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        local = mf.sharedMesh.bounds;
                    }
                    else
                    {
                        local = mr.localBounds;
                    }
                }
                else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    local = smr.sharedMesh.bounds;
                }
                else
                {
                    local = r.localBounds;
                }

                if (!hasAny)
                {
                    combined = local;
                    hasAny = true;
                }
                else
                {
                    combined.Encapsulate(local.min);
                    combined.Encapsulate(local.max);
                }
            }

            if (!hasAny)
            {
                combined = new Bounds(Vector3.zero, Vector3.one);
            }
            return combined;
        }

        private void ReleaseRuntimeMaterials()
        {
            if (!Application.isPlaying)
            {
                _runtimeMaterials.Clear();
                return;
            }
            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                if (_runtimeMaterials[i] != null) Destroy(_runtimeMaterials[i]);
            }
            _runtimeMaterials.Clear();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeMaterials();
        }
    }
}
