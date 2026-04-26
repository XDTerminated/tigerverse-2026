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

        private int targetCurrent;
        private int targetMax = 1;
        private float _currentWidth = -1f;

        public void SetHP(int current, int max)
        {
            targetCurrent = current;
            targetMax = max;
            labelText?.SetText($"{current}/{max}");
        }

        public void SetElementColor(ElementType element)
        {
            if (fillImage != null)
            {
                fillImage.color = element.ToColor();
            }
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
                // the bar drains — a hard square right edge would otherwise
                // appear once the fill drops below ~95%.
                var fillRT = fillImage.rectTransform;
                var parentRT = (RectTransform)fillRT.parent;
                float innerWidth = Mathf.Max(0f, parentRT.rect.width - fillPadding * 2f);
                float desiredWidth = innerWidth * Mathf.Clamp01((float)targetCurrent / Mathf.Max(1, targetMax));
                if (_currentWidth < 0f) _currentWidth = desiredWidth;
                _currentWidth = Mathf.Lerp(_currentWidth, desiredWidth, Time.deltaTime * 8f);
                var sd = fillRT.sizeDelta;
                sd.x = _currentWidth;
                fillRT.sizeDelta = sd;
            }
        }

        public void SetBillboardOffsetY(float y) => billboardOffsetY = y;
        public float BillboardOffsetY => billboardOffsetY;
    }
}
