using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Keeps a world-space UI panel in front of the local camera with a soft
    /// "lazy" follow. The panel sits at a fixed distance + height in front of
    /// the player; small head turns leave it in place (deadzone), larger ones
    /// smoothly drag the panel back into view. Used by the title / loading
    /// screens so menus stay readable even if the player rotates in their
    /// chair or wanders a step in VR.
    /// </summary>
    [DisallowMultipleComponent]
    public class CanvasFollowPlayer : MonoBehaviour
    {
        [Tooltip("Distance from the camera the panel should sit at (metres).")]
        [SerializeField] private float distance = 2.6f;

        [Tooltip("Vertical offset relative to the camera position (metres). 0 = eye-level, negative = below eye-line.")]
        [SerializeField] private float verticalOffset = -0.10f;

        [Tooltip("How quickly the panel chases its target position. Higher = snappier, lower = floatier.")]
        [SerializeField] private float positionLerpSpeed = 4.0f;

        [Tooltip("How quickly the panel rotates to face the camera.")]
        [SerializeField] private float rotationLerpSpeed = 6.0f;

        [Tooltip("Yaw angle (deg) the panel can drift off the camera's forward before it starts catching up. Keeps tiny head shakes from jiggling the menu.")]
        [SerializeField] private float yawDeadzoneDeg = 18f;

        private Camera _cam;
        private bool _initialised;
        private Vector3 _targetPos;
        private Quaternion _targetRot;

        private void OnEnable()
        {
            _initialised = false;
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Compute desired target every frame, then apply the deadzone /
            // smoothing on top.
            ComputeTarget(out Vector3 desiredPos, out Quaternion desiredRot);

            if (!_initialised)
            {
                transform.SetPositionAndRotation(desiredPos, desiredRot);
                _targetPos = desiredPos;
                _targetRot = desiredRot;
                _initialised = true;
                return;
            }

            // Yaw deadzone — only repick the target position when the camera
            // has rotated enough that the panel is leaving the field of view.
            Vector3 toPanel = _targetPos - _cam.transform.position;
            toPanel.y = 0f;
            Vector3 camFwd = _cam.transform.forward;
            camFwd.y = 0f;
            float yawDelta = Vector3.Angle(toPanel.normalized, camFwd.normalized);
            if (yawDelta > yawDeadzoneDeg)
            {
                _targetPos = desiredPos;
            }
            // Always retarget rotation (we always want to face the player).
            _targetRot = desiredRot;

            // Smooth toward the latched target.
            transform.position = Vector3.Lerp(transform.position, _targetPos, positionLerpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, rotationLerpSpeed * Time.deltaTime);
        }

        private void ComputeTarget(out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = _cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            pos = _cam.transform.position + fwd * distance + Vector3.up * verticalOffset;
            // Unity world-space UI content faces the canvas's local -Z, so for
            // the player to see the front (not a mirrored back) we want the
            // canvas's +Z to align with the camera's forward — i.e. point
            // AWAY from the player. Earlier code used `-fwd` and the menu
            // rendered as a mirror image because we were showing its back.
            rot = Quaternion.LookRotation(fwd, Vector3.up);
        }
    }
}
