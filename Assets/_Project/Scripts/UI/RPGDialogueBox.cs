using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// World-space dialogue panel. Backing is a flat tinted quad (no
    /// rounded corners — they stretched ugly when the panel was scaled
    /// non-uniformly). Speaker + body are TextMeshPro components using
    /// the project default font, mirroring the legacy subtitleLabel
    /// pattern that's known to render reliably.
    /// </summary>
    public class RPGDialogueBox : MonoBehaviour
    {
        private const float PanelW = 2.2f;
        private const float PanelH = 0.95f;

        private GameObject _card;
        private GameObject _portrait;
        private TextMeshPro _speaker;
        private TextMeshPro _body;
        private Camera _cam;

        private static readonly Color PaperCream    = new Color(0.99f, 0.97f, 0.92f, 1f);
        private static readonly Color BorderInk     = new Color(0.10f, 0.10f, 0.16f, 1f);
        private static readonly Color SpeakerAccent = new Color(0.10f, 0.40f, 0.65f, 1f);
        private static readonly Color BodyInk       = new Color(0.07f, 0.06f, 0.10f, 1f);

        public void Initialize(Transform parent, string speakerName, Texture2D portrait)
        {
            transform.SetParent(parent, false);
            // Sit clearly above the "Professor" billboard label (which is
            // pinned at y=2.15 on the figure) — leaves ~0.6m of breathing
            // room between the bottom of the panel and the top of the tag.
            transform.localPosition = new Vector3(0f, 3.2f, 0f);
            transform.localRotation = Quaternion.identity;

            _card = new GameObject("DialogueCard");
            _card.transform.SetParent(transform, false);
            _card.transform.localPosition = Vector3.zero;

            // Border (slightly larger, dark) sits behind the cream panel.
            var border = MakeQuad(_card.transform, "Border",
                localPos: new Vector3(0f, 0f, 0.002f),
                scale:    new Vector3(PanelW + 0.06f, PanelH + 0.06f, 1f),
                color:    BorderInk);

            // Cream backing panel.
            var bg = MakeQuad(_card.transform, "Backing",
                localPos: new Vector3(0f, 0f, 0.001f),
                scale:    new Vector3(PanelW, PanelH, 1f),
                color:    PaperCream);

            float panelTop  = PanelH * 0.5f;
            float panelLeft = -PanelW * 0.5f;

            // Portrait quad in the top-left. If the caller passed null
            // for the texture, try to bake a head shot of the parent's
            // model (e.g. the Adventurer's actual face) instead of the
            // doodle smiley fallback.
            _portrait = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _portrait.name = "Portrait";
            var portCol = _portrait.GetComponent<Collider>(); if (portCol != null) Destroy(portCol);
            _portrait.transform.SetParent(_card.transform, false);
            _portrait.transform.localPosition = new Vector3(panelLeft + 0.16f, panelTop - 0.17f, -0.001f);
            _portrait.transform.localScale    = new Vector3(0.22f, 0.22f, 1f);

            // Apply portrait. If caller passed null, bake from the model
            // — but defer one frame so the Animator has a chance to tick
            // and skinned-mesh bounds are valid (otherwise we'd render an
            // empty frame because the SMR bones are still at default).
            _portrait.GetComponent<Renderer>().sharedMaterial = MakePortraitMaterial(portrait);
            if (portrait == null)
            {
                StartCoroutine(BakeAndApplyPortrait(parent));
            }

            // Speaker name — vertically centered with the portrait quad
            // (Left-Middle pivot anchors the text on the same Y as the
            // portrait's center, so the name sits "next to the face"
            // instead of floating high above it).
            var speakerGo = new GameObject("SpeakerName");
            speakerGo.transform.SetParent(_card.transform, false);
            speakerGo.transform.localPosition = new Vector3(panelLeft + 0.32f, panelTop - 0.17f, -0.001f);
            _speaker = speakerGo.AddComponent<TextMeshPro>();
            _speaker.text       = speakerName ?? string.Empty;
            _speaker.fontSize   = 1.2f;
            _speaker.fontStyle  = FontStyles.Bold;
            _speaker.alignment  = TextAlignmentOptions.Left;
            _speaker.color      = SpeakerAccent;
            _speaker.enableWordWrapping = false;
            var sophiecomic = LoadSophiecomicFont();
            if (sophiecomic != null) _speaker.font = sophiecomic;
            _speaker.rectTransform.pivot = new Vector2(0f, 0.5f);
            _speaker.rectTransform.sizeDelta = new Vector2(PanelW - 0.45f, 0.25f);

            // Body — fills the bottom 2/3 of the panel. Auto-sizing so the
            // text always fits even if a line of dialogue is short or long.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_card.transform, false);
            bodyGo.transform.localPosition = new Vector3(panelLeft + 0.40f, panelTop - 0.30f, -0.001f);
            _body = bodyGo.AddComponent<TextMeshPro>();
            _body.text = "...";
            _body.fontSize          = 1.0f;
            _body.enableAutoSizing  = true;
            _body.fontSizeMin       = 0.5f;
            _body.fontSizeMax       = 1.4f;
            _body.alignment         = TextAlignmentOptions.TopLeft;
            _body.color             = BodyInk;
            _body.enableWordWrapping = true;
            if (sophiecomic != null) _body.font = sophiecomic;
            _body.rectTransform.pivot = new Vector2(0f, 1f);
            _body.rectTransform.sizeDelta = new Vector2(PanelW - 0.50f, PanelH - 0.45f);

            _card.SetActive(true);
        }

        public void SetText(string body)
        {
            if (_body == null)
            {
                Debug.LogWarning("[RPGDialogueBox] SetText called before Initialize.");
                return;
            }
            _body.text = body ?? string.Empty;
            _body.fontStyle = FontStyles.Normal;
            _body.alignment = TextAlignmentOptions.Center;
            if (_portrait != null) _portrait.SetActive(true);
            if (_speaker != null) _speaker.gameObject.SetActive(true);
        }

        public void SetStageDirection(string text)
        {
            if (_body == null) return;
            _body.text = text ?? string.Empty;
            _body.fontStyle = FontStyles.Italic;
            _body.alignment = TextAlignmentOptions.Center;
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

        private System.Collections.IEnumerator BakeAndApplyPortrait(Transform modelRoot)
        {
            Texture2D baked = null;
            yield return HeadshotBaker.Bake(modelRoot, t => baked = t);
            if (baked != null && _portrait != null)
            {
                _portrait.GetComponent<Renderer>().sharedMaterial = MakePortraitMaterial(baked);
            }
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

        private static GameObject MakeQuad(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
            q.transform.SetParent(parent, false);
            q.transform.localPosition = localPos;
            q.transform.localScale    = scale;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            q.GetComponent<Renderer>().sharedMaterial = mat;
            return q;
        }

        private static Material MakePortraitMaterial(Texture portrait)
        {
            // Use Unlit so the portrait stays bright regardless of scene
            // lighting (otherwise dim scenes turn the headshot pitch black).
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
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

        private static TMP_FontAsset _cachedFont;
        private static TMP_FontAsset LoadSophiecomicFont()
        {
            if (_cachedFont != null) return _cachedFont;
            _cachedFont = Resources.Load<TMP_FontAsset>("Fonts/sophiecomic SDF");
            return _cachedFont;
        }
    }
}
