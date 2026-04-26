using System;
using System.Collections;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Floating paper card that "draws itself" line-by-line via a wipe shader.
    /// Spawned during model load so judges see the player's drawing materialise
    /// while the GLB downloads/parses. When the monster is ready, Crumple()
    /// runs a tumble-and-fall animation toward the spawn point and destroys the
    /// card.
    /// </summary>
    [DisallowMultipleComponent]
    public class DrawingRevealCard : MonoBehaviour
    {
        [Tooltip("Seconds for the wipe to fully reveal the drawing.")]
        public float revealDurationSec = 4.0f;

        [Tooltip("Subtle vertical bob amplitude (metres).")]
        public float idleBobAmplitude = 0.025f;

        [Tooltip("Bob cycles per second.")]
        public float idleBobSpeed = 1.2f;

        [Tooltip("Idle yaw spin while reveal is in progress (degrees / sec).")]
        public float idleYawSpeed = 8f;

        [Tooltip("Crumple-and-fall duration in seconds.")]
        public float crumpleDurationSec = 0.9f;

        private Material _mat;
        private Renderer _renderer;
        private Vector3  _basePos;
        private Quaternion _baseRot;
        private float    _phase;
        private bool     _crumpling;
        private float    _hatchedHoldSec = 0.25f;

        /// <summary>
        /// Build a quad with the reveal shader, parent it under the given
        /// transform, and start the wipe animation immediately.
        /// </summary>
        public static DrawingRevealCard Spawn(Texture2D drawingTex, Transform parent, Vector3 worldPos, Quaternion worldRot, Vector2 sizeMeters)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "DrawingRevealCard";
            // No collider, purely visual.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;
            go.transform.localScale = new Vector3(sizeMeters.x, sizeMeters.y, 1f);

            var card = go.AddComponent<DrawingRevealCard>();
            card.SetDrawing(drawingTex);
            card._basePos = worldPos;
            card._baseRot = worldRot;
            return card;
        }

        public void SetDrawing(Texture2D tex)
        {
            _renderer = GetComponent<Renderer>();
            var sh = Shader.Find("Tigerverse/DrawingRevealCard");
            if (sh == null)
            {
                Debug.LogWarning("[DrawingRevealCard] Shader 'Tigerverse/DrawingRevealCard' not found. Falling back to URP/Unlit (no wipe animation).");
                sh = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (sh == null)
            {
                Debug.LogError("[DrawingRevealCard] Even URP/Unlit not found, card will be invisible.");
                sh = Shader.Find("Hidden/InternalErrorShader");
            }
            _mat = new Material(sh);
            if (tex != null && _mat.HasProperty("_DrawingTex"))
                _mat.SetTexture("_DrawingTex", tex);
            // For URP/Unlit fallback, set _BaseMap so SOMETHING shows.
            if (tex != null && _mat.HasProperty("_BaseMap"))
                _mat.SetTexture("_BaseMap", tex);

            // Try to attach a paper texture if one was downloaded by the
            // paper-shader pipeline (Resources/PaperTextures).
            var paper = LoadPaperTex("Color");
            if (paper != null && _mat.HasProperty("_PaperTex"))
                _mat.SetTexture("_PaperTex", paper);

            if (_mat.HasProperty("_RevealAmount")) _mat.SetFloat("_RevealAmount", 0f);
            _renderer.material = _mat;
        }

        private void Update()
        {
            if (_crumpling || _mat == null) return;
            _phase += Time.deltaTime;
            float reveal = Mathf.Clamp01(_phase / Mathf.Max(revealDurationSec, 0.01f));
            _mat.SetFloat("_RevealAmount", reveal);

            // Idle bob.
            Vector3 p = _basePos;
            p.y += Mathf.Sin(_phase * idleBobSpeed * Mathf.PI * 2f) * idleBobAmplitude;
            transform.position = p;

            // Subtle yaw rotation around card normal.
            transform.rotation = _baseRot * Quaternion.Euler(0f, _phase * idleYawSpeed, 0f);
        }

        /// <summary>
        /// Force-complete the wipe (so judges see the full drawing for a beat),
        /// then crumple toward fallTarget and destroy.
        /// </summary>
        public IEnumerator Crumple(Vector3 fallTarget, Action onComplete = null)
        {
            _crumpling = true;
            if (_mat != null && _mat.HasProperty("_RevealAmount"))
                _mat.SetFloat("_RevealAmount", 1f);

            yield return new WaitForSeconds(_hatchedHoldSec);

            Vector3    startPos   = transform.position;
            Quaternion startRot   = transform.rotation;
            Vector3    startScale = transform.localScale;

            float t = 0f;
            while (t < crumpleDurationSec)
            {
                t += Time.deltaTime;
                float k     = Mathf.Clamp01(t / crumpleDurationSec);
                float eased = k * k;                           // ease-in
                transform.position = Vector3.Lerp(startPos, fallTarget, eased);
                transform.rotation = startRot * Quaternion.Euler(360f * eased, 720f * eased, 180f * eased);
                // Crumple horizontally first (paper folding), then collapse vertically.
                float sx = startScale.x * (1f - eased);
                float sy = startScale.y * (1f - eased * eased);
                transform.localScale = new Vector3(sx, sy, startScale.z);
                yield return null;
            }

            onComplete?.Invoke();
            Destroy(gameObject);
        }

        // Mirror DrawingColorize's lookup convention so the card and the monster
        // share the same paper texture if one is available.
        private static Texture2D[] _allPaperTextures;
        private static Texture2D LoadPaperTex(string suffix)
        {
            if (_allPaperTextures == null)
                _allPaperTextures = Resources.LoadAll<Texture2D>("PaperTextures");
            foreach (var t in _allPaperTextures)
            {
                if (t == null) continue;
                if (t.name.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return null;
        }
    }
}
