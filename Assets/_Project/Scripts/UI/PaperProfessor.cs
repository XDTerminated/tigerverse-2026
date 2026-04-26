using System.Collections;
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
        private bool      _spawning;
        private bool      _leaving;

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

            // Suspend idle bob / sway / speaking pulses while a spawn or leave
            // animation is driving the figure directly.
            if (_spawning || _leaving) return;

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

        /// <summary>
        /// Poof-in spawn. Scales up from 0 with a tiny elastic overshoot,
        /// rises from y=-0.3 to y=0, waves arms once like a "ta-da", and
        /// emits a small white confetti puff at floor level.
        /// </summary>
        public IEnumerator PlaySpawnAnimation()
        {
            _spawning = true;

            // Cache the starting transform so we end exactly where we began.
            Vector3 endPos = transform.localPosition;
            Vector3 startPos = endPos + new Vector3(0f, -0.3f, 0f);
            transform.localPosition = startPos;
            transform.localScale    = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Reset arm rotations to their bind so we drive them cleanly.
            if (_leftArm  != null) _leftArm.localRotation  = _baseLeftArmRot;
            if (_rightArm != null) _rightArm.localRotation = _baseRightArmRot;

            // Confetti puff at floor level (world position).
            SpawnConfettiPuff(transform.position, new Color(1f, 1f, 1f));

            const float dur = 1.2f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);

                // Elastic-ish ease: overshoot at ~0.7 and settle to 1.0.
                // 0..0.7 -> 0..1.1, 0.7..1.0 -> 1.1..1.0
                float scale;
                if (k < 0.7f)
                {
                    float a = k / 0.7f;
                    // ease-out cubic up to 1.1
                    float e = 1f - Mathf.Pow(1f - a, 3f);
                    scale = e * 1.1f;
                }
                else
                {
                    float a = (k - 0.7f) / 0.3f;
                    scale = Mathf.Lerp(1.1f, 1.0f, a);
                }
                transform.localScale = new Vector3(scale, scale, scale);

                // Rise.
                float riseK = 1f - Mathf.Pow(1f - k, 2f);
                transform.localPosition = Vector3.Lerp(startPos, endPos, riseK);

                // Ta-da arm wave: lift both arms, peak around mid, return.
                float wave = Mathf.Sin(k * Mathf.PI);
                if (_leftArm != null)
                    _leftArm.localRotation  = _baseLeftArmRot  * Quaternion.Euler(0f, 0f,  60f * wave);
                if (_rightArm != null)
                    _rightArm.localRotation = _baseRightArmRot * Quaternion.Euler(0f, 0f, -60f * wave);

                yield return null;
            }

            // Snap to the bind pose.
            transform.localScale    = Vector3.one;
            transform.localPosition = endPos;
            if (_leftArm  != null) _leftArm.localRotation  = _baseLeftArmRot;
            if (_rightArm != null) _rightArm.localRotation = _baseRightArmRot;

            _spawning = false;
        }

        /// <summary>
        /// Wave-and-vanish leave. Right arm waves like a goodbye, then the
        /// figure scales down ~30%, drifts upward and fades alpha to zero
        /// (with a confetti puff). Caller should Destroy the GameObject after.
        /// </summary>
        public IEnumerator PlayLeaveAnimation()
        {
            _leaving = true;

            // --- 1. Friendly wave (right arm up + side-to-side oscillation). ---
            const float waveDur = 0.7f;
            float t = 0f;
            while (t < waveDur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / waveDur);
                // Ease-in then hold for the lift.
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 1.4f));
                // Side-to-side oscillation, ~3 cycles across the duration.
                float osc  = Mathf.Sin(k * Mathf.PI * 6f) * 25f;

                if (_rightArm != null)
                {
                    // Raise ~120 degrees on Z (arm pivots from shoulder, +Z lifts it up
                    // on the right side). Add Y oscillation for the side-to-side wave.
                    _rightArm.localRotation = _baseRightArmRot
                        * Quaternion.Euler(0f, osc, -120f * lift);
                }
                if (_leftArm != null)
                {
                    _leftArm.localRotation = _baseLeftArmRot;
                }
                yield return null;
            }

            // --- 2. Cache per-renderer cloned materials so we can fade alpha. ---
            var rends = GetComponentsInChildren<Renderer>(true);
            var mats  = new Material[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                // r.material auto-clones, so we don't mutate the shared paper mat.
                mats[i] = rends[i].material;
                TryMakeTransparent(mats[i]);
            }

            // --- 3. Confetti puff at the figure's position. ---
            SpawnConfettiPuff(transform.position + Vector3.up * 0.6f, new Color(1f, 1f, 1f));

            // --- 4. Fade + scale-down + drift-up. ---
            Vector3 startScale = transform.localScale;
            Vector3 endScale   = startScale * 0.7f;
            Vector3 startPos   = transform.localPosition;
            Vector3 endPos     = startPos + new Vector3(0f, 0.25f, 0f);

            // Capture each material's starting color so we can lerp alpha cleanly.
            var startColors = new Color[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                startColors[i] = mats[i].HasProperty("_BaseColor")
                    ? mats[i].GetColor("_BaseColor")
                    : mats[i].color;
            }

            const float fadeDur = 0.8f;
            t = 0f;
            while (t < fadeDur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / fadeDur);

                transform.localScale    = Vector3.Lerp(startScale, endScale, k);
                transform.localPosition = Vector3.Lerp(startPos,   endPos,   k);

                float a = 1f - k;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Color c = startColors[i];
                    c.a = startColors[i].a * a;
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c);
                    else mats[i].color = c;
                }

                // Hold the wave overhead while fading (tapers off quickly).
                if (_rightArm != null)
                {
                    float holdLift = 1f - k;
                    _rightArm.localRotation = _baseRightArmRot
                        * Quaternion.Euler(0f, 0f, -120f * holdLift);
                }

                yield return null;
            }

            // Final state: invisible. Hide all renderers as a hard guarantee in
            // case alpha didn't actually go transparent on opaque-pipeline mats.
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null) rends[i].enabled = false;
            }

            // Don't Destroy ourselves — caller does that.
        }

        /// <summary>
        /// Try to flip a URP/Lit (or Standard) material into transparent
        /// blend so an alpha lerp is visible. Best-effort: if none of the
        /// expected properties exist we just leave it; the leave animation
        /// also disables renderers at the end as a fallback.
        /// </summary>
        private static void TryMakeTransparent(Material m)
        {
            if (m == null) return;

            // URP Lit / Unlit: _Surface 0=Opaque, 1=Transparent.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", 0f);   // Alpha
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);

            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Procedural one-shot particle puff — small white cubes spraying
        /// outward. Pattern matches ProfessorTutorial.SpawnLightningEffect.
        /// </summary>
        private void SpawnConfettiPuff(Vector3 worldPos, Color tint)
        {
            var go = new GameObject("PaperConfettiFx");
            // Detach from this transform — we may be destroyed soon, and the
            // FX should outlive us long enough to play out.
            go.transform.position = worldPos;

            var ps  = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();

            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            psr.sharedMaterial = mat;

            // Use the default billboard render mode — small white squares look
            // like paper confetti without needing a custom mesh.
            psr.renderMode = ParticleSystemRenderMode.Billboard;

            var main = ps.main;
            main.playOnAwake     = false;
            main.duration        = 0.3f;
            main.loop            = false;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f), new Color(0.95f, 0.92f, 0.85f));
            main.maxParticles    = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.6f);

            var emission = ps.emission;
            emission.enabled      = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });

            var shape = ps.shape;
            shape.enabled    = true;
            shape.shapeType  = ParticleSystemShapeType.Sphere;
            shape.radius     = 0.08f;

            ps.Play();
            Destroy(go, 1.5f);
        }
    }
}
