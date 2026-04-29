using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Gently pulses the attached transform's scale between 1.0 and an upper
    /// bound on a sine wave. Used on the title-screen Scribble Showdown logo
    /// so the menu doesn't feel statically frozen.
    /// </summary>
    [DisallowMultipleComponent]
    public class LogoPulse : MonoBehaviour
    {
        [SerializeField] private float maxScale = 1.04f;
        [SerializeField] private float periodSeconds = 2.0f;

        private Vector3 _baseScale;
        private float _phase;

        private void Awake()
        {
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            _phase += Time.unscaledDeltaTime;
            float t = (Mathf.Sin(_phase * Mathf.PI * 2f / Mathf.Max(0.05f, periodSeconds)) + 1f) * 0.5f;
            float k = Mathf.Lerp(1f, maxScale, t);
            transform.localScale = _baseScale * k;
        }
    }
}
