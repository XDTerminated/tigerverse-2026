using System.Collections;
using TMPro;
using Tigerverse.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Large center-of-vision banner that flashes "SCRIBBLE MODE" or
    /// "ARTIST MODE" whenever the player toggles between them. Built
    /// programmatically as a head-locked world-space canvas so it shows up
    /// front-and-center in XR without needing any scene wiring.
    /// </summary>
    public class BattleModeOverlay : MonoBehaviour
    {
        [Header("Tunables")]
        public float fadeInSeconds = 0.12f;
        public float holdSeconds = 0.7f;
        public float fadeOutSeconds = 0.4f;
        public float distanceFromCamera = 1.6f;

        private Canvas _canvas;
        private CanvasGroup _group;
        private TMP_Text _label;
        private Coroutine _flashCo;

        private void Awake()
        {
            BuildCanvas();
            // Start hidden; we only show on Show().
            _group.alpha = 0f;
        }

        private void LateUpdate()
        {
            // Head-lock the overlay to the camera. Doing this in LateUpdate
            // (after head pose is sampled) keeps the banner perfectly centered
            // even with rapid head motion.
            var cam = Camera.main;
            if (cam == null || _canvas == null) return;
            var camT = cam.transform;
            transform.position = camT.position + camT.forward * distanceFromCamera;
            transform.rotation = camT.rotation;
        }

        public void Show(BattleControlMode mode)
        {
            if (_label != null)
                _label.text = mode == BattleControlMode.Scribble ? "SCRIBBLE MODE" : "ARTIST MODE";
            if (_flashCo != null) StopCoroutine(_flashCo);
            _flashCo = StartCoroutine(Flash());
        }

        private IEnumerator Flash()
        {
            // Fade in
            float t = 0f;
            while (t < fadeInSeconds)
            {
                t += Time.deltaTime;
                _group.alpha = Mathf.Clamp01(t / fadeInSeconds);
                yield return null;
            }
            _group.alpha = 1f;
            yield return new WaitForSeconds(holdSeconds);
            // Fade out
            t = 0f;
            while (t < fadeOutSeconds)
            {
                t += Time.deltaTime;
                _group.alpha = 1f - Mathf.Clamp01(t / fadeOutSeconds);
                yield return null;
            }
            _group.alpha = 0f;
            _flashCo = null;
        }

        private void BuildCanvas()
        {
            // Top-level canvas on this GO.
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 1000;
            gameObject.AddComponent<CanvasScaler>();
            // No raycaster — overlay is purely visual.

            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(900, 220);
            transform.localScale = Vector3.one * 0.0025f; // ~2.25m × 0.55m at the canvas's worldspace position

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            // Label
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(transform, false);
            var lblRT = (RectTransform)labelGo.transform;
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
            _label = labelGo.GetComponent<TMP_Text>();
            _label.text = "";
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 140;
            _label.color = Color.black;
            _label.raycastTarget = false;

#if UNITY_EDITOR
            // In editor, auto-grab the project font so the overlay matches the
            // doodle aesthetic without needing inspector wiring. In a build,
            // it falls back to TMP's default font (LiberationSans).
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
            if (font != null) _label.font = font;
#endif
        }
    }
}
