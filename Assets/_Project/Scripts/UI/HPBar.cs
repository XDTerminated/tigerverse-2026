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

        private int targetCurrent;
        private int targetMax = 1;

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
                float desired = (float)targetCurrent / Mathf.Max(1, targetMax);
                fillImage.fillAmount = Mathf.Lerp(fillImage.fillAmount, desired, Time.deltaTime * 8f);
            }
        }

        public void SetBillboardOffsetY(float y) => billboardOffsetY = y;
        public float BillboardOffsetY => billboardOffsetY;
    }
}
