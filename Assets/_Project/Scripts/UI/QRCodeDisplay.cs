using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Renders a join-URL QR code (when ZXing.Net is available) or falls back to plain text.
    /// </summary>
    public class QRCodeDisplay : MonoBehaviour
    {
        [SerializeField] private RawImage targetImage;
        [SerializeField] private TMP_Text fallbackTextLabel;
        [SerializeField] private int textureSize = 512;
        [SerializeField] private string urlTemplate = "{0}/join/{1}?p={2}";

        public string LastUrl { get; private set; }

        private static bool warnedNoZxing;

        public void ShowCode(string baseUrl, string code) => ShowCode(baseUrl, code, 1);

        public void ShowCode(string baseUrl, string code, int playerSlot)
        {
            LastUrl = string.Format(urlTemplate, baseUrl, code, playerSlot);

            if (fallbackTextLabel != null)
            {
                fallbackTextLabel.text = LastUrl;
                fallbackTextLabel.gameObject.SetActive(true);
            }

#if ZXING
            try
            {
                var writer = new ZXing.QrCode.QRCodeWriter();
                var hints = new System.Collections.Generic.Dictionary<ZXing.EncodeHintType, object>
                {
                    { ZXing.EncodeHintType.MARGIN, 1 },
                    { ZXing.EncodeHintType.CHARACTER_SET, "UTF-8" }
                };
                var matrix = writer.encode(LastUrl, ZXing.BarcodeFormat.QR_CODE, textureSize, textureSize, hints);

                int w = matrix.Width;
                int h = matrix.Height;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;

                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        bool on = matrix[x, y];
                        // Flip Y so the image isn't upside down on RawImage
                        int idx = (h - 1 - y) * w + x;
                        pixels[idx] = on ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, false);

                if (targetImage != null)
                {
                    targetImage.texture = tex;
                    targetImage.color = Color.white;
                    targetImage.gameObject.SetActive(true);
                }

                if (fallbackTextLabel != null)
                {
                    // QR available, keep label small/disabled
                    fallbackTextLabel.gameObject.SetActive(false);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[QRCodeDisplay] QR generation failed, falling back to text: {ex.Message}");
                if (targetImage != null)
                {
                    targetImage.gameObject.SetActive(false);
                }
            }
#else
            if (!warnedNoZxing)
            {
                Debug.LogWarning("ZXing.Net not present, QR will display as URL text only. Install ZXing.Net DLL into Assets/Plugins to enable QR rendering, then add ZXING to Player → Scripting Define Symbols.");
                warnedNoZxing = true;
            }

            if (targetImage != null)
            {
                targetImage.gameObject.SetActive(false);
            }
#endif
        }
    }
}
