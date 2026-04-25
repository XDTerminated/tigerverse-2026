using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Procedural paper-craft "Professor" NPC. Built entirely out of
    /// primitives at runtime — head sphere, body cylinder, arms, hat —
    /// shaded paper-white. Includes a subtle idle bob and a SpeakingPulse()
    /// animation hook the tutorial calls per spoken line so the figure
    /// gestures while talking.
    /// </summary>
    [DisallowMultipleComponent]
    public class PaperProfessor : MonoBehaviour
    {
        [Header("Pose")]
        [SerializeField] private float idleBobAmplitude = 0.015f;
        [SerializeField] private float idleBobHz        = 0.6f;
        [SerializeField] private float idleSwayDeg      = 4f;
        [SerializeField] private float idleSwayHz       = 0.4f;

        [Header("Speaking gesture")]
        [SerializeField] private float speakArmWaveDeg  = 32f;
        [SerializeField] private float speakHeadNodDeg  = 6f;
        [SerializeField] private float speakDuration    = 1.0f;

        private Transform _head, _body, _hat, _leftArm, _rightArm;
        private Vector3   _baseHeadLocal;
        private Quaternion _baseLeftArmRot, _baseRightArmRot, _baseHeadRot;
        private float     _phase;
        private float     _speakT = -10f;

        private void Awake()
        {
            BuildBody();
        }

        private void BuildBody()
        {
            // Pivot at the figure's feet (Y=0). Total height ~1.2m.
            //   Body cyl: y 0..0.7
            //   Head:     y 0.78
            //   Hat:      y 0.95
            //   Arms:     y 0.55, sticking out

            // Cache shared materials.
            Material paperMat   = MakePaperMaterial(new Color(0.97f, 0.95f, 0.91f));
            Material darkMat    = MakeUnlitColor(new Color(0.10f, 0.10f, 0.12f));
            Material accentMat  = MakeUnlitColor(new Color(0.22f, 0.18f, 0.55f));   // wizard-y purple
            Material mouthMat   = MakeUnlitColor(new Color(0.06f, 0.05f, 0.08f));

            _body = MakePrim(PrimitiveType.Cylinder, paperMat, "Body", localPos: new Vector3(0, 0.35f, 0), localScale: new Vector3(0.32f, 0.35f, 0.32f));

            _head = MakePrim(PrimitiveType.Sphere, paperMat, "Head", localPos: new Vector3(0, 0.84f, 0), localScale: new Vector3(0.32f, 0.32f, 0.32f));
            _baseHeadLocal = _head.localPosition;
            _baseHeadRot   = _head.localRotation;

            // Wizard hat — cone-ish (use a cylinder with a tapered scale to fake a cone).
            _hat = MakePrim(PrimitiveType.Cylinder, accentMat, "Hat", localPos: new Vector3(0, 1.04f, 0), localScale: new Vector3(0.18f, 0.20f, 0.18f));
            _hat.SetParent(_head, worldPositionStays: true);

            // Hat brim (flat cylinder underneath).
            var brim = MakePrim(PrimitiveType.Cylinder, accentMat, "HatBrim", localPos: new Vector3(0, 0.95f, 0), localScale: new Vector3(0.30f, 0.012f, 0.30f));
            brim.SetParent(_head, worldPositionStays: true);

            // Eyes — small dark spheres on the front of the head.
            MakePrim(PrimitiveType.Sphere, darkMat, "EyeL", localPos: new Vector3(-0.08f, 0.86f, -0.13f), localScale: new Vector3(0.04f, 0.045f, 0.04f), parent: _head);
            MakePrim(PrimitiveType.Sphere, darkMat, "EyeR", localPos: new Vector3( 0.08f, 0.86f, -0.13f), localScale: new Vector3(0.04f, 0.045f, 0.04f), parent: _head);
            // Mouth bar.
            MakePrim(PrimitiveType.Cube,   mouthMat,"Mouth",  localPos: new Vector3(0f,    0.78f, -0.155f), localScale: new Vector3(0.10f, 0.012f, 0.01f), parent: _head);

            // Arms — two thin elongated cubes with rotation pivots near the shoulder.
            _leftArm  = MakeArm(name: "ArmL", paperMat, shoulder: new Vector3(-0.20f, 0.62f, 0f), tilt: 18f);
            _rightArm = MakeArm(name: "ArmR", paperMat, shoulder: new Vector3( 0.20f, 0.62f, 0f), tilt: -18f);
            _baseLeftArmRot  = _leftArm.localRotation;
            _baseRightArmRot = _rightArm.localRotation;
        }

        private Transform MakeArm(string name, Material mat, Vector3 shoulder, float tilt)
        {
            // Pivot GO at the shoulder. The mesh hangs DOWN from there so
            // rotating the pivot rotates the arm naturally.
            var pivot = new GameObject(name);
            pivot.transform.SetParent(transform, worldPositionStays: false);
            pivot.transform.localPosition = shoulder;
            pivot.transform.localRotation = Quaternion.Euler(0, 0, tilt);

            var sleeve = MakePrim(PrimitiveType.Cube, mat, name + "_Sleeve",
                localPos: new Vector3(0, -0.18f, 0),
                localScale: new Vector3(0.08f, 0.30f, 0.08f),
                parent: pivot.transform);

            return pivot.transform;
        }

        private Transform MakePrim(PrimitiveType type, Material mat, string name, Vector3 localPos, Vector3 localScale, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            go.transform.SetParent(parent != null ? parent : transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Material MakePaperMaterial(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            // Try to attach the shared paper texture for fibre detail.
            var paper = LoadPaperTex("Color");
            if (paper != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", paper);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", paper);
            }
            return mat;
        }

        private static Material MakeUnlitColor(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
            return mat;
        }

        private static Texture2D[] _allPaper;
        private static Texture2D LoadPaperTex(string suffix)
        {
            if (_allPaper == null) _allPaper = Resources.LoadAll<Texture2D>("PaperTextures");
            foreach (var t in _allPaper)
            {
                if (t == null) continue;
                if (t.name.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return null;
        }

        private void Update()
        {
            _phase += Time.deltaTime;

            // Idle sway (whole body).
            float sway = Mathf.Sin(_phase * idleSwayHz * Mathf.PI * 2f) * idleSwayDeg;
            transform.localRotation = Quaternion.Euler(0, 0, sway);

            // Idle bob (head only).
            if (_head != null)
            {
                Vector3 hp = _baseHeadLocal;
                hp.y += Mathf.Sin(_phase * idleBobHz * Mathf.PI * 2f) * idleBobAmplitude;
                _head.localPosition = hp;
            }

            // Speaking pulse: animate arms + head nodding.
            float speakElapsed = Time.time - _speakT;
            if (speakElapsed >= 0f && speakElapsed < speakDuration && _leftArm != null && _rightArm != null && _head != null)
            {
                float k = Mathf.Clamp01(speakElapsed / speakDuration);
                // Smooth in-out window so we don't snap on stop.
                float window = Mathf.Sin(k * Mathf.PI);
                float beat = Mathf.Sin(speakElapsed * 6f * Mathf.PI) * window;

                _rightArm.localRotation = _baseRightArmRot * Quaternion.Euler(beat * speakArmWaveDeg, 0, 0);
                _leftArm.localRotation  = _baseLeftArmRot  * Quaternion.Euler(beat * speakArmWaveDeg * 0.5f, 0, 0);

                _head.localRotation = _baseHeadRot * Quaternion.Euler(Mathf.Abs(beat) * speakHeadNodDeg, 0, 0);
            }
            else if (_leftArm != null && _rightArm != null && _head != null)
            {
                _leftArm.localRotation  = _baseLeftArmRot;
                _rightArm.localRotation = _baseRightArmRot;
                _head.localRotation     = _baseHeadRot;
            }
        }

        /// <summary>
        /// Trigger a speaking-gesture pulse. Call once per spoken line.
        /// </summary>
        public void SpeakingPulse(float duration = -1f)
        {
            _speakT = Time.time;
            if (duration > 0f) speakDuration = duration;
        }
    }
}
