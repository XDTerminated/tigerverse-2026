using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// "X WINS!" panel shown when the battle ends. Built as a 2D world-space
    /// canvas styled the same way as the main-menu loading screen: rounded
    /// panel sprite, Sophiecomic font, paper-cream tint. Soft-follows the
    /// player via CanvasFollowPlayer so it stays in view if they turn.
    /// Optional ElevenLabs TTS announcement.
    /// </summary>
    [DisallowMultipleComponent]
    public class WinScreenBanner : MonoBehaviour
    {
        private static Sprite _cachedPanelSprite;
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
            BuildCanvas(winnerName);
            // Intentionally NOT calling SpeakAnnouncement — BattleCommentator
            // already fires its KO line via the OnBattleEnd hook, and having
            // both speak at the same moment created an overlapping mess. The
            // commentator's line is more in-character anyway.
        }

        private void BuildCanvas(string winnerName)
        {
            string nameUpper = string.IsNullOrEmpty(winnerName) ? "PLAYER" : winnerName.ToUpperInvariant();

            // World-space canvas styled to match TitleCanvas — same scale
            // (0.002), same size envelope, same XR raycaster setup so future
            // clickable buttons would just work without a separate config.
            var canvasGo = new GameObject("WinCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRT = (RectTransform)canvasGo.transform;
            canvasRT.sizeDelta = new Vector2(900, 500);
            canvasGo.transform.localScale = Vector3.one * 0.002f;

            // CanvasFollowPlayer keeps the panel in front of the local camera
            // with the same easing the loading menu uses.
            canvasGo.AddComponent<CanvasFollowPlayer>();

            // Panel backing — rounded-sliced sprite, paper-cream tint.
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRT = (RectTransform)panelGo.transform;
            panelRT.SetParent(canvasRT, false);
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(820, 420);
            var panelImg = panelGo.GetComponent<Image>();
            var panelSprite = LoadPanelSprite();
            if (panelSprite != null)
            {
                panelImg.sprite = panelSprite;
                panelImg.type = Image.Type.Sliced;
            }
            panelImg.color = new Color(0.99f, 0.97f, 0.92f, 1f);
            panelImg.raycastTarget = false;

            // Winner label — bold, accent orange ink, large.
            var winnerLbl = BuildLabel(canvasRT, nameUpper + " WINS!", new Vector2(0f, 70f),
                                        new Vector2(800, 160),
                                        fontSize: 96, color: new Color(0.85f, 0.40f, 0.10f),
                                        bold: true, italic: false,
                                        outlineColor: new Color32(20, 14, 8, 255), outlineWidth: 0.25f);
            winnerLbl.text = nameUpper + " WINS!";

            // Flavor sub-line.
            BuildLabel(canvasRT, "Returning to title…", new Vector2(0f, -90f),
                       new Vector2(700, 80),
                       fontSize: 36, color: new Color(0.18f, 0.14f, 0.10f),
                       bold: false, italic: true,
                       outlineColor: new Color32(255, 255, 255, 200), outlineWidth: 0.16f);

            StartCoroutine(PopInAnimation(canvasGo.transform));
        }

        private TMP_Text BuildLabel(RectTransform parent, string text, Vector2 anchoredPos, Vector2 size,
                                     int fontSize, Color color, bool bold, bool italic,
                                     Color32 outlineColor, float outlineWidth)
        {
            var go = new GameObject("Lbl_" + text.Replace(" ", "_"), typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize;
            FontStyles fs = FontStyles.Normal;
            if (bold)   fs |= FontStyles.Bold;
            if (italic) fs |= FontStyles.Italic;
            tmp.fontStyle = fs;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.outlineColor = outlineColor;
            tmp.outlineWidth = outlineWidth;
            var font = LoadSophiecomicFont();
            if (font != null) tmp.font = font;
            return tmp;
        }

        private IEnumerator PopInAnimation(Transform t)
        {
            t.localScale = Vector3.zero;
            const float dur = 0.45f;
            float elapsed = 0f;
            while (elapsed < dur && t != null)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / dur);
                float k = 1f - Mathf.Pow(1f - p, 3f);
                float scale = Mathf.Lerp(0f, 1.1f, k);
                if (p > 0.85f) scale = Mathf.Lerp(1.1f, 1.0f, (p - 0.85f) / 0.15f);
                t.localScale = Vector3.one * 0.002f * scale;
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one * 0.002f;
        }

        private IEnumerator SpeakAnnouncement(string winnerName)
        {
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

        private static Sprite LoadPanelSprite()
        {
            if (_cachedPanelSprite != null) return _cachedPanelSprite;
            _cachedPanelSprite = Resources.Load<Sprite>("UI/panel-rounded-sm");
#if UNITY_EDITOR
            if (_cachedPanelSprite == null)
                _cachedPanelSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Generated/panel-rounded-sm.png");
#endif
            return _cachedPanelSprite;
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
