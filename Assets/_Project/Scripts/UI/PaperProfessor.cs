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

        private static readonly int SpeakHash = Animator.StringToHash("Speak");

        private void Awake()
        {
            BuildBody();
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

            // Floating "Professor" tag, sits above the figure's head.
            Tigerverse.UI.BillboardLabel.Create(transform, "Professor", yOffset: 2.15f);
        }

        private void Update()
        {
            _phase += Time.deltaTime;

            if (_spawning || _leaving) return;
            if (_model == null) return;

            // Idle sway (whole body).
            float sway = Mathf.Sin(_phase * idleSwayHz * Mathf.PI * 2f) * idleSwayDeg;
            transform.localRotation = Quaternion.Euler(0, 0, sway);

            // Idle bob — applied to the model so it stays pinned at the
            // pivot when scaled by the spawn animation.
            Vector3 hp = _baseModelLocalPos;
            hp.y += Mathf.Sin(_phase * idleBobHz * Mathf.PI * 2f) * idleBobAmplitude;
            _model.localPosition = hp;
        }

        /// <summary>
        /// Trigger a speaking-gesture pulse. Call once per spoken line.
        /// </summary>
        public void SpeakingPulse(float duration = -1f)
        {
            _speakT = Time.time;
            if (duration > 0f) speakDuration = duration;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == SpeakHash)
                    {
                        _animator.SetTrigger(SpeakHash);
                        ClapSfx.Play(transform.position + Vector3.up * 1.4f);
                        break;
                    }
                }
            }
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
                    if (p.nameHash == SpeakHash) { _animator.SetTrigger(SpeakHash); ClapSfx.Play(transform.position + Vector3.up * 1.4f); break; }
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
                    if (p.nameHash == SpeakHash) { _animator.SetTrigger(SpeakHash); ClapSfx.Play(transform.position + Vector3.up * 1.4f); break; }
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
