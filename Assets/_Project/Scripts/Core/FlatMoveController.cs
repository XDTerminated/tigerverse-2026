using UnityEngine;
using UnityEngine.InputSystem;

namespace Tigerverse.Core
{
    /// <summary>
    /// Editor / non-XR fallback movement: WASD moves the XR Origin horizontally,
    /// QE rotates yaw, mouse-look optional. Disabled automatically when XR is active.
    /// In VR, the rig's ContinuousMoveProvider (re-enabled by the editor menu)
    /// handles joystick locomotion instead.
    /// </summary>
    public class FlatMoveController : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 2f;
        [SerializeField] float turnSpeed = 90f;

        // Disable this when an XR display is active so we don't double-move.
        [SerializeField] bool disableWhenXrActive = true;

        private Transform _origin;

        private void Awake()
        {
            _origin = transform; // attach this component to the XR Origin GO
        }

        private void Update()
        {
            if (disableWhenXrActive && IsXrActive()) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (move.sqrMagnitude > 0.01f)
            {
                // Move in XR Origin's forward space (so movement is relative to the rig's orientation).
                Vector3 worldMove = _origin.TransformDirection(move.normalized) * moveSpeed * Time.deltaTime;
                worldMove.y = 0; // keep on ground
                _origin.position += worldMove;
            }

            float yaw = 0f;
            if (kb.qKey.isPressed) yaw -= 1f;
            if (kb.eKey.isPressed) yaw += 1f;
            if (Mathf.Abs(yaw) > 0.01f)
            {
                _origin.Rotate(Vector3.up, yaw * turnSpeed * Time.deltaTime, Space.World);
            }
        }

        private static bool IsXrActive()
        {
            // True if a real XR display subsystem is running (Quest Link, headset attached).
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                if (d != null && d.running) return true;
            }
            return false;
        }
    }
}
