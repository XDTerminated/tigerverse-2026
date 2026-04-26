using TMPro;
using Tigerverse.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// World-space billboarded HP bar. Lerps fill toward target and tints by element.
    /// </summary>
    public class HPBar : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private Camera billboardCamera;
        [SerializeField] private float billboardOffsetY = 1.6f;
        [Tooltip("Padding (canvas units) reserved between the panel border and the fill on each side. Used to compute the available fill width.")]
        [SerializeField] private float fillPadding = 8f;

        [Header("Health colors")]
        [Tooltip("Fill color while HP is healthy (above the lowHpThreshold).")]
        [SerializeField] private Color healthyColor = new Color(0x22 / 255f, 0xC5 / 255f, 0x5E / 255f, 1f); // Tailwind green-500
        [Tooltip("Fill color when HP drops to or below the lowHpThreshold.")]
        [SerializeField] private Color lowHpColor = new Color(0xDC / 255f, 0x26 / 255f, 0x26 / 255f, 1f); // Tailwind red-600
        [Tooltip("Ratio of currentHP/maxHP at or below which the bar flips to lowHpColor.")]
        [SerializeField, Range(0f, 1f)] private float lowHpThreshold = 0.25f;

        private int targetCurrent;
        private int targetMax = 1;
        private float _currentWidth = -1f;

        public void SetHP(int current, int max)
        {
            targetCurrent = current;
            targetMax = max;
            labelText?.SetText($"{current}/{max}");
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void SetElementColor(ElementType element)
        {
            // Element-tinting is no longer used; fill color is health-state
            // driven (green > 25%, red <= 25%). Method left as a no-op so
            // existing callers compile.
        }

        private void LateUpdate()
        {
            if (billboardCamera == null)
            {
                billboardCamera = Camera.main;
                if (billboardCamera == null)
                {
                    return;
                }
            }

            // Billboard the bar canvas to face the camera.
            Quaternion camRot = billboardCamera.transform.rotation;
            Vector3 lookTarget = transform.position + camRot * Vector3.forward;
            Vector3 upHint = camRot * Vector3.up;
            transform.LookAt(lookTarget, upHint);

            if (fillImage != null)
            {
                // Drive the visible bar by animating the Fill rect's width
                // (left-anchored) instead of Image.fillAmount. With a sliced
                // rounded sprite, this keeps the rounded RIGHT edge intact as
                // the bar drains, a hard square right edge would otherwise
                // appear once the fill drops below ~95%.
                var fillRT = fillImage.rectTransform;
                var parentRT = (RectTransform)fillRT.parent;
                float innerWidth = Mathf.Max(0f, parentRT.rect.width - fillPadding * 2f);
                float ratio = Mathf.Clamp01((float)targetCurrent / Mathf.Max(1, targetMax));
                float desiredWidth = innerWidth * ratio;
                if (_currentWidth < 0f) _currentWidth = desiredWidth;
                _currentWidth = Mathf.Lerp(_currentWidth, desiredWidth, Time.deltaTime * 8f);
                var sd = fillRT.sizeDelta;
                sd.x = _currentWidth;
                fillRT.sizeDelta = sd;

                // Health-state coloring: green when healthy, red once HP hits
                // the danger threshold (default 25%).
                fillImage.color = ratio <= lowHpThreshold ? lowHpColor : healthyColor;
            }
        }

        public void SetBillboardOffsetY(float y) => billboardOffsetY = y;
        public float BillboardOffsetY => billboardOffsetY;
    }
}
