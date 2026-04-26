using System;
using System.Collections;
using Tigerverse.Drawing;
using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Procedural paper-craft egg that hovers above a player's pedestal,
    /// wobbles + cracks while their monster loads, then explodes open with
    /// the monster popping out. Mesh and material are built at runtime so
    /// no prefab/asset wiring is required — just AddComponent and call
    /// Configure(drawingTex, optional).
    /// </summary>
    [DisallowMultipleComponent]
    public class HatchingEggSequence : MonoBehaviour
    {
        // ─── Shape ───────────────────────────────────────────────────────
        [Header("Shape")]
        [SerializeField] private float eggRadius = 0.18f;
        [SerializeField] private float eggHalfHeightTop    = 0.30f;
        [SerializeField] private float eggHalfHeightBottom = 0.24f;
        [SerializeField] private int   latSegments         = 32;
        [SerializeField] private int   lonSegments         = 40;

        // ─── Idle motion ─────────────────────────────────────────────────
        [Header("Idle motion")]
        [SerializeField] private float spinSpeedDeg     = 16f;
        [SerializeField] private float wobbleHz         = 1.6f;
        [SerializeField] private float wobbleAmpDegMin  = 3f;
        [SerializeField] private float wobbleAmpDegMax  = 22f;
        [SerializeField] private float bobAmplitude     = 0.012f;

        // ─── Hatch ───────────────────────────────────────────────────────
        [Header("Hatch")]
        [SerializeField] private float chargeShakeSec   = 0.30f;
        [SerializeField] private float chargeShakeAmp   = 0.022f;
        [SerializeField] private float burstUpwardSpeed = 1.8f;
        [SerializeField] private float burstSidewaysSpeed = 1.0f;
        [SerializeField] private float burstSpinSpeed   = 720f;
        [SerializeField] private float fragmentLifetime = 1.4f;
        [Tooltip("How long after the shells start flying apart before the monster pops up. Higher = shells visibly clear the spawn before the reveal.")]
        [SerializeField] private float burstToRevealDelay = 0.45f;
        [SerializeField] private float monsterPopSec    = 0.55f;

        // ─── Optional inspector hooks (used if assigned, otherwise built procedurally) ─────────
        [Header("Optional hooks (auto-built if null)")]
        [SerializeField] private Renderer       eggRenderer;
        [SerializeField] private ParticleSystem hatchBurst;
        [SerializeField] private AudioSource    sfx;
        [SerializeField] private AudioClip      hatchSfx;

        [Tooltip("If true, automatically attach EggPokeInteraction so VR controllers can poke the egg while it's loading.")]
        [SerializeField] private bool autoEnablePoke = true;

        [Range(0f, 1f)] public float progress01;

        public bool IsHatched => _hatched;

        private GameObject  _eggGo;
        private MeshFilter  _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material    _eggMaterial;
        private Vector3     _baseLocalPos;
        private float       _phase;
        private bool        _hatched;

        private ParticleSystem _shellChunks;   // mesh-based 3D paper shrapnel
        private ParticleSystem _confetti;      // small colorful paper bits
        private ParticleSystem _sparkles;      // textured 4-point stars
        private ParticleSystem _dustCloud;     // soft white smoke puff
        private ParticleSystem _flash;         // bright instant flash
        private ParticleSystem _ring;          // expanding ground ring
        private ParticleSystem _popInDust;     // monster reveal — soft white poof
        private ParticleSystem _popInSparkles; // monster reveal — gold twinkle
        private static Texture2D _sparkleTex;
        private static Texture2D _dustTex;
        private static Mesh _shellChunkMesh;

        // Poke pulse: temporary extra wobble amplitude that decays over ~0.4s.
        private float _pokePulseDeg;
        private float _pokePulseDecay = 4.0f;

        // Floating UI (name tag + progress bar)
        private GameObject  _uiRoot;
        private TextMeshPro _nameLabel;
        private GameObject  _progressBg;
        private GameObject  _progressFill;
        private float       _displayProgress;

        private static readonly int CrackProp        = Shader.PropertyToID("_CrackAmount");
        private static readonly int DrawingTexProp   = Shader.PropertyToID("_DrawingTex");
        private static readonly int DrawingFaceProp  = Shader.PropertyToID("_DrawingFaceAxis");
        private static readonly int PaperTexProp     = Shader.PropertyToID("_PaperTex");

        /// <summary>
        /// Build / re-build the egg with an optional drawing sticker. Safe to
        /// call repeatedly — destroys any existing internal egg first.
        /// </summary>
        public void Configure(Texture2D drawingTex)
        {
            BuildEgg();
            ApplyDrawing(drawingTex);
        }

        private void Awake()
        {
            if (_eggGo == null) BuildEgg();
            if (autoEnablePoke && GetComponent<EggPokeInteraction>() == null)
                gameObject.AddComponent<EggPokeInteraction>();
        }

        /// <summary>
        /// Add a transient extra wobble pulse (degrees). Decays exponentially.
        /// Used by EggPokeInteraction so each poke makes the egg shake more.
        /// </summary>
        public void AddWobblePulse(float pulseDeg)
        {
            _pokePulseDeg = Mathf.Max(_pokePulseDeg, pulseDeg);
        }

        private void BuildEgg()
        {
            // If the inspector wired an explicit egg renderer, use it as-is.
            if (eggRenderer != null && _eggGo == null)
            {
                _eggGo = eggRenderer.gameObject;
                _meshRenderer = eggRenderer as MeshRenderer;
                _meshFilter   = _eggGo.GetComponent<MeshFilter>();
                _eggMaterial  = eggRenderer.material;
                _baseLocalPos = _eggGo.transform.localPosition;
                BuildHatchParticles();
                return;
            }

            if (_eggGo != null) return;

            _eggGo = new GameObject("EggBody");
            _eggGo.transform.SetParent(transform, worldPositionStays: false);
            _eggGo.transform.localPosition = Vector3.zero;
            _eggGo.transform.localRotation = Quaternion.identity;
            _eggGo.transform.localScale    = Vector3.one;
            _baseLocalPos = _eggGo.transform.localPosition;

            _meshFilter   = _eggGo.AddComponent<MeshFilter>();
            _meshRenderer = _eggGo.AddComponent<MeshRenderer>();

            var cfg = EggMeshGenerator.Config.Default;
            cfg.radiusEquator    = eggRadius;
            cfg.halfHeightTop    = eggHalfHeightTop;
            cfg.halfHeightBottom = eggHalfHeightBottom;
            cfg.latSegments      = latSegments;
            cfg.lonSegments      = lonSegments;
            _meshFilter.sharedMesh = EggMeshGenerator.Generate(cfg, "TigerverseEgg");

            var sh = Shader.Find("Tigerverse/Egg");
            if (sh == null)
            {
                Debug.LogWarning("[HatchingEggSequence] Shader 'Tigerverse/Egg' missing — falling back to URP/Lit (no cracks).");
                sh = Shader.Find("Universal Render Pipeline/Lit");
            }
            _eggMaterial = new Material(sh);
            _meshRenderer.sharedMaterial = _eggMaterial;

            // Try to share Paper003 with the monster shader for visual consistency.
            var paper = LoadPaperTex("Color");
            if (paper != null && _eggMaterial.HasProperty(PaperTexProp))
                _eggMaterial.SetTexture(PaperTexProp, paper);

            // Default the drawing-face axis to point along world +Z (forward toward the player).
            if (_eggMaterial.HasProperty(DrawingFaceProp))
                _eggMaterial.SetVector(DrawingFaceProp, new Vector4(0, 0, 1, 0));

            BuildHatchParticles();
            BuildFloatingUI();
        }

        // ─── Floating UI (name + progress bar) ──────────────────────────
        private void BuildFloatingUI()
        {
            if (_uiRoot != null) return;

            _uiRoot = new GameObject("EggUI");
            _uiRoot.transform.SetParent(transform, worldPositionStays: false);
            _uiRoot.transform.localPosition = new Vector3(0f, eggHalfHeightTop + 0.16f, 0f);
            _uiRoot.transform.localRotation = Quaternion.identity;

            // Name label.
            var labelGo = new GameObject("NameLabel");
            labelGo.transform.SetParent(_uiRoot.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            _nameLabel = labelGo.AddComponent<TextMeshPro>();
            _nameLabel.text = "";
            _nameLabel.fontSize = 0.45f;
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.color = new Color(0.07f, 0.06f, 0.10f, 1f);
            _nameLabel.enableWordWrapping = false;
            // Make sure the label has a sane size for raymarching/rendering.
            var rt = _nameLabel.rectTransform;
            rt.sizeDelta = new Vector2(0.8f, 0.18f);
            // Slight outline for legibility against any background.
            _nameLabel.outlineColor = new Color32(255, 255, 255, 220);
            _nameLabel.outlineWidth = 0.18f;

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Unlit/Color");

            // Progress bar background — dark inset.
            _progressBg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _progressBg.name = "ProgressBg";
            DestroyIfExists(_progressBg.GetComponent<Collider>());
            _progressBg.transform.SetParent(_uiRoot.transform, false);
            _progressBg.transform.localPosition = Vector3.zero;
            _progressBg.transform.localScale = new Vector3(0.34f, 0.046f, 1f);
            var bgMat = new Material(unlit);
            if (bgMat.HasProperty("_BaseColor")) bgMat.SetColor("_BaseColor", new Color(0.12f, 0.10f, 0.08f, 1f));
            else bgMat.color = new Color(0.12f, 0.10f, 0.08f, 1f);
            _progressBg.GetComponent<Renderer>().sharedMaterial = bgMat;

            // Progress fill — bright warm bar that grows from left to right.
            _progressFill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _progressFill.name = "ProgressFill";
            DestroyIfExists(_progressFill.GetComponent<Collider>());
            _progressFill.transform.SetParent(_progressBg.transform, false);
            // Place very slightly in front of the bg so it z-fights cleanly toward the camera.
            _progressFill.transform.localPosition = new Vector3(-0.5f, 0f, -0.001f);
            _progressFill.transform.localScale    = new Vector3(0f, 0.86f, 1f);
            var fillMat = new Material(unlit);
            if (fillMat.HasProperty("_BaseColor")) fillMat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.28f, 1f));
            else fillMat.color = new Color(1f, 0.78f, 0.28f, 1f);
            _progressFill.GetComponent<Renderer>().sharedMaterial = fillMat;
        }

        private static void DestroyIfExists(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        public void SetName(string playerName)
        {
            if (_nameLabel == null) BuildFloatingUI();
            if (_nameLabel != null) _nameLabel.text = playerName ?? "";
        }

        public void SetDisplayProgress(float t)
        {
            _displayProgress = Mathf.Clamp01(t);
            if (_progressFill == null) BuildFloatingUI();
            if (_progressFill != null)
            {
                // Quad mesh is 1×1 centred at origin → x goes -0.5..0.5 in
                // its parent's local space. Anchor fill to the LEFT edge of
                // the bg by setting position based on width.
                float w = _displayProgress;
                _progressFill.transform.localScale    = new Vector3(w, 0.86f, 1f);
                _progressFill.transform.localPosition = new Vector3(-0.5f + w * 0.5f, 0f, _progressFill.transform.localPosition.z);
            }
        }

        public void HideFloatingUI()
        {
            if (_uiRoot != null) _uiRoot.SetActive(false);
        }

        /// <summary>
        /// Spawn pop-in animation: scale from 0 → 1 with elastic overshoot.
        /// Call right after instantiating the egg.
        /// </summary>
        public IEnumerator PlayPopInAnimation(float duration = 0.65f)
        {
            Vector3 finalScale = transform.localScale;
            if (finalScale == Vector3.zero) finalScale = Vector3.one;
            transform.localScale = Vector3.zero;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                transform.localScale = finalScale * ElasticOutNorm(k);
                yield return null;
            }
            transform.localScale = finalScale;
        }

        private void LateUpdate()
        {
            if (_uiRoot == null || !_uiRoot.activeInHierarchy) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Billboard the UI so it always faces the player camera. Lock pitch.
            Vector3 toCam = cam.transform.position - _uiRoot.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-4f)
                _uiRoot.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        // ─── Particle build ──────────────────────────────────────────────
        // Two scenes of particles:
        //   Hatch:    shell chunks (mesh) + confetti + sparkles + dust cloud + flash + ground ring
        //   Pop-in:   bright dust poof + gold twinkles when monster materialises
        private void BuildHatchParticles()
        {
            if (_shellChunks != null) return;

            EnsureSparkleTexture();
            EnsureDustTexture();
            EnsureShellChunkMesh();

            _shellChunks   = MakeParticleChild("Hatch_ShellChunks",   alpha: true,  textured: null);
            _confetti      = MakeParticleChild("Hatch_Confetti",      alpha: true,  textured: null);
            _sparkles      = MakeParticleChild("Hatch_Sparkles",      alpha: false, textured: _sparkleTex); // additive
            _dustCloud     = MakeParticleChild("Hatch_DustCloud",     alpha: true,  textured: _dustTex);
            _flash         = MakeParticleChild("Hatch_Flash",         alpha: false, textured: _dustTex);
            _ring          = MakeParticleChild("Hatch_ShockRing",     alpha: false, textured: _dustTex);
            _popInDust     = MakeParticleChild("PopIn_Dust",          alpha: true,  textured: _dustTex);
            _popInSparkles = MakeParticleChild("PopIn_Sparkles",      alpha: false, textured: _sparkleTex);

            ConfigureShellChunks(_shellChunks);
            ConfigureConfetti(_confetti);
            ConfigureSparkles(_sparkles, count: 50, lifeMin: 0.6f, lifeMax: 1.1f, sizeMin: 0.020f, sizeMax: 0.055f, speedMin: 0.6f, speedMax: 1.8f);
            ConfigureDustCloud(_dustCloud);
            ConfigureFlash(_flash);
            ConfigureShockRing(_ring);
            ConfigurePopInDust(_popInDust);
            ConfigureSparkles(_popInSparkles, count: 35, lifeMin: 0.7f, lifeMax: 1.2f, sizeMin: 0.025f, sizeMax: 0.060f, speedMin: 0.4f, speedMax: 1.2f);
        }

        // ─── Texture/mesh generators (cached statically) ────────────────
        // Soft round-glow texture: bright core + smooth radial falloff. No
        // rays — the previous 4-point version looked like throwing stars at
        // small particle sizes.
        private static void EnsureSparkleTexture()
        {
            if (_sparkleTex != null) return;
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "SparkleGlow" };
            var px = new Color[S * S];
            float cx = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - cx) / cx;
                    float dy = (y - cx) / cx;
                    float r  = Mathf.Sqrt(dx * dx + dy * dy);
                    // Soft halo with smooth ease-out falloff.
                    float halo = Mathf.Clamp01(1f - r);
                    halo = halo * halo * halo;        // cubic falloff for softer edge
                    // Tight bright core so it still pops.
                    float core = Mathf.Pow(Mathf.Clamp01(1f - r * 3f), 4f);
                    float a    = Mathf.Clamp01(halo * 0.7f + core * 1.0f);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px); t.Apply(false, false);
            t.wrapMode = TextureWrapMode.Clamp;
            _sparkleTex = t;
        }

        // Soft dust circle: solid centre fading to alpha 0 at the edge.
        private static void EnsureDustTexture()
        {
            if (_dustTex != null) return;
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "DustCircle" };
            var px = new Color[S * S];
            float cx = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - cx) / cx;
                    float dy = (y - cx) / cx;
                    float r  = Mathf.Sqrt(dx * dx + dy * dy);
                    float a  = Mathf.Clamp01(1f - r);
                    a = a * a; // smoother falloff
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px); t.Apply(false, false);
            t.wrapMode = TextureWrapMode.Clamp;
            _dustTex = t;
        }

        // Tiny crumpled paper triangle mesh — 3 verts with random offsets so
        // it doesn't read as a flat billboard. Used as ParticleSystem mesh.
        private static void EnsureShellChunkMesh()
        {
            if (_shellChunkMesh != null) return;
            var m = new Mesh { name = "ShellChunkTri" };
            m.vertices = new[]
            {
                new Vector3( 0.00f,  0.030f, 0f),
                new Vector3(-0.025f, -0.020f, 0.004f),
                new Vector3( 0.027f, -0.018f, -0.003f),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 1 }; // double-sided
            m.uv = new[] { new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            m.RecalculateNormals();
            m.RecalculateBounds();
            _shellChunkMesh = m;
        }

        // ─── Helpers ────────────────────────────────────────────────────
        private ParticleSystem MakeParticleChild(string name, bool alpha, Texture2D textured)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var ps  = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();

            var mat = MakeParticleMaterial(alpha, textured);
            psr.sharedMaterial = mat;
            psr.renderMode = ParticleSystemRenderMode.Billboard;

            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private static Material MakeParticleMaterial(bool alphaBlend, Texture2D mainTex)
        {
            // Try URP particle shader first, then built-in fallback.
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);

            if (mainTex != null)
            {
                if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", mainTex);
                if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", mainTex);
            }

            // Force transparent surface + alpha or additive blending.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // 1 = Transparent
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (alphaBlend)
            {
                if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0); // 0 = alpha
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            else
            {
                if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 1); // 1 = additive
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            return mat;
        }

        // ─── Hatch system configs ───────────────────────────────────────
        private void ConfigureShellChunks(ParticleSystem ps)
        {
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = _shellChunkMesh;
            // Shell chunk material: paper texture if available so they look like real shell
            var paper = LoadPaperTex("Color");
            if (paper != null)
            {
                var pmat = psr.sharedMaterial;
                if (pmat != null && pmat.HasProperty("_BaseMap")) pmat.SetTexture("_BaseMap", paper);
                if (pmat != null && pmat.HasProperty("_MainTex")) pmat.SetTexture("_MainTex", paper);
            }

            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(2.2f, 4.5f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.97f, 0.95f, 0.91f, 1f),
                new Color(0.85f, 0.80f, 0.72f, 1f));
            main.gravityModifier = 1.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 90;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) }); // halved for VR

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.10f;

            var velOL = ps.velocityOverLifetime;
            velOL.enabled = true;
            velOL.space = ParticleSystemSimulationSpace.Local;
            velOL.radial = new ParticleSystem.MinMaxCurve(2.0f, 4.0f);

            var rot3D = ps.rotationOverLifetime;
            rot3D.enabled = true;
            rot3D.separateAxes = true;
            rot3D.x = new ParticleSystem.MinMaxCurve(-15f, 15f);
            rot3D.y = new ParticleSystem.MinMaxCurve(-15f, 15f);
            rot3D.z = new ParticleSystem.MinMaxCurve(-15f, 15f);
            main.maxParticles = 35;

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);
        }

        private void ConfigureConfetti(ParticleSystem ps)
        {
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.lengthScale = 1.5f;
            psr.velocityScale = 0.0f;

            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.012f, 0.030f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Bright confetti palette — random gradient of warm + cool brights.
            main.startColor = new ParticleSystem.MinMaxGradient(BuildConfettiGradient());
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) }); // halved for VR

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var velOL = ps.velocityOverLifetime;
            velOL.enabled = true;
            velOL.space = ParticleSystemSimulationSpace.World;
            // All three axes must use the same MinMaxCurveMode or Unity
            // spams "Particle Velocity curves must all be in the same mode"
            // every frame, which kills performance.
            velOL.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            velOL.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            velOL.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            var rotOL = ps.rotationOverLifetime;
            rotOL.enabled = true;
            rotOL.z = new ParticleSystem.MinMaxCurve(-12f, 12f);

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(fade);
        }

        private static Gradient BuildConfettiGradient()
        {
            // Paper / eggshell palette so confetti reads as torn shell bits,
            // not party streamers. Range: pure paper white → cream → beige →
            // light tan, with subtle warm shadow tones for depth.
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.99f, 0.97f, 0.93f), 0.00f),  // paper white
                    new GradientColorKey(new Color(0.96f, 0.93f, 0.86f), 0.20f),  // light cream
                    new GradientColorKey(new Color(0.92f, 0.87f, 0.77f), 0.40f),  // cream
                    new GradientColorKey(new Color(0.88f, 0.82f, 0.71f), 0.60f),  // beige
                    new GradientColorKey(new Color(0.82f, 0.75f, 0.63f), 0.80f),  // tan
                    new GradientColorKey(new Color(0.76f, 0.68f, 0.55f), 1.00f),  // shadow tan
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private void ConfigureSparkles(ParticleSystem ps, int count, float lifeMin, float lifeMax, float sizeMin, float sizeMax, float speedMin, float speedMax)
        {
            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize     = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.95f, 0.55f, 1f),
                new Color(1.0f, 1.0f, 1.0f, 1f));
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = count + 10;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.10f;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            var twinkle = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.15f, 1.2f),
                new Keyframe(0.55f, 0.8f),
                new Keyframe(1f, 0f));
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, twinkle);

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1, 0.95f, 0.6f), 0f),
                        new GradientColorKey(new Color(1, 1, 1), 0.4f),
                        new GradientColorKey(new Color(1, 0.85f, 0.55f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                        new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);
        }

        private void ConfigureDustCloud(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.00f, 0.97f, 0.92f, 0.70f),
                new Color(0.95f, 0.92f, 0.88f, 0.55f));
            main.gravityModifier = -0.05f; // drifts up slightly
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.8f));

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);

            var rotOL = ps.rotationOverLifetime;
            rotOL.enabled = true;
            rotOL.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        }

        private void ConfigureFlash(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.15f;
            main.loop = false;
            main.startLifetime = 0.18f;
            main.startSpeed    = 0f;
            main.startSize     = 0.55f;
            main.startColor    = new Color(1f, 0.97f, 0.85f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 4;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 2.5f));

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1, 1, 0.9f), 0f),
                        new GradientColorKey(new Color(1, 1, 1), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);
        }

        private void ConfigureShockRing(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = 0.07f;
            main.startColor = new Color(1f, 0.95f, 0.85f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 4;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 18f));

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1, 0.95f, 0.85f), 0f),
                        new GradientColorKey(new Color(1, 1, 1), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        private void ConfigurePopInDust(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.98f, 0.92f, 0.85f),
                new Color(0.96f, 0.93f, 0.88f, 0.65f));
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.10f;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.5f, 1, 1.6f));

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.65f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);
        }

        private void ApplyDrawing(Texture2D drawingTex)
        {
            if (_eggMaterial == null) return;
            if (drawingTex != null && _eggMaterial.HasProperty(DrawingTexProp))
                _eggMaterial.SetTexture(DrawingTexProp, drawingTex);

            // Aim the drawing toward whichever camera is present so judges see it.
            var cam = Camera.main;
            if (cam != null && _eggMaterial.HasProperty(DrawingFaceProp))
            {
                Vector3 toCam = cam.transform.position - transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-4f)
                {
                    Vector3 axis = toCam.normalized;
                    _eggMaterial.SetVector(DrawingFaceProp, new Vector4(axis.x, axis.y, axis.z, 0));
                }
            }
        }

        private void Update()
        {
            if (_hatched || _eggGo == null) return;

            _phase += Time.deltaTime;

            // Drive cracks from progress.
            if (_eggMaterial != null)
                _eggMaterial.SetFloat(CrackProp, Mathf.Clamp01(progress01));

            // Wobble: tilt around X+Z, magnitude grows with progress; constant slow Y spin.
            // Plus a transient poke pulse that decays exponentially.
            _pokePulseDeg = Mathf.Max(0f, _pokePulseDeg - _pokePulseDecay * Time.deltaTime * (_pokePulseDeg + 0.5f));
            float ampDeg = Mathf.Lerp(wobbleAmpDegMin, wobbleAmpDegMax, Mathf.Clamp01(progress01)) + _pokePulseDeg;
            float angX = Mathf.Sin(_phase * wobbleHz * Mathf.PI * 2f) * ampDeg;
            float angZ = Mathf.Cos(_phase * wobbleHz * 1.7f * Mathf.PI * 2f) * ampDeg * 0.6f;
            float yaw  = _phase * spinSpeedDeg;
            _eggGo.transform.localRotation = Quaternion.Euler(angX, yaw, angZ);

            // Tiny vertical bob.
            Vector3 p = _baseLocalPos;
            p.y += Mathf.Sin(_phase * 1.1f * Mathf.PI * 2f) * bobAmplitude;
            _eggGo.transform.localPosition = p;
        }

        public IEnumerator BeginHatchSequence(GameObject monster, Vector3 spawnOrigin, Action onComplete)
        {
            if (_hatched) { onComplete?.Invoke(); yield break; }
            _hatched = true;

            // Hide the floating UI as the hatch begins — name tag and
            // progress bar are wait-time furniture, not part of the reveal.
            HideFloatingUI();

            // Capture monster's intended final scale BEFORE we hide it, so the
            // pop-in animation can scale back to this value (regardless of any
            // re-positioning the egg sequence does).
            Renderer[] monsterRends = null;
            Vector3 monsterFinalScale = Vector3.one;
            if (monster != null)
            {
                monsterRends = monster.GetComponentsInChildren<Renderer>(true);
                monsterFinalScale = monster.transform.localScale;
                if (monsterFinalScale == Vector3.zero) monsterFinalScale = Vector3.one;
                // Hide the monster entirely until the egg has finished bursting.
                foreach (var r in monsterRends) if (r != null) r.enabled = false;
                monster.transform.position = spawnOrigin;
            }

            // Force full crack visually.
            if (_eggMaterial != null) _eggMaterial.SetFloat(CrackProp, 1f);

            // Optional inspector-wired effects.
            if (hatchBurst != null) hatchBurst.Play();
            if (sfx != null && hatchSfx != null) sfx.PlayOneShot(hatchSfx);

            // 1) Charge-up shake.
            float t = 0f;
            while (t < chargeShakeSec)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / chargeShakeSec);
                Vector3 jitter = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(-1f, 1f)
                ) * (chargeShakeAmp * Mathf.Lerp(0.4f, 1.2f, k));
                _eggGo.transform.localPosition = _baseLocalPos + jitter;
                yield return null;
            }
            _eggGo.transform.localPosition = _baseLocalPos;

            // 2) Burst: replace the whole egg with two flying half-shells.
            //    Disabling the MeshRenderer alone hides it; MeshFilter has no
            //    'enabled' (it inherits Component, not Behaviour).
            if (_meshRenderer != null) _meshRenderer.enabled = false;

            // Fire ALL hatch particles at the burst origin.
            if (_shellChunks != null) _shellChunks.Play();
            if (_confetti    != null) _confetti.Play();
            if (_sparkles    != null) _sparkles.Play();
            if (_dustCloud   != null) _dustCloud.Play();
            if (_flash       != null) _flash.Play();
            if (_ring        != null) _ring.Play();

            // Burst plays in parallel; the monster reveal waits long enough
            // for the shells to visibly fly clear of the spawn point before
            // the new actor appears.
            StartCoroutine(BurstFragments());
            yield return new WaitForSeconds(burstToRevealDelay);

            // 3) Reveal monster: scale-in with elastic ease.
            if (monster != null)
            {
                Transform mt = monster.transform;

                // Reposition + fire pop-in particles AT the monster's actual
                // spawn point (which may differ from the egg's hover point).
                if (_popInDust != null)
                {
                    _popInDust.transform.position = spawnOrigin;
                    _popInDust.Play();
                }
                if (_popInSparkles != null)
                {
                    _popInSparkles.transform.position = spawnOrigin;
                    _popInSparkles.Play();
                }

                if (monsterRends != null)
                    foreach (var r in monsterRends) if (r != null) r.enabled = true;
                mt.position = spawnOrigin;
                mt.localScale = Vector3.zero;

                float pt = 0f;
                while (pt < monsterPopSec)
                {
                    pt += Time.deltaTime;
                    float k = Mathf.Clamp01(pt / monsterPopSec);
                    float eased = ElasticOutNorm(k);
                    mt.localScale = monsterFinalScale * eased;
                    yield return null;
                }
                mt.localScale = monsterFinalScale;
            }

            onComplete?.Invoke();

            // Leave the carrier alive long enough for any trailing fragments to fade.
            Destroy(gameObject, fragmentLifetime + 0.5f);
        }

        private IEnumerator BurstFragments()
        {
            // Build matching split halves with the same config.
            var cfg = EggMeshGenerator.Config.Default;
            cfg.radiusEquator    = eggRadius;
            cfg.halfHeightTop    = eggHalfHeightTop;
            cfg.halfHeightBottom = eggHalfHeightBottom;
            cfg.latSegments      = latSegments;
            cfg.lonSegments      = lonSegments;
            var (topMesh, botMesh) = EggMeshGenerator.GenerateSplit(cfg, splitV: 0.46f);

            GameObject topGo = MakeFragment(topMesh, "EggFrag_Top");
            GameObject botGo = MakeFragment(botMesh, "EggFrag_Bottom");

            // Random fly-apart vectors. Top jumps up + outward; bottom hops aside.
            Vector3 topVel = new Vector3(
                UnityEngine.Random.Range(-burstSidewaysSpeed, burstSidewaysSpeed),
                burstUpwardSpeed,
                UnityEngine.Random.Range(-burstSidewaysSpeed, burstSidewaysSpeed)
            );
            Vector3 botVel = new Vector3(
                -topVel.x * 0.5f,
                burstUpwardSpeed * 0.35f,
                -topVel.z * 0.5f
            );
            Vector3 topSpin = UnityEngine.Random.onUnitSphere * burstSpinSpeed;
            Vector3 botSpin = UnityEngine.Random.onUnitSphere * burstSpinSpeed * 0.6f;

            float gravity = 4.0f;
            float t = 0f;
            Material topMat = topGo.GetComponent<MeshRenderer>().material;
            Material botMat = botGo.GetComponent<MeshRenderer>().material;

            while (t < fragmentLifetime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / fragmentLifetime);

                topVel += Vector3.down * gravity * Time.deltaTime;
                botVel += Vector3.down * gravity * Time.deltaTime;

                topGo.transform.localPosition += topVel * Time.deltaTime;
                botGo.transform.localPosition += botVel * Time.deltaTime;
                topGo.transform.localRotation *= Quaternion.Euler(topSpin * Time.deltaTime);
                botGo.transform.localRotation *= Quaternion.Euler(botSpin * Time.deltaTime);

                float alpha = 1f - k;
                if (topMat.HasProperty("_BaseColor"))
                {
                    var c = topMat.GetColor("_BaseColor"); c.a = alpha; topMat.SetColor("_BaseColor", c);
                }
                if (botMat.HasProperty("_BaseColor"))
                {
                    var c = botMat.GetColor("_BaseColor"); c.a = alpha; botMat.SetColor("_BaseColor", c);
                }
                yield return null;
            }

            if (topGo != null) Destroy(topGo);
            if (botGo != null) Destroy(botGo);
        }

        private GameObject MakeFragment(Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();

            // URP/Lit set up for transparent fade.
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.97f, 0.95f, 0.91f, 1f));
            mat.SetFloat("_Surface", 1);     // 1 = Transparent
            mat.SetFloat("_Blend", 0);       // 0 = Alpha
            mat.SetFloat("_ZWrite", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mr.sharedMaterial = mat;

            return go;
        }

        private static float ElasticOutNorm(float t)
        {
            // Standard elastic ease-out [0,1] → [0,1] with overshoot.
            const float p = 0.32f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - p / 4f) * (2f * Mathf.PI) / p) + 1f;
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
