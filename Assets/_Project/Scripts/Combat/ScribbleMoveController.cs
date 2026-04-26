using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Drives the local scribble's horizontal position from the left
    /// thumbstick. Only active during Scribble mode (the manager toggles
    /// <c>enabled</c>). Camera-relative so "stick up" feels like "forward".
    /// </summary>
    public class ScribbleMoveController : MonoBehaviour
    {
        [Tooltip("The scribble (monster) this controller moves. Position changes apply to its transform on the XZ plane only.")]
        public Transform target;

        [Tooltip("Movement speed in meters/second at full stick deflection.")]
        public float speed = 2.5f;

        [Tooltip("Stick magnitude below this is treated as zero to ignore drift.")]
        public float deadzone = 0.15f;

        [Tooltip("XR Origin transform — when set, the camera rig is locked to the scribble's position (first-person POV) while this controller is enabled. Restores to the trainer's last pose when disabled.")]
        public Transform xrOrigin;

        [Header("First-person POV")]
        [Tooltip("Height above the scribble's transform.position to place the camera. Roughly the creature's eye height in meters.")]
        public float scribbleEyeHeight = 0.35f;

        [Tooltip("Yaw turn speed (degrees per second at full right-stick deflection) for editor / non-headset use. Ignored in real XR — head tracking handles look.")]
        public float yawSpeed = 120f;

        private Vector3 _trainerHomePos;
        private Quaternion _trainerHomeRot;
        private bool _hasTrainerHome;

        private void OnEnable()
        {
            if (xrOrigin != null && target != null)
            {
                _trainerHomePos = xrOrigin.position;
                _trainerHomeRot = xrOrigin.rotation;
                _hasTrainerHome = true;
                ApplyFirstPerson();
            }
        }

        private void OnDisable()
        {
            if (xrOrigin != null && _hasTrainerHome)
            {
                xrOrigin.position = _trainerHomePos;
                if (!UnityEngine.XR.XRSettings.isDeviceActive)
                    xrOrigin.rotation = _trainerHomeRot;
            }
        }

        private void LateUpdate()
        {
            ReadYawInput();
            ApplyFirstPerson();
        }

        private void ReadYawInput()
        {
            // Real XR: head tracking handles look — don't fight it.
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            if (xrOrigin == null) return;

            // Right stick X (or left/right arrow keys in editor) yaws the rig.
            var rightDev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            Vector2 axis = Vector2.zero;
            if (rightDev.isValid) rightDev.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);
#if UNITY_EDITOR
            if (Mathf.Abs(axis.x) < deadzone)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    float kx = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                    if (Mathf.Abs(kx) > 0f) axis.x = kx;
                }
            }
#endif
            if (Mathf.Abs(axis.x) < deadzone) return;
            xrOrigin.Rotate(0f, axis.x * yawSpeed * Time.deltaTime, 0f, Space.World);
        }

        private void ApplyFirstPerson()
        {
            if (xrOrigin == null || target == null) return;
            // Camera lives at the scribble's eye position. Rotation belongs
            // to head tracking (XR) or the yaw input above (editor).
            xrOrigin.position = target.position + Vector3.up * scribbleEyeHeight;
        }

        private void Update()
        {
            if (target == null) return;

            var dev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            Vector2 axis = Vector2.zero;
            if (dev.isValid) dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);

            // Editor / non-XR fallback: WASD on the keyboard.
#if UNITY_EDITOR
            if (axis.sqrMagnitude < deadzone * deadzone)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    float kx = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                    float ky = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                    axis = new Vector2(kx, ky);
                }
            }
#endif

            if (axis.sqrMagnitude < deadzone * deadzone) return;

            // Camera-relative XZ movement so stick-forward feels like
            // "into the scene" no matter which way the trainer is facing.
            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            fwd.y = 0f; right.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            fwd.Normalize(); right.Normalize();

            Vector3 delta = (right * axis.x + fwd * axis.y) * speed * Time.deltaTime;
            var p = target.position;
            p.x += delta.x;
            p.z += delta.z;
            target.position = p;
            // Camera follow is handled in LateUpdate via ApplyFollow().
        }
    }
}
