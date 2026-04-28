using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// World-space RPG-style dialogue panel: backing card + portrait + speaker
    /// header + body text. Built procedurally to match the project's existing
    /// procedural-card pattern (see MonsterHoverStats.BuildCard) — all children
    /// are 3D quads / TextMeshPro components, no Canvas, billboarded toward
    /// the camera each LateUpdate.
    /// </summary>
    public class RPGDialogueBox : MonoBehaviour
    {
        private GameObject _card;
        private GameObject _portrait;
        private TextMeshPro _speaker;
        private TextMeshPro _body;
        private Camera _cam;

        private static Material _panelMat;
        private static TMP_FontAsset _cachedFont;

        private static readonly Color PaperCream = new Color(0.99f, 0.97f, 0.92f, 1f);
        private static readonly Color SpeakerAccent = new Color(0.10f, 0.40f, 0.65f, 1f);
        private static readonly Color BodyInk = new Color(0.07f, 0.06f, 0.10f, 1f);

        public void Initialize(Transform parent, string speakerName, Texture2D portrait)
        {
            transform.SetParent(parent, false);
            transform.localPosition = new Vector3(0f, 2.4f, 0f);
            transform.localRotation = Quaternion.identity;

            _card = new GameObject("DialogueCard");
            _card.transform.SetParent(transform, false);
            _card.transform.localPosition = Vector3.zero;

            // Backing panel — single quad, panel texture, paper-cream tint.
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Backing";
            var bgCol = bg.GetComponent<Collider>(); if (bgCol != null) Destroy(bgCol);
            bg.transform.SetParent(_card.transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            bg.transform.localScale = new Vector3(1.4f, 0.55f, 1f);
            bg.GetComponent<Renderer>().sharedMaterial = MakePanelMaterial();

            // Portrait — half above the panel's top edge, on the left.
            _portrait = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _portrait.name = "Portrait";
            var portCol = _portrait.GetComponent<Collider>(); if (portCol != null) Destroy(portCol);
            _portrait.transform.SetParent(_card.transform, false);
            _portrait.transform.localPosition = new Vector3(-0.55f, 0.18f, -0.002f);
            _portrait.transform.localScale = new Vector3(0.20f, 0.20f, 1f);
            _portrait.GetComponent<Renderer>().sharedMaterial = MakePortraitMaterial(portrait);

            // Speaker name strip — top of panel body, right of portrait.
            var speakerGo = new GameObject("SpeakerName");
            speakerGo.transform.SetParent(_card.transform, false);
            speakerGo.transform.localPosition = new Vector3(-0.05f, 0.17f, 0f);
            _speaker = speakerGo.AddComponent<TextMeshPro>();
            _speaker.text = speakerName ?? string.Empty;
            _speaker.fontSize = 0.85f;
            _speaker.fontStyle = FontStyles.Bold;
            _speaker.alignment = TextAlignmentOptions.Left;
            _speaker.color = SpeakerAccent;
            _speaker.enableWordWrapping = false;
            _speaker.font = LoadSophiecomicFont() ?? _speaker.font;
            _speaker.rectTransform.sizeDelta = new Vector2(0.95f, 0.10f);

            // Body text — centered within the panel body.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_card.transform, false);
            bodyGo.transform.localPosition = new Vector3(0.05f, -0.05f, 0f);
            _body = bodyGo.AddComponent<TextMeshPro>();
            _body.text = string.Empty;
            _body.fontSize = 0.75f;
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.color = BodyInk;
            _body.outlineColor = new Color32(255, 255, 255, 220);
            _body.outlineWidth = 0.08f;
            _body.enableWordWrapping = true;
            _body.font = LoadSophiecomicFont() ?? _body.font;
            _body.rectTransform.sizeDelta = new Vector2(1.15f, 0.42f);

            _card.SetActive(true);
        }

        public void SetText(string body)
        {
            if (_body == null) return;
            _body.text = body ?? string.Empty;
            _body.fontStyle = FontStyles.Normal;
            _body.alignment = TextAlignmentOptions.TopLeft;
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

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Compute world-space LookRotation directly so any sway/rotation on
            // the parent (e.g. PaperProfessor's idle sway) doesn't drag the
            // dialogue card off-axis.
            var camPos = _cam.transform.position;
            var dir = transform.position - camPos;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        private static Material MakePanelMaterial()
        {
            if (_panelMat != null) return _panelMat;
            var sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _panelMat = new Material(sh);
            var tex = Resources.Load<Texture2D>("UI/panel-rounded-sm");
            if (tex != null)
            {
                _panelMat.mainTexture = tex;
                if (_panelMat.HasProperty("_MainTex")) _panelMat.SetTexture("_MainTex", tex);
                if (_panelMat.HasProperty("_BaseMap")) _panelMat.SetTexture("_BaseMap", tex);
            }
            if (_panelMat.HasProperty("_Color")) _panelMat.SetColor("_Color", PaperCream);
            if (_panelMat.HasProperty("_BaseColor")) _panelMat.SetColor("_BaseColor", PaperCream);
            return _panelMat;
        }

        private static Material MakePortraitMaterial(Texture2D portrait)
        {
            // Each instance gets its own portrait material — the texture varies
            // by speaker, so a shared static would clobber other dialogue boxes.
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
