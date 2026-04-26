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
        [Tooltip("Visible head sphere radius (metres) attached to the synced head transform.")]
        [SerializeField] private float headRadius  = 0.20f;
        [Tooltip("Vertical offset (metres) lifting the head sphere above the synced head transform so it sits above the torso instead of sinking into it.")]
        [SerializeField] private float headLift    = 0.08f;

        public Renderer[] BodyRenderers { get; private set; }

        private Transform _bodyT, _hatT, _headSphereT;
        private Transform _legL, _legR;
        private Transform _armL, _armR;
        private Material  _bodyMat;
        private Material  _accentMat;
        private static Material _faceMat;

        // Display name for the floating billboard label above the head.
        // Default to "Player"; PlayerAvatar (or any other spawner) can call
        // SetDisplayName("Player 1") / ("Player 2") to differentiate slots.
        [SerializeField] private string displayName = "Player";
        private Tigerverse.UI.BillboardLabel _nameLabel;
        public void SetDisplayName(string name)
        {
            displayName = name;
            if (_nameLabel != null) _nameLabel.SetText(name);
        }

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
            // Built lazily, parented to the head once headSrc is non-null.

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

        // Builds the visible head: a paper-white sphere centered on the synced
        // head transform, plus a doodle face quad pinned to its front so other
        // players can see the avatar's "face". Idempotent like EnsureHat.
        private void EnsureHead()
        {
            if (_headSphereT != null || headSrc == null) return;

            // Sphere body — share the body material so paper texture matches.
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Head";
            var sCol = sphere.GetComponent<Collider>(); if (sCol != null) Destroy(sCol);
            sphere.transform.SetParent(headSrc, false);
            sphere.transform.localPosition = new Vector3(0f, headLift, 0f);
            sphere.transform.localScale = Vector3.one * (headRadius * 2f);
            sphere.GetComponent<Renderer>().sharedMaterial = _bodyMat;
            _headSphereT = sphere.transform;

            // Face quad on the sphere's front. The sphere's local -Z is the
            // forward direction in head-local space.
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            var fCol = face.GetComponent<Collider>(); if (fCol != null) Destroy(fCol);
            face.transform.SetParent(sphere.transform, false);
            // Same convention as the professor: head-local -Z is "front", and
            // a default Quad's normal +Z naturally points back outward.
            face.transform.localPosition = new Vector3(0f, 0.05f, -0.51f);
            face.transform.localRotation = Quaternion.identity;
            face.transform.localScale = new Vector3(0.42f, 0.5f, 1f);
            face.GetComponent<Renderer>().sharedMaterial = MakeFaceMaterial();

            // Floating "Player 1" / "Player 2" tag above the head.
            if (_nameLabel == null)
                _nameLabel = Tigerverse.UI.BillboardLabel.Create(headSrc, displayName, yOffset: headRadius + 0.18f);
        }

        private static Material MakeFaceMaterial()
        {
            if (_faceMat != null) return _faceMat;
            // Legacy Unlit/Transparent: hardcoded alpha blending, no shader
            // variant gymnastics. URP/Unlit + SetFloat("_Surface", 1) was
            // silently rendering opaque (black square around the face)
            // because the transparent variant gets stripped at build.
            var sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _faceMat = new Material(sh);
            var face = Resources.Load<Texture2D>("face");
            if (face != null)
            {
                _faceMat.mainTexture = face;
                if (_faceMat.HasProperty("_MainTex")) _faceMat.SetTexture("_MainTex", face);
                if (_faceMat.HasProperty("_BaseMap")) _faceMat.SetTexture("_BaseMap", face);
            }
            if (_faceMat.HasProperty("_Color")) _faceMat.SetColor("_Color", Color.white);
            if (_faceMat.HasProperty("_BaseColor")) _faceMat.SetColor("_BaseColor", Color.white);
            return _faceMat;
        }

        private void LateUpdate()
        {
            if (headSrc == null) return;
            EnsureHat();
            EnsureHead();

            // Body anchored below the head, looking the same way (yaw only).
            Vector3 bodyPos = headSrc.position + Vector3.down * bodyDropFromHead;
            float yaw = headSrc.eulerAngles.y;
            transform.position = bodyPos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Legs hang below body, they don't animate, just bob with body.
            if (_legL != null) _legL.localPosition = new Vector3(-legSeparation, -bodyHeight * 0.5f - legHeight * 0.5f - legDropFromBody * 0f, 0f);
            if (_legR != null) _legR.localPosition = new Vector3( legSeparation, -bodyHeight * 0.5f - legHeight * 0.5f - legDropFromBody * 0f, 0f);

            // Arms, stretch a thin rectangular cube between shoulder and hand.
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
