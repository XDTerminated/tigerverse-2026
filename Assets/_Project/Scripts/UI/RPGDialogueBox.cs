using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// World-space RPG-style dialogue panel. The backing uses a
    /// SpriteRenderer in 9-sliced mode so rounded corners don't stretch
    /// when we resize the panel, and the speaker / body text are TMP
    /// components anchored top-left and force-rebuilt whenever text is
    /// set so they always actually render.
    /// </summary>
    public class RPGDialogueBox : MonoBehaviour
    {
        // Panel size in metres.
        private const float PanelW = 1.8f;
        private const float PanelH = 0.85f;
        // Pixels-per-unit for the sprite; tuned so the 9-slice corners
        // are about 6cm in world space.
        private const float PixelsPerUnit = 512f;

        private GameObject _card;
        private SpriteRenderer _bg;
        private GameObject _portrait;
        private TextMeshPro _speaker;
        private TextMeshPro _body;
        private Camera _cam;

        private static Sprite _panelSprite;
        private static TMP_FontAsset _cachedFont;

        private static readonly Color PaperCream = new Color(0.99f, 0.97f, 0.92f, 1f);
        private static readonly Color SpeakerAccent = new Color(0.10f, 0.40f, 0.65f, 1f);
        private static readonly Color BodyInk = new Color(0.07f, 0.06f, 0.10f, 1f);

        public void Initialize(Transform parent, string speakerName, Texture2D portrait)
        {
            transform.SetParent(parent, false);
            transform.localPosition = new Vector3(0f, 2.7f, 0f);
            transform.localRotation = Quaternion.identity;

            _card = new GameObject("DialogueCard");
            _card.transform.SetParent(transform, false);
            _card.transform.localPosition = Vector3.zero;

            // ----- Background panel: 9-sliced SpriteRenderer -----
            var bgGo = new GameObject("Backing");
            bgGo.transform.SetParent(_card.transform, false);
            bgGo.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = Get9SliceSprite();
            _bg.drawMode = SpriteDrawMode.Sliced;
            _bg.size = new Vector2(PanelW, PanelH);
            _bg.color = PaperCream;
            _bg.sortingOrder = 0;

            float panelTop = PanelH * 0.5f;
            float panelLeft = -PanelW * 0.5f;

            // ----- Portrait: small quad inset top-left -----
            _portrait = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _portrait.name = "Portrait";
            var portCol = _portrait.GetComponent<Collider>(); if (portCol != null) Destroy(portCol);
            _portrait.transform.SetParent(_card.transform, false);
            _portrait.transform.localPosition = new Vector3(panelLeft + 0.18f, panelTop - 0.18f, -0.002f);
            _portrait.transform.localScale = new Vector3(0.28f, 0.28f, 1f);
            _portrait.GetComponent<Renderer>().sharedMaterial = MakePortraitMaterial(portrait);

            // ----- Speaker name: bold, accent color, top-left next to portrait -----
            var speakerGo = new GameObject("SpeakerName");
            speakerGo.transform.SetParent(_card.transform, false);
            speakerGo.transform.localPosition = new Vector3(panelLeft + 0.36f, panelTop - 0.10f, -0.001f);
            _speaker = speakerGo.AddComponent<TextMeshPro>();
            _speaker.text = speakerName ?? string.Empty;
            _speaker.fontSize = 0.95f;
            _speaker.fontStyle = FontStyles.Bold;
            _speaker.alignment = TextAlignmentOptions.TopLeft;
            _speaker.color = SpeakerAccent;
            _speaker.enableWordWrapping = false;
            _speaker.font = LoadFont() ?? _speaker.font;
            _speaker.rectTransform.pivot = new Vector2(0f, 1f);
            _speaker.rectTransform.sizeDelta = new Vector2(PanelW - 0.45f, 0.18f);
            _speaker.ForceMeshUpdate(true, true);

            // ----- Body: bigger, dark ink, fills space below speaker -----
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_card.transform, false);
            float bodyTop = panelTop - 0.22f;
            float bodyH   = PanelH - 0.30f;
            bodyGo.transform.localPosition = new Vector3(panelLeft + 0.10f, bodyTop, -0.001f);
            _body = bodyGo.AddComponent<TextMeshPro>();
            _body.text = "...";
            _body.fontSize = 0.55f;
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.color = BodyInk;
            _body.outlineColor = new Color32(255, 255, 255, 200);
            _body.outlineWidth = 0.06f;
            _body.enableWordWrapping = true;
            _body.font = LoadFont() ?? _body.font;
            _body.rectTransform.pivot = new Vector2(0f, 1f);
            _body.rectTransform.sizeDelta = new Vector2(PanelW - 0.20f, bodyH);
            _body.ForceMeshUpdate(true, true);

            _card.SetActive(true);
        }

        public void SetText(string body)
        {
            if (_body == null) return;
            _body.text = body ?? string.Empty;
            _body.fontStyle = FontStyles.Normal;
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.ForceMeshUpdate(true, true);
            if (_portrait != null) _portrait.SetActive(true);
            if (_speaker != null) _speaker.gameObject.SetActive(true);
        }

        public void SetStageDirection(string text)
        {
            if (_body == null) return;
            _body.text = text ?? string.Empty;
            _body.fontStyle = FontStyles.Italic;
            _body.alignment = TextAlignmentOptions.Center;
            _body.ForceMeshUpdate(true, true);
            if (_portrait != null) _portrait.SetActive(false);
            if (_speaker != null) _speaker.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (_card != null) _card.SetActive(true);
        }

        public void Hide()
        {
            if (_card != null) _card.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            var camPos = _cam.transform.position;
            var dir = transform.position - camPos;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // Builds a 9-sliced sprite from the panel-rounded-sm texture so
        // the corners stay fixed in world space when we resize the panel.
        // Computed once and cached.
        private static Sprite Get9SliceSprite()
        {
            if (_panelSprite != null) return _panelSprite;
            var tex = Resources.Load<Texture2D>("UI/panel-rounded-sm");
            if (tex == null)
            {
                Debug.LogWarning("[RPGDialogueBox] Resources/UI/panel-rounded-sm.png missing — using flat sprite.");
                tex = Texture2D.whiteTexture;
            }
            // Border in pixels: ~12% of the texture's smaller dimension so
            // rounded corners survive even very wide panel aspect ratios.
            float border = Mathf.Min(tex.width, tex.height) * 0.18f;
            _panelSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            return _panelSprite;
        }

        private static Material MakePortraitMaterial(Texture2D portrait)
        {
            var sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (portrait != null)
            {
                mat.mainTexture = portrait;
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", portrait);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", portrait);
            }
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            return mat;
        }

        private static TMP_FontAsset LoadFont()
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
