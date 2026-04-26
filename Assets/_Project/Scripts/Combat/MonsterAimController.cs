using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Lives on the LOCAL player's monster during the battle phase. Reads the
    /// left thumbstick to maintain a horizontal aim direction, parks a small
    /// reticle on the floor in front of the monster at <see cref="aimRangeMeters"/>,
    /// and on <see cref="LaunchAttack(MoveSO,int)"/> spawns a <see cref="MonsterProjectile"/>
    /// heading toward the reticle. Cooldown-gated so voice spam can't fire
    /// projectiles back-to-back.
    ///
    /// Visible only in <see cref="BattleControlMode.Scribble"/>; hidden in
    /// Artist mode (where the joystick is busy translating the monster
    /// instead via <see cref="ScribbleMoveController"/>).
    /// </summary>
    public class MonsterAimController : MonoBehaviour
    {
        /// <summary>
        /// The aim controller for THIS client's local monster. There's only
        /// ever one — set by the most recently enabled instance. Used by
        /// VoiceCommandRouter to route voice-cast moves through the aim
        /// pipeline without scene-wiring.
        /// </summary>
        public static MonsterAimController LocalInstance { get; private set; }

        [Tooltip("The monster the local player is casting AGAINST. Hit-detection target for spawned projectiles.")]
        public Transform opponent;

        [Tooltip("Left-stick deflection below this magnitude is treated as zero.")]
        public float deadzone = 0.15f;

        [Tooltip("How far in front of the caster the reticle / aim point sits at full deflection.")]
        public float aimRangeMeters = 3.0f;

        [Tooltip("Height above the floor at which the projectile flies (and the reticle hovers).")]
        public float aimHeight = 0.6f;

        [Tooltip("Cooldown in seconds between successive casts. Prevents voice-command spam.")]
        public float cooldownSeconds = 1.5f;

        [Tooltip("Projectile travel speed in m/s.")]
        public float projectileSpeed = 8f;

        [Tooltip("Projectile lifetime in seconds — auto-despawns if it hasn't hit anything.")]
        public float projectileLifetime = 3f;

        [Tooltip("Hit radius around the opponent monster's position. Approximate; tune to monster scale.")]
        public float hitRadius = 0.6f;

        // Defaults to the BattleManager.casterIndex of whichever side this
        // monster belongs to. Set by GameStateManager when wiring up.
        public int casterIndex = 0;

        // Reference to the global BattleManager, to call SubmitMove on hit.
        public BattleManager battle;

        private Transform _reticle;
        private Renderer  _reticleRenderer;
        private Vector2   _stickAxis;
        private float     _nextCastTime;
        private bool      _scribbleMode = true;   // matches BattleControlModeManager default

        private void Awake()
        {
            BuildReticle();
            SetReticleVisible(_scribbleMode);
        }

        private void OnEnable()
        {
            LocalInstance = this;
            if (BattleControlModeManager.Instance != null)
            {
                BattleControlModeManager.Instance.OnModeChanged += HandleModeChanged;
                _scribbleMode = (BattleControlModeManager.Instance.CurrentMode == BattleControlMode.Scribble);
                SetReticleVisible(_scribbleMode);
            }
        }

        private void OnDisable()
        {
            if (LocalInstance == this) LocalInstance = null;
            if (BattleControlModeManager.Instance != null)
                BattleControlModeManager.Instance.OnModeChanged -= HandleModeChanged;
            SetReticleVisible(false);
        }

        private void HandleModeChanged(BattleControlMode mode)
        {
            _scribbleMode = (mode == BattleControlMode.Scribble);
            SetReticleVisible(_scribbleMode);
        }

        private void Update()
        {
            ReadStick();
            UpdateReticlePosition();
        }

        // ─── Stick read ──────────────────────────────────────────────────
        private void ReadStick()
        {
            // Left thumbstick — same source ScribbleMoveController uses.
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            Vector2 axis = Vector2.zero;
            if (dev.isValid) dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);

#if UNITY_EDITOR
            // Editor fallback so laptop dev testing works without an HMD.
            if (axis.sqrMagnitude < deadzone * deadzone)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    float kx = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                    float ky = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                    if (Mathf.Abs(kx) + Mathf.Abs(ky) > 1e-4f) axis = new Vector2(kx, ky);
                }
            }
