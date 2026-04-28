using System.Collections;
using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Big floating "X WINS!" banner shown when the battle ends. Spawns ~3 m
    /// in front of the local camera, billboards each LateUpdate so the
    /// player can turn freely. Pop-in scale animation, optional voice
    /// announcement via the scene's Announcer (TTS), idle pulse for drama.
    /// Lives until Hide() / OnDestroy.
    /// </summary>
    [DisallowMultipleComponent]
    public class WinScreenBanner : MonoBehaviour
    {
        [SerializeField] private float distanceFromPlayer = 3.2f;
        [SerializeField] private float anchorHeight        = 1.55f;

        private TextMeshPro _winnerLabel;
        private TextMeshPro _flavorLabel;
        private GameObject  _backing;
        private Camera      _cam;
        private float       _phase;

        private static TMP_FontAsset _cachedFont;

        public static WinScreenBanner Spawn(string winnerName)
        {
            var go = new GameObject("WinScreenBanner");
            var banner = go.AddComponent<WinScreenBanner>();
            banner.SetWinner(winnerName);
            return banner;
        }

        public void SetWinner(string winnerName)
        {
            string nameUpper = string.IsNullOrEmpty(winnerName) ? "PLAYER" : winnerName.ToUpperInvariant();
            BuildVisualIfNeeded();
            if (_winnerLabel != null) _winnerLabel.text = nameUpper + " WINS!";
            if (_flavorLabel != null) _flavorLabel.text = "Returning to title…";
            StartCoroutine(SpeakAnnouncement(winnerName));
        }

        private void BuildVisualIfNeeded()
        {
            if (_backing != null) return;

            // Anchor in front of the local camera at chest/eye height. World
            // position is computed once on spawn and then we billboard the
            // rotation per frame so the player can turn freely.
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                transform.position = cam.transform.position + fwd * distanceFromPlayer + Vector3.up * (anchorHeight - cam.transform.position.y + cam.transform.position.y);
                transform.position = new Vector3(transform.position.x, anchorHeight, transform.position.z);
            }

            // Backing card — paper-cream cube, dramatic-sized.
            _backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _backing.name = "Backing";
            var col = _backing.GetComponent<Collider>(); if (col != null) Destroy(col);
            _backing.transform.SetParent(transform, false);
            _backing.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            _backing.transform.localScale = new Vector3(2.4f, 0.85f, 0.04f);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var bgMat = new Material(sh);
            var paperCream = new Color(0.99f, 0.97f, 0.92f, 1f);
            if (bgMat.HasProperty("_BaseColor")) bgMat.SetColor("_BaseColor", paperCream);
            else bgMat.color = paperCream;
            _backing.GetComponent<Renderer>().sharedMaterial = bgMat;

            // Winner label — big, bold, accent colour.
            var winnerGo = new GameObject("WinnerLabel");
            winnerGo.transform.SetParent(transform, false);
            winnerGo.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            _winnerLabel = winnerGo.AddComponent<TextMeshPro>();
            _winnerLabel.text = "PLAYER WINS!";
            _winnerLabel.fontSize = 1.40f;
            _winnerLabel.fontStyle = FontStyles.Bold;
            _winnerLabel.alignment = TextAlignmentOptions.Center;
            _winnerLabel.color = new Color(0.85f, 0.40f, 0.10f);
            _winnerLabel.outlineColor = new Color32(20, 14, 8, 255);
            _winnerLabel.outlineWidth = 0.25f;
            _winnerLabel.enableWordWrapping = false;
            _winnerLabel.font = LoadSophiecomicFont() ?? _winnerLabel.font;
            _winnerLabel.rectTransform.sizeDelta = new Vector2(2.30f, 0.45f);

            // Flavor label — sub line.
            var flavorGo = new GameObject("FlavorLabel");
            flavorGo.transform.SetParent(transform, false);
            flavorGo.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            _flavorLabel = flavorGo.AddComponent<TextMeshPro>();
            _flavorLabel.text = "Returning to title…";
            _flavorLabel.fontSize = 0.42f;
            _flavorLabel.fontStyle = FontStyles.Italic;
            _flavorLabel.alignment = TextAlignmentOptions.Center;
            _flavorLabel.color = new Color(0.18f, 0.14f, 0.10f);
            _flavorLabel.outlineColor = new Color32(255, 255, 255, 200);
            _flavorLabel.outlineWidth = 0.16f;
            _flavorLabel.enableWordWrapping = false;
            _flavorLabel.font = LoadSophiecomicFont() ?? _flavorLabel.font;
            _flavorLabel.rectTransform.sizeDelta = new Vector2(2.20f, 0.20f);

            StartCoroutine(PopInAnimation());
        }

        private IEnumerator PopInAnimation()
        {
            transform.localScale = Vector3.zero;
            const float dur = 0.45f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);
                // Elastic-ish overshoot.
                float k = 1f - Mathf.Pow(1f - p, 3f);
                float scale = Mathf.Lerp(0f, 1.1f, k);
                if (p > 0.85f) scale = Mathf.Lerp(1.1f, 1.0f, (p - 0.85f) / 0.15f);
                transform.localScale = Vector3.one * scale;
                yield return null;
            }
            transform.localScale = Vector3.one;
        }

        private IEnumerator SpeakAnnouncement(string winnerName)
        {
            // One-frame delay so the banner is visible before the voice fires.
            yield return null;
            var announcer = FindFirstObjectByType<Tigerverse.Voice.Announcer>();
            if (announcer != null)
            {
                string line = string.IsNullOrEmpty(winnerName)
                    ? "Battle over! The winner is on screen."
                    : $"{winnerName} wins the battle!";
                announcer.Say(line);
            }
        }

        public void Hide()
        {
            if (gameObject != null) Destroy(gameObject);
        }

        private void LateUpdate()
        {
            _phase += Time.deltaTime;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Billboard toward the camera (world UI faces the camera's -Z so we
            // align +Z with the player's forward direction).
            Vector3 fwd = _cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            // Subtle bob so it doesn't feel static.
            if (_winnerLabel != null)
            {
                float pulse = 1f + Mathf.Sin(_phase * 2.5f) * 0.04f;
                _winnerLabel.transform.localScale = Vector3.one * pulse;
            }
        }

        private static TMP_FontAsset LoadSophiecomicFont()
        {
            if (_cachedFont != null) return _cachedFont;
            _cachedFont = Resources.Load<TMP_FontAsset>("Fonts/sophiecomic SDF");
#if UNITY_EDITOR
            if (_cachedFont == null)
                _cachedFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
#endif
            return _cachedFont;
        }
    }
}
