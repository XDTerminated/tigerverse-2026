using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Adds a subtle wind-sway tilt and breathing scale wobble to every
    /// BirchTree* environment model, with per-tree random phase so the
    /// scene reads as alive without all trees moving in lockstep.
    /// </summary>
    [DisallowMultipleComponent]
    public class PaperTreeSway : MonoBehaviour
    {
        [SerializeField] private float swayDegrees = 2f;
        [SerializeField] private float swayHz = 0.4f;
        [SerializeField] private float breatheAmplitude = 0.02f;
        [SerializeField] private float breatheHz = 0.25f;

        private Transform[] _trees;
        private Quaternion[] _baseRot;
        private Vector3[] _baseScale;
        private float[] _phaseSway;
        private float[] _phaseBreathe;

        private void Start()
        {
            CollectTrees();
            int n = _trees != null ? _trees.Length : 0;
            _baseRot = new Quaternion[n];
            _baseScale = new Vector3[n];
            _phaseSway = new float[n];
            _phaseBreathe = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (_trees[i] == null) continue;
                _baseRot[i] = _trees[i].localRotation;
                _baseScale[i] = _trees[i].localScale;
                _phaseSway[i] = Random.Range(0f, 100f);
                _phaseBreathe[i] = Random.Range(0f, 100f);
            }
        }

        private void Update()
        {
            if (_trees == null) return;
            float dt = Time.deltaTime;
            float twoPi = Mathf.PI * 2f;
            for (int i = 0; i < _trees.Length; i++)
            {
                Transform t = _trees[i];
                if (t == null) continue;
                _phaseSway[i] += dt;
                _phaseBreathe[i] += dt;

                float zDeg = Mathf.Sin(_phaseSway[i] * swayHz * twoPi) * swayDegrees;
                t.localRotation = _baseRot[i] * Quaternion.Euler(0f, 0f, zDeg);

                float wobble = Mathf.Sin(_phaseBreathe[i] * breatheHz * twoPi) * breatheAmplitude;
                Vector3 s = _baseScale[i];
                s.y *= (1f + wobble);
                t.localScale = s;
            }
        }

        private void CollectTrees()
        {
            System.Collections.Generic.List<Transform> found = new System.Collections.Generic.List<Transform>();
            System.Collections.Generic.HashSet<Transform> seen = new System.Collections.Generic.HashSet<Transform>();

            MeshFilter[] childFilters = GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>(true);
            AddMatching(childFilters, childRenderers, found, seen);

            if (found.Count == 0)
            {
                MeshFilter[] sceneFilters = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                MeshRenderer[] sceneRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                AddMatching(sceneFilters, sceneRenderers, found, seen);
            }

            _trees = found.ToArray();
        }

        private static void AddMatching(MeshFilter[] filters, MeshRenderer[] renderers,
            System.Collections.Generic.List<Transform> found,
            System.Collections.Generic.HashSet<Transform> seen)
        {
            if (filters != null)
            {
                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i] == null) continue;
                    Transform tr = filters[i].transform;
                    if (tr.name.StartsWith("BirchTree") && seen.Add(tr)) found.Add(tr);
                }
            }
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    Transform tr = renderers[i].transform;
                    if (tr.name.StartsWith("BirchTree") && seen.Add(tr)) found.Add(tr);
                }
            }
        }

        public static PaperTreeSway InstallSceneWide()
        {
            GameObject go = new GameObject("PaperTreeSwayCoordinator");
            PaperTreeSway sway = go.AddComponent<PaperTreeSway>();
            return sway;
        }
    }
}
