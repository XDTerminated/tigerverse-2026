using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Drives the borrowed scribble's Y-axis rotation each frame so it
    /// "looks at" a target (default: the local camera = player). When an
    /// attack starts, ProfessorTutorial flips the target to the dummy via
    /// SetAttackMode(true), then back to the player when the attack
    /// finishes. Pitch (X) and roll (Z) offsets stay fixed — only yaw is
    /// driven, preserving whatever model-axis correction the scribble
    /// needs.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScribbleFollow : MonoBehaviour
    {
        [Tooltip("SmoothDamp time when looking at the player (idle gaze).")]
        [SerializeField] private float playerYawSmoothTime = 0.45f;
        [Tooltip("SmoothDamp time when looking at the dummy during an attack — much shorter so the scribble snaps onto the target before firing.")]
        [SerializeField] private float attackYawSmoothTime = 0.10f;

        private float _pitchDeg;
        private float _yawOffsetDeg;
        private float _rollDeg;
        private Transform _playerTarget;
        private Transform _attackTarget;
        private bool _attackMode;

        private float _currentYaw;
        private float _yawVel;
        private bool _yawInitialised;

        public void Init(float pitchDeg, float yawOffsetDeg, float rollDeg, Transform playerTarget, Transform attackTarget)
        {
            _pitchDeg     = pitchDeg;
            _yawOffsetDeg = yawOffsetDeg;
            _rollDeg      = rollDeg;
            _playerTarget = playerTarget;
            _attackTarget = attackTarget;
        }

        /// <summary>True = look at the dummy/attack target. False = look at the player.</summary>
        public void SetAttackMode(bool attacking) => _attackMode = attacking;

        public void SetAttackTarget(Transform t) => _attackTarget = t;

        private void LateUpdate()
        {
            Transform target = _attackMode ? _attackTarget : _playerTarget;
            if (target == null && _playerTarget == null)
            {
                var cam = Camera.main;
                if (cam != null) _playerTarget = cam.transform;
                target = _attackMode ? _attackTarget : _playerTarget;
            }
            if (target == null) return;

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f) return;

            float worldYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float desired  = worldYaw + _yawOffsetDeg;

            if (!_yawInitialised) { _currentYaw = desired; _yawInitialised = true; }
            else
            {
                float smooth = _attackMode ? attackYawSmoothTime : playerYawSmoothTime;
                _currentYaw = Mathf.SmoothDampAngle(_currentYaw, desired, ref _yawVel, smooth);
            }

            transform.rotation = Quaternion.Euler(_pitchDeg, _currentYaw, _rollDeg);
        }
    }
}
