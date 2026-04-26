using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.UI
{
    /// <summary>
    /// Filler interaction for the model-load wait window: players can reach
    /// out and "poke" the egg with their VR controllers. Each poke triggers
    /// a wobble pulse, a particle puff, and a haptic buzz on the poking
    /// hand. Cosmetic only — the egg still hatches when the GLB load
    /// completes, not when the player has poked it enough times.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HatchingEggSequence))]
    public class EggPokeInteraction : MonoBehaviour
    {
        [Tooltip("Distance from egg centre at which a controller counts as 'poking' it (metres).")]
        [SerializeField] private float pokeRadius = 0.28f;
        [Tooltip("Minimum seconds between two pokes from the same hand.")]
        [SerializeField] private float pokeCooldown = 0.20f;
        [Tooltip("Wobble pulse magnitude in degrees added to the idle wobble for ~0.4s after each poke.")]
        [SerializeField] private float pokeWobblePulseDeg = 12f;
        [Tooltip("Haptic amplitude 0..1.")]
        [SerializeField] private float hapticAmplitude = 0.65f;
        [Tooltip("Haptic duration in seconds.")]
        [SerializeField] private float hapticDuration = 0.10f;
        [Tooltip("Particles emitted per poke.")]
        [SerializeField] private int puffPerPoke = 14;

        private HatchingEggSequence _egg;
        private Transform _leftCtrl, _rightCtrl;
        private bool _leftInside, _rightInside;
        private float _lastLeftPokeT = -10f, _lastRightPokeT = -10f;
        private float _lastFindAt;
        private ParticleSystem _puff;
        private int _pokeCount;

        private void Awake()
        {
            _egg = GetComponent<HatchingEggSequence>();
            BuildPuffParticles();
        }

        private void Start()
        {
            FindControllers();
        }

        private void FindControllers()
        {
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            _leftCtrl  = FindUnderRig(origin.transform, "Left Controller")
                      ?? FindUnderRig(origin.transform, "LeftHand Controller")
                      ?? FindUnderRig(origin.transform, "Left Hand Controller");
            _rightCtrl = FindUnderRig(origin.transform, "Right Controller")
                      ?? FindUnderRig(origin.transform, "RightHand Controller")
                      ?? FindUnderRig(origin.transform, "Right Hand Controller");
        }

        private static Transform FindUnderRig(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private void Update()
        {
            if (_egg == null || _egg.IsHatched) return;
            if ((_leftCtrl == null || _rightCtrl == null) && Time.unscaledTime - _lastFindAt > 0.5f)
            {
                _lastFindAt = Time.unscaledTime;
                FindControllers();
            }

            TryPoke(_leftCtrl,  ref _leftInside,  ref _lastLeftPokeT,  XRNode.LeftHand);
            TryPoke(_rightCtrl, ref _rightInside, ref _lastRightPokeT, XRNode.RightHand);
        }

        private void TryPoke(Transform ctrl, ref bool inside, ref float lastPokeT, XRNode node)
        {
            if (ctrl == null) return;
            float dist = Vector3.Distance(ctrl.position, transform.position);
            bool nowInside = dist < pokeRadius;
            if (nowInside && !inside && Time.time - lastPokeT > pokeCooldown)
            {
                OnPoke(ctrl.position, node);
                lastPokeT = Time.time;
            }
            inside = nowInside;
        }

        private void OnPoke(Vector3 pokeWorld, XRNode node)
        {
            _pokeCount++;
            if (_egg != null) _egg.AddWobblePulse(pokeWobblePulseDeg);
            if (_puff != null)
            {
                _puff.transform.position = pokeWorld;
                _puff.Emit(puffPerPoke);
            }
            SendHaptic(node);
        }

        private void SendHaptic(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;
            if (device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(hapticAmplitude), Mathf.Max(0.01f, hapticDuration));
        }

        private void BuildPuffParticles()
        {
            var go = new GameObject("EggPokePuff");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;

            _puff = go.AddComponent<ParticleSystem>();
            // Stop FIRST so we can safely change main.duration etc.
            _puff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _puff.main;
            main.playOnAwake = false;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(0.97f, 0.95f, 0.91f, 1f),
                new Color(1.00f, 0.92f, 0.78f, 1f));
            main.gravityModifier = 0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            var emission = _puff.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;

            var shape = _puff.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var sizeOL = _puff.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));

            var colOL = _puff.colorOverLifetime;
            colOL.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colOL.color = new ParticleSystem.MinMaxGradient(grad);

            // Material: same approach as the egg's hatch particles.
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var psr = _puff.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = new Material(sh);
            psr.renderMode = ParticleSystemRenderMode.Billboard;

            _puff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