#endif
            _stickAxis = axis;
        }

        // ─── Reticle ─────────────────────────────────────────────────────
        private void BuildReticle()
        {
            // Tiny disc made of a flattened cylinder primitive. Self-contained,
            // no asset references, identifiable by name in the scene.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "AimReticle";
            // Drop the collider — purely visual.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localScale = new Vector3(0.3f, 0.01f, 0.3f);

            _reticle = go.transform;
            _reticleRenderer = go.GetComponent<Renderer>();
            if (_reticleRenderer != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    mat.color = new Color(1f, 0.85f, 0.2f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    _reticleRenderer.sharedMaterial = mat;
                }
            }
        }

        private void UpdateReticlePosition()
        {
            if (_reticle == null) return;

            Vector3 aimPos = ComputeAimPosition();
            // Reticle is unparented from monster's rotation/scale by setting
            // world position directly each frame (still parented for cleanup).
            _reticle.position = aimPos;
            _reticle.localScale = new Vector3(0.3f, 0.01f, 0.3f) / Mathf.Max(0.0001f, transform.lossyScale.x);
        }

        // World-space aim point. Forward (relative to monster's facing) plus
        // sideways drift from the joystick X axis. Always at aimHeight above
        // the floor.
        private Vector3 ComputeAimPosition()
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // Y-stick = forward/back range, X-stick = left/right offset.
            // Stick fully up = aim straight ahead at full range.
            // Stick neutral = aim straight ahead at 60% range.
            // Clamp so the reticle never goes behind the monster.
            float stickX = Mathf.Abs(_stickAxis.x) > deadzone ? _stickAxis.x : 0f;
            float stickY = Mathf.Abs(_stickAxis.y) > deadzone ? _stickAxis.y : 0f;

            float forwardK = Mathf.Clamp01(0.6f + stickY * 0.4f);   // [0.2..1.0]
            float sideK    = stickX;                                 // [-1..1]

            Vector3 offset = fwd * (aimRangeMeters * forwardK) + right * (aimRangeMeters * 0.6f * sideK);
            Vector3 aimPos = transform.position + offset;
            aimPos.y = (transform.position.y) + aimHeight;
            return aimPos;
        }

        public Vector3 AimPosition => ComputeAimPosition();
        public Vector3 AimDirection
        {
            get
            {
                Vector3 d = ComputeAimPosition() - transform.position;
                d.y = 0f;
                return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
            }
        }

        private void SetReticleVisible(bool visible)
        {
            if (_reticleRenderer != null) _reticleRenderer.enabled = visible;
        }

        // ─── Cast ────────────────────────────────────────────────────────
        /// <summary>
        /// Called by VoiceCommandRouter when the player shouts a move name
        /// that matched. Spawns a projectile heading toward the current
        /// reticle position. Returns false (silently) if the cast was
        /// rejected by the cooldown.
        /// </summary>
        public bool LaunchAttack(MoveSO move, int caster)
        {
            if (move == null) return false;
            if (!_scribbleMode) return false;
            if (Time.time < _nextCastTime)
            {
                Debug.Log($"[Aim] '{move.displayName}' rejected — cooldown {(_nextCastTime - Time.time):F1}s remaining.");
                return false;
            }
            if (opponent == null)
            {
                Debug.LogWarning("[Aim] No opponent reference — projectile would have no hit target. Casting anyway as a flying VFX.");
            }

            _nextCastTime = Time.time + cooldownSeconds;

            Vector3 spawnPos = transform.position + Vector3.up * aimHeight;
            Vector3 aimPos = ComputeAimPosition();
            Vector3 dir = (aimPos - spawnPos);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward; else dir.Normalize();

            var projGo = new GameObject($"Projectile_{move.displayName}");
            projGo.transform.position = spawnPos;
            projGo.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            var proj = projGo.AddComponent<MonsterProjectile>();
            proj.Launch(
                move:           move,
                casterIndex:    caster,
                direction:      dir,
                opponentTarget: opponent,
                speed:          projectileSpeed,
                lifetime:       projectileLifetime,
                hitRadius:      hitRadius,
                onHit:          (m, c) => { if (battle != null) battle.SubmitMove(m, c); });

            return true;
        }
    }
}
