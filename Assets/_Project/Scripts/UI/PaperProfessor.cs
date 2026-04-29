using System.Collections;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// FBX-backed Professor NPC. Loads the Adventurer prefab from
    /// Resources at Awake and drives it with parent-transform animations
    /// (idle bob, spawn poof-in, leave wave-and-fade). Speaking pulses
    /// trigger the animator's "Speak" trigger if present.
    /// </summary>
    [DisallowMultipleComponent]
    public class PaperProfessor : MonoBehaviour
    {
        [Header("Pose")]
        [SerializeField] private float idleBobAmplitude = 0.012f;
        [SerializeField] private float idleBobHz        = 0.6f;
        [SerializeField] private float idleSwayDeg      = 2f;
        [SerializeField] private float idleSwayHz       = 0.4f;

        [Header("Look at player")]
        [Tooltip("If true, smoothly rotates the body to face the local camera with eased delay.")]
        [SerializeField] private bool  lookAtPlayer      = true;
        [Tooltip("Approximate time (seconds) for SmoothDamp to reach the player's direction. Higher = lazier follow, lower = snappier.")]
        [SerializeField] private float lookAtSmoothTime  = 0.55f;
        [Tooltip("Yaw delta (degrees) the player can drift off the body's facing before we start rotating. Stops the Professor from twitching when the player makes tiny head moves.")]
        [SerializeField] private float lookAtDeadzoneDeg = 8f;

        [Header("Speaking gesture")]
        [SerializeField] private float speakDuration    = 1.0f;

        [Header("Model")]
        [Tooltip("Resources/Characters prefab to load.")]
        [SerializeField] private string prefabName = "Adventurer";

        private Transform _model;
        private Animator  _animator;
        private Vector3   _baseModelLocalPos;
        private float     _phase;
        private float     _speakT = -10f;
        private bool      _spawning;
        private bool      _leaving;

        // Procedural clap rig — bones we override in LateUpdate to bring
        // the hands together since the Adventurer FBX has no clap clip.
        private Transform _upperArmL, _upperArmR, _lowerArmL, _lowerArmR;
        private Quaternion _restUpperL, _restUpperR, _restLowerL, _restLowerR;
        private float _clapStartT = -10f;
        private const float ClapTotalDur = 0.7f;

        // Eased look-at-player state.
        private Camera    _cam;
        private float     _currentYaw;
        private float     _targetYaw;
        private float     _yawVel;
        private bool      _yawInitialised;
        private float     _baseLocalYaw;

        private static readonly int SpeakHash = Animator.StringToHash("Speak");
        private static readonly int CastHash  = Animator.StringToHash("Cast");
        private static readonly int HitHash   = Animator.StringToHash("Hit");
        private static readonly int PointHash = Animator.StringToHash("Point");

        /// <summary>
        /// Fires the "Point" trigger on the Adventurer animator. Wire a Point
        /// state in Resources/Characters/Adventurer.controller driven by a
        /// Trigger param named "Point" with the FBX's pointing clip. Safe to
        /// call before that wiring exists — it silently no-ops.
        /// </summary>
        public void PlayPoint() => TryFireTrigger(PointHash);

        private void Awake()
        {
            BuildBody();

            // Snapshot the local yaw set by whoever spawned us (e.g.
            // ProfessorTutorial.BuildScene calls LookRotation(_stageForward)).
            // We use this as the rest yaw when look-at-player is disabled.
            _baseLocalYaw = transform.localRotation.eulerAngles.y;
            _currentYaw   = _baseLocalYaw;
        }

        private void BuildBody()
        {
            var prefab = Resources.Load<GameObject>("Characters/" + prefabName);
            if (prefab == null)
            {
                Debug.LogError($"[PaperProfessor] Missing Resources/Characters/{prefabName}.prefab");
                return;
            }
            var inst = Instantiate(prefab, transform);
            inst.name = prefabName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            _model = inst.transform;
            _baseModelLocalPos = _model.localPosition;
            _animator = inst.GetComponentInChildren<Animator>();

            // Cache the arm bones we'll drive procedurally for clap. Names
            // match the Adventurer / Casual KayKit rigs (Shoulder.L,
            // UpperArm.L, LowerArm.L, mirrored on the right).
            FindArmBones(inst.transform);

            // Floating "Professor" tag, sits above the figure's head.
            Tigerverse.UI.BillboardLabel.Create(transform, "Professor", yOffset: 2.15f);
        }

        private void FindArmBones(Transform root)
        {
            _upperArmL = FindByName(root, "UpperArm.L");
            _upperArmR = FindByName(root, "UpperArm.R");
            _lowerArmL = FindByName(root, "LowerArm.L");
            _lowerArmR = FindByName(root, "LowerArm.R");
            if (_upperArmL != null) _restUpperL = _upperArmL.localRotation;
            if (_upperArmR != null) _restUpperR = _upperArmR.localRotation;
            if (_lowerArmL != null) _restLowerL = _lowerArmL.localRotation;
            if (_lowerArmR != null) _restLowerR = _lowerArmR.localRotation;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private void Update()
        {
            _phase += Time.deltaTime;

            if (_spawning || _leaving) return;
            if (_model == null) return;

            // Update target yaw to face the player. We compute a *world* yaw
            // pointing from us to the camera, then convert to the parent's
            // local space so SmoothDamp drives a stable local-space angle.
            // SmoothDampAngle gives the natural ease-in / ease-out feel —
            // accelerates from rest, decelerates as it nears the target —
            // so the body never snaps. A small deadzone (lookAtDeadzoneDeg)
            // suppresses twitchy micro-corrections from the player's tiny
            // head movements.
            if (lookAtPlayer)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    Vector3 toCam = _cam.transform.position - transform.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 1e-4f)
                    {
                        float worldYaw  = Quaternion.LookRotation(toCam, Vector3.up).eulerAngles.y;
                        float parentYaw = transform.parent != null ? transform.parent.eulerAngles.y : 0f;
                        float newTarget = Mathf.DeltaAngle(parentYaw, worldYaw);

                        if (!_yawInitialised) { _targetYaw = newTarget; _currentYaw = newTarget; _yawInitialised = true; }
                        else if (Mathf.Abs(Mathf.DeltaAngle(_targetYaw, newTarget)) > lookAtDeadzoneDeg)
                            _targetYaw = newTarget;
                    }
                }
                _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVel, lookAtSmoothTime);
            }
            else
            {
                _currentYaw = _baseLocalYaw;
            }

            // The Adventurer FBX has its own Idle animation playing via
            // the Animator, so we no longer layer procedural bob / sway —
            // doing both fights the bone animation and looks twitchy. The
            // only thing this Update still drives is the look-at-player
            // yaw above.
            transform.localRotation = Quaternion.Euler(0, _currentYaw, 0);
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
        /// Praise the player for doing something good — three procedural
        /// claps with matching audio. Distinct from SpeakingPulse so we
        /// don't applaud every spoken line, only on success moments.
        /// </summary>
        public void Celebrate()
        {
            _clapStartT = Time.time;
            ClapSfx.Play(transform.position + Vector3.up * 1.4f);
        }

        /// <summary>
        /// Fire the Adventurer's Cast (Sword_Slash) animation when the
        /// Professor demos a spell. No-op if the controller lacks the
        /// trigger (so older controllers still compile).
        /// </summary>
        public void Cast() => TryFireTrigger(CastHash);

        /// <summary>
        /// Play the HitRecieve animation, e.g. when the player's spell
        /// "lands" on the Professor.
        /// </summary>
        public void PlayHit() => TryFireTrigger(HitHash);

        private void TryFireTrigger(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
                if (p.nameHash == hash) { _animator.SetTrigger(hash); return; }
        }

        // Override the cached arm bones AFTER the Animator has run for the
        // frame so our pose wins. Three clap apex points at t=0.05, 0.26,
        // 0.48 line up with the audio's three claps.
        private void LateUpdate()
        {
            if (_clapStartT < 0f) return;
            float t = Time.time - _clapStartT;
            if (t > ClapTotalDur)
            {
                // Settle back to rest (no further override needed).
                _clapStartT = -10f;
                return;
            }

            // Three triangular envelopes (apex = clap impact, hands together)
            // peaking at the audio clap times. Each clap is ~120ms wide.
            float[] apex   = { 0.05f, 0.26f, 0.48f };
            float clapWidth = 0.10f;
            float k = 0f;
            for (int i = 0; i < apex.Length; i++)
            {
                float d = Mathf.Abs(t - apex[i]) / clapWidth;
                if (d < 1f) k = Mathf.Max(k, 1f - d);
            }
            // Smoothstep so impact feels snappy.
            k = k * k * (3f - 2f * k);

            // Apex pose: arms forward and inward so hands meet near the
            // chest. Empirically tuned for the Adventurer rig (bone forward
            // axis is along its own +Y, so we rotate around X for forward
            // raise and around Y for inward swing).
            ApplyArm(_upperArmL, _restUpperL, new Vector3(-75f, -45f,  35f), k);
            ApplyArm(_upperArmR, _restUpperR, new Vector3(-75f,  45f, -35f), k);
            ApplyArm(_lowerArmL, _restLowerL, new Vector3(  0f, -25f,  40f), k);
            ApplyArm(_lowerArmR, _restLowerR, new Vector3(  0f,  25f, -40f), k);
        }

        private static void ApplyArm(Transform bone, Quaternion rest, Vector3 apexEuler, float k)
        {
            if (bone == null) return;
            var apex = rest * Quaternion.Euler(apexEuler);
            bone.localRotation = Quaternion.Slerp(rest, apex, k);
        }

        /// <summary>
        /// Poof-in spawn. Scales up from 0 with a tiny elastic overshoot,
        /// rises from y=-0.3 to y=0, and emits a small white confetti puff.
        /// </summary>
        public IEnumerator PlaySpawnAnimation()
        {
            _spawning = true;

            Vector3 endPos = transform.localPosition;
            Vector3 startPos = endPos + new Vector3(0f, -0.3f, 0f);
            transform.localPosition = startPos;
            transform.localScale    = Vector3.zero;

            SpawnConfettiPuff(transform.position, new Color(1f, 1f, 1f));

            const float dur = 1.2f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);

                float scale;
                if (k < 0.7f)
                {
                    float a = k / 0.7f;
                    float e = 1f - Mathf.Pow(1f - a, 3f);
                    scale = e * 1.1f;
                }
                else
                {
                    float a = (k - 0.7f) / 0.3f;
                    scale = Mathf.Lerp(1.1f, 1.0f, a);
                }
                transform.localScale = new Vector3(scale, scale, scale);

                float riseK = 1f - Mathf.Pow(1f - k, 2f);
                transform.localPosition = Vector3.Lerp(startPos, endPos, riseK);

                yield return null;
            }

            transform.localScale    = Vector3.one;
            transform.localPosition = endPos;

            _spawning = false;

            // Greeting wave once we land — fires the animator's "Speak"
            // (mapped to Wave / Interact in each character's controller).
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == SpeakHash) { _animator.SetTrigger(SpeakHash); break; }
                }
            }
        }

        /// <summary>
        /// Wave-and-vanish leave. Triggers the "Speak" / wave anim if the
        /// animator has it, then scales down ~30%, drifts upward and fades
        /// alpha to zero (with a confetti puff).
        /// </summary>
        public IEnumerator PlayLeaveAnimation()
        {
            _leaving = true;

            // 1. Friendly wave via animator if available.
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == SpeakHash) { _animator.SetTrigger(SpeakHash); break; }
                }
            }
            const float waveDur = 0.7f;
            float t = 0f;
            while (t < waveDur)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // 2. Cache per-renderer cloned materials so we can fade alpha.
            var rends = GetComponentsInChildren<Renderer>(true);
            var mats  = new Material[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                mats[i] = rends[i].material;
                TryMakeTransparent(mats[i]);
            }

            SpawnConfettiPuff(transform.position + Vector3.up * 0.6f, new Color(1f, 1f, 1f));

            Vector3 startScale = transform.localScale;
            Vector3 endScale   = startScale * 0.7f;
            Vector3 startPos   = transform.localPosition;
            Vector3 endPos     = startPos + new Vector3(0f, 0.25f, 0f);

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

                yield return null;
            }

            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null) rends[i].enabled = false;
            }
        }

        private static void TryMakeTransparent(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", 0f);
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

        private void SpawnConfettiPuff(Vector3 worldPos, Color tint)
        {
            var go = new GameObject("PaperConfettiFx");
            go.transform.position = worldPos;

            var ps  = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            psr.sharedMaterial = mat;
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
