using UnityEngine;

namespace Tigerverse.Net
{
    /// <summary>
    /// Procedural paper-craft humanoid body, attached at runtime to the
    /// existing PlayerAvatar head/hand transforms. Built out of primitives
    /// (sphere head, cylinder body, stretchy arm boxes, hanging leg
    /// cylinders) so it reads as a person without needing any external
    /// assets. Body anchors below the head; arms stretch from shoulders to
    /// each hand transform every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class PaperHumanoid : MonoBehaviour
    {
        [Header("Sources (synced transforms)")]
        public Transform headSrc;
        public Transform leftHandSrc;
        public Transform rightHandSrc;

        [Header("Body proportions (metres)")]
        [SerializeField] private float bodyDropFromHead   = 0.32f;
        [SerializeField] private float bodyHeight         = 0.55f;
        [SerializeField] private float bodyRadius         = 0.16f;
        [SerializeField] private float legDropFromBody    = 0.30f;
        [SerializeField] private float legHeight          = 0.55f;
        [SerializeField] private float legRadius          = 0.07f;
        [SerializeField] private float legSeparation      = 0.10f;
        [SerializeField] private float armRadius          = 0.05f;
        [SerializeField] private float shoulderHeight     = 0.20f;  // up from body centre
        [SerializeField] private float shoulderSeparation = 0.20f;  // out from body centre

        [Header("Look")]
        [SerializeField] private Color bodyColor   = new Color(0.97f, 0.95f, 0.91f);
        [SerializeField] private Color accentColor = new Color(0.30f, 0.45f, 0.85f);
        [SerializeField] private bool  showHat     = true;

        public Renderer[] BodyRenderers { get; private set; }

        private Transform _bodyT, _hatT;
        private Transform _legL, _legR;
        private Transform _armL, _armR;
        private Material  _bodyMat;
        private Material  _accentMat;

        private void Awake()
        {
            BuildBody();
        }

        public void SetBodyColor(Color body, Color accent)
        {
            bodyColor = body;
            accentColor = accent;
            if (_bodyMat != null) SetMatColor(_bodyMat, body);
            if (_accentMat != null) SetMatColor(_accentMat, accent);
        }

        private static void SetMatColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
        }

        private void BuildBody()
        {
            Shader litSh = Shader.Find("Universal Render Pipeline/Lit");
            if (litSh == null) litSh = Shader.Find("Standard");

            _bodyMat   = new Material(litSh);
            _accentMat = new Material(litSh);
            SetMatColor(_bodyMat,   bodyColor);
            SetMatColor(_accentMat, accentColor);

            // Try to apply Paper003 to body so it matches the rest of the world.
            var paperTex = LoadPaperTex("Color");
            if (paperTex != null)
            {
                if (_bodyMat.HasProperty("_BaseMap")) _bodyMat.SetTexture("_BaseMap", paperTex);
                if (_bodyMat.HasProperty("_MainTex")) _bodyMat.SetTexture("_MainTex", paperTex);
            }

            _bodyT = MakePrim(PrimitiveType.Cylinder, _bodyMat, "Body",
                localPos: Vector3.zero,
                localScale: new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f));

            // Hat sits on top of the head (head transform is synced separately,
            // we attach the hat as a child of headSrc so it follows).
            // Built lazily — parented to the head once headSrc is non-null.

            _legL = MakePrim(PrimitiveType.Cylinder, _bodyMat, "LegL",
                localPos: new Vector3(-legSeparation, 0f, 0f),
                localScale: new Vector3(legRadius * 2f, legHeight * 0.5f, legRadius * 2f));
            _legR = MakePrim(PrimitiveType.Cylinder, _bodyMat, "LegR",
                localPos: new Vector3( legSeparation, 0f, 0f),
                localScale: new Vector3(legRadius * 2f, legHeight * 0.5f, legRadius * 2f));

            _armL = MakePrim(PrimitiveType.Cube, _accentMat, "ArmL",
                localPos: Vector3.zero, localScale: new Vector3(armRadius * 2f, 0.3f, armRadius * 2f));
            _armR = MakePrim(PrimitiveType.Cube, _accentMat, "ArmR",
                localPos: Vector3.zero, localScale: new Vector3(armRadius * 2f, 0.3f, armRadius * 2f));

            // Collect renderers for tinting hooks.
            BodyRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        private Transform MakePrim(PrimitiveType type, Material mat, string name, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        private void EnsureHat()
        {
            if (!showHat || _hatT != null || headSrc == null) return;
            Shader litSh = Shader.Find("Universal Render Pipeline/Lit");
            if (litSh == null) litSh = Shader.Find("Standard");
            var mat = new Material(litSh);
            SetMatColor(mat, accentColor);
            var hat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hat.name = "Hat";
            var col = hat.GetComponent<Collider>(); if (col != null) Destroy(col);
            hat.transform.SetParent(headSrc, false);
            hat.transform.localPosition = new Vector3(0f, 0.13f, 0f);
            hat.transform.localScale = new Vector3(0.18f, 0.05f, 0.18f);
            hat.GetComponent<Renderer>().sharedMaterial = mat;
            _hatT = hat.transform;
        }

        private void LateUpdate()
        {
            if (headSrc == null) return;
            EnsureHat();

            // Body anchored below the head, looking the same way (yaw only).
            Vector3 bodyPos = headSrc.position + Vector3.down * bodyDropFromHead;
            float yaw = headSrc.eulerAngles.y;
            transform.position = bodyPos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Legs hang below body — they don't animate, just bob with body.
            if (_legL != null) _legL.localPosition = new Vector3(-legSeparation, -bodyHeight * 0.5f - legHeight * 0.5f - legDropFromBody * 0f, 0f);
            if (_legR != null) _legR.localPosition = new Vector3( legSeparation, -bodyHeight * 0.5f - legHeight * 0.5f - legDropFromBody * 0f, 0f);

            // Arms — stretch a thin rectangular cube between shoulder and hand.
            UpdateArm(_armL, leftShoulderWorld(),  leftHandSrc);
            UpdateArm(_armR, rightShoulderWorld(), rightHandSrc);
        }

        private Vector3 leftShoulderWorld()
        {
            return transform.TransformPoint(new Vector3(-shoulderSeparation, shoulderHeight, 0f));
        }
        private Vector3 rightShoulderWorld()
        {
            return transform.TransformPoint(new Vector3( shoulderSeparation, shoulderHeight, 0f));
        }

        private void UpdateArm(Transform arm, Vector3 shoulderWS, Transform handSrc)
        {
            if (arm == null) return;
            Vector3 handWS = handSrc != null ? handSrc.position : shoulderWS + Vector3.down * 0.3f;
            Vector3 mid    = (shoulderWS + handWS) * 0.5f;
            Vector3 dir    = handWS - shoulderWS;
            float   len    = dir.magnitude;
            if (len < 1e-3f) len = 1e-3f;

            arm.position = mid;
            arm.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            arm.localScale = new Vector3(armRadius * 2f, len * 0.5f, armRadius * 2f);
        }

        private static Texture2D[] _allPaperTextures;
        private static Texture2D LoadPaperTex(string suffix)
        {
            if (_allPaperTextures == null)
                _allPaperTextures = Resources.LoadAll<Texture2D>("PaperTextures");
            foreach (var t in _allPaperTextures)
            {
                if (t == null) continue;
                if (t.name.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return null;
        }
    }
}
