using System.Collections;
using UnityEngine;

namespace Tigerverse.Meshy
{
    /// <summary>
    /// Tweenless procedural attack/hit/win/lose animations for monsters
    /// that lack a humanoid rig. Pure coroutines + Mathf.Lerp / SmoothStep.
    /// </summary>
    public class ProceduralPunchAttacker : MonoBehaviour
    {
        private Vector3 _initialLocalPos;
        private Quaternion _initialLocalRot;
        private bool _cached;

        private void Awake()
        {
            CacheInitial();
        }

        private void CacheInitial()
        {
            _initialLocalPos = transform.localPosition;
            _initialLocalRot = transform.localRotation;
            _cached = true;
        }

        private void EnsureCached()
        {
            if (!_cached) CacheInitial();
        }

        public IEnumerator PlayAttack(Vector3 forwardWorldDir)
        {
            EnsureCached();

            // Convert the world-space forward direction into local-space offset relative to parent.
            Vector3 worldDir = forwardWorldDir.sqrMagnitude > 1e-6f ? forwardWorldDir.normalized : transform.forward;
            Vector3 localDir;
            if (transform.parent != null)
            {
                localDir = transform.parent.InverseTransformDirection(worldDir);
            }
            else
            {
                localDir = worldDir;
            }

            const float distance = 0.4f;
            const float halfDur = 0.15f;

            Vector3 start = _initialLocalPos;
            Vector3 peak = _initialLocalPos + localDir * distance;

            // Punch out
            float t = 0f;
            while (t < halfDur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / halfDur));
                transform.localPosition = Vector3.LerpUnclamped(start, peak, k);
                yield return null;
            }
            transform.localPosition = peak;

            // Return
            t = 0f;
            while (t < halfDur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / halfDur));
                transform.localPosition = Vector3.LerpUnclamped(peak, start, k);
                yield return null;
            }
            transform.localPosition = start;
        }

        public IEnumerator PlayHit()
        {
            EnsureCached();

            const float duration = 0.3f;
            const float magnitude = 0.1f;
            float t = 0f;
            Vector3 start = _initialLocalPos;
            while (t < duration)
            {
                t += Time.deltaTime;
                float damp = 1f - Mathf.Clamp01(t / duration);
                Vector3 jitter = new Vector3(
                    (Random.value * 2f - 1f) * magnitude * damp,
                    (Random.value * 2f - 1f) * magnitude * damp,
                    (Random.value * 2f - 1f) * magnitude * damp);
                transform.localPosition = start + jitter;
                yield return null;
            }
            transform.localPosition = start;
        }

        public IEnumerator PlayWin()
        {
            EnsureCached();

            const float duration = 0.4f;
            const float height = 0.3f;
            Vector3 start = _initialLocalPos;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                // Sine arc: 0 -> 1 -> 0 across the duration.
                float arc = Mathf.Sin(k * Mathf.PI);
                transform.localPosition = start + Vector3.up * (arc * height);
                yield return null;
            }
            transform.localPosition = start;
        }

        public IEnumerator PlayLose()
        {
            EnsureCached();

            const float duration = 0.5f;
            float t = 0f;
            Quaternion start = _initialLocalRot;
            // Fall forward = rotate around the local X axis by 80 degrees.
            Quaternion end = start * Quaternion.Euler(80f, 0f, 0f);

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                transform.localRotation = Quaternion.SlerpUnclamped(start, end, k);
                yield return null;
            }
            transform.localRotation = end;
        }
    }
}
