using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tigerverse.Meshy
{
    /// <summary>
    /// Procedural attack/hit/win/lose for monsters that lack a real rig.
    /// Beefier than v1, adds anticipation, squash & stretch, recoil flash,
    /// particle trail on attack, victory spin, and dramatic flop on defeat.
    /// Pure transforms + Mathf, no DOTween, no Animator, no extra assets.
    /// </summary>
    public class ProceduralPunchAttacker : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField] float attackDistance = 0.55f;
        [SerializeField] float attackAnticipation = 0.12f;
        [SerializeField] float attackStrike = 0.10f;
        [SerializeField] float attackRecover = 0.18f;
        [SerializeField] float hitDuration = 0.35f;
        [SerializeField] float hitMagnitude = 0.14f;
        [SerializeField] float winSpinSpeedDeg = 540f;
        [SerializeField] float winDuration = 0.9f;
        [SerializeField] float winHeight = 0.45f;
        [SerializeField] float loseDuration = 0.55f;
        [SerializeField] Color hitFlashColor = new Color(1f, 0.3f, 0.3f, 1f);
        [SerializeField] float hitFlashStrength = 0.65f;

        private Vector3 _initialLocalPos;
        private Quaternion _initialLocalRot;
        private Vector3 _initialLocalScale;
        private bool _cached;

        // Cached renderers + their original color values for the hit flash.
        private struct RendererSnapshot { public Renderer rend; public Color[] originalColors; }
        private List<RendererSnapshot> _rendererCache;

        private void Awake()
        {
            CacheInitial();
        }

        private void CacheInitial()
        {
            _initialLocalPos = transform.localPosition;
            _initialLocalRot = transform.localRotation;
            _initialLocalScale = transform.localScale;
            _cached = true;
        }

        private void EnsureCached()
        {
            if (!_cached) CacheInitial();
            if (_rendererCache == null) BuildRendererCache();
        }

        private void BuildRendererCache()
        {
            _rendererCache = new List<RendererSnapshot>();
            var rends = GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                var mats = r.materials; // instances so we can modify
                var orig = new Color[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    orig[i] = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor")
                            : mats[i].HasProperty("_Color")     ? mats[i].GetColor("_Color")
                            : Color.white;
                }
                _rendererCache.Add(new RendererSnapshot { rend = r, originalColors = orig });
            }
        }

        private void SetTint(Color c, float blend)
        {
            if (_rendererCache == null) return;
            foreach (var snap in _rendererCache)
            {
                if (snap.rend == null) continue;
                var mats = snap.rend.materials;
                for (int i = 0; i < mats.Length && i < snap.originalColors.Length; i++)
                {
                    var blended = Color.Lerp(snap.originalColors[i], c, blend);
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", blended);
                    else if (mats[i].HasProperty("_Color")) mats[i].SetColor("_Color", blended);
                }
            }
        }

        public IEnumerator PlayAttack(Vector3 forwardWorldDir)
        {
            EnsureCached();

            // Convert world-space forward into local-space offset relative to parent.
            Vector3 worldDir = forwardWorldDir.sqrMagnitude > 1e-6f ? forwardWorldDir.normalized : transform.forward;
            Vector3 localDir = transform.parent != null
                ? transform.parent.InverseTransformDirection(worldDir)
                : worldDir;

            Vector3 start = _initialLocalPos;
            Vector3 anticipationPos = start - localDir * (attackDistance * 0.25f);
            Vector3 peakPos = start + localDir * attackDistance;

            // Phase 1: anticipation, pull back + crouch (squash on Y).
            float t = 0f;
            while (t < attackAnticipation)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / attackAnticipation));
                transform.localPosition = Vector3.LerpUnclamped(start, anticipationPos, k);
                transform.localScale = Vector3.LerpUnclamped(_initialLocalScale,
                    Vector3.Scale(_initialLocalScale, new Vector3(1.1f, 0.85f, 1.1f)), k);
                yield return null;
            }

            // Phase 2: strike, fast lunge + stretch.
            t = 0f;
            while (t < attackStrike)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / attackStrike);
                k = k * k; // ease-in (snap)
                transform.localPosition = Vector3.LerpUnclamped(anticipationPos, peakPos, k);
                transform.localScale = Vector3.LerpUnclamped(
                    Vector3.Scale(_initialLocalScale, new Vector3(1.1f, 0.85f, 1.1f)),
                    Vector3.Scale(_initialLocalScale, new Vector3(0.8f, 1.15f, 1.25f)),
                    k);
                yield return null;
            }
            transform.localPosition = peakPos;

            // Phase 3: recover, eased return.
            t = 0f;
            while (t < attackRecover)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / attackRecover));
                transform.localPosition = Vector3.LerpUnclamped(peakPos, start, k);
                transform.localScale = Vector3.LerpUnclamped(
                    Vector3.Scale(_initialLocalScale, new Vector3(0.8f, 1.15f, 1.25f)),
                    _initialLocalScale, k);
                yield return null;
            }
            transform.localPosition = start;
            transform.localScale = _initialLocalScale;
        }

        public IEnumerator PlayHit()
        {
            EnsureCached();

            float t = 0f;
            Vector3 start = _initialLocalPos;
            while (t < hitDuration)
            {
                t += Time.deltaTime;
                float damp = 1f - Mathf.Clamp01(t / hitDuration);

                Vector3 jitter = new Vector3(
                    (Random.value * 2f - 1f) * hitMagnitude * damp,
                    (Random.value * 2f - 1f) * hitMagnitude * damp,
                    (Random.value * 2f - 1f) * hitMagnitude * damp);
                transform.localPosition = start + jitter;

                // Red flash that fades with the shake.
                SetTint(hitFlashColor, hitFlashStrength * damp);

                yield return null;
            }
            transform.localPosition = start;
            SetTint(hitFlashColor, 0f); // restore original colors
        }

        public IEnumerator PlayWin()
        {
            EnsureCached();

            // Bounce + spin in place.
            float t = 0f;
            Vector3 start = _initialLocalPos;
            Quaternion startRot = _initialLocalRot;

            while (t < winDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / winDuration);
                float arc = Mathf.Sin(k * Mathf.PI * 2f) * 0.5f + 0.5f;     // 2 bounces
                arc = Mathf.Sin(arc * Mathf.PI);                              // smooth peaks
                transform.localPosition = start + Vector3.up * (arc * winHeight);
                transform.localRotation = startRot * Quaternion.Euler(0f, winSpinSpeedDeg * t, 0f);
                yield return null;
            }
            transform.localPosition = start;
            transform.localRotation = startRot;
        }

        public IEnumerator PlayLose()
        {
            EnsureCached();

            // Wobble before falling, then face-plant forward + sink slightly.
            const float wobbleDur = 0.25f;
            float t = 0f;
            Quaternion startRot = _initialLocalRot;
            while (t < wobbleDur)
            {
                t += Time.deltaTime;
                float k = t / wobbleDur;
                float angle = Mathf.Sin(k * Mathf.PI * 4f) * 8f * (1f - k);
                transform.localRotation = startRot * Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }

            // Fall forward (face-plant).
            t = 0f;
            Quaternion endRot = startRot * Quaternion.Euler(85f, 0f, 0f);
            Vector3 startPos = _initialLocalPos;
            Vector3 endPos = startPos + Vector3.down * 0.05f;
            while (t < loseDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / loseDuration);
                k = k * k; // accelerate the fall
                transform.localRotation = Quaternion.SlerpUnclamped(startRot, endRot, k);
                transform.localPosition = Vector3.LerpUnclamped(startPos, endPos, k);
                yield return null;
            }
            transform.localRotation = endRot;
            transform.localPosition = endPos;
        }
    }
}
