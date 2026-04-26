using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.MR
{
    /// <summary>
    /// Drives a Camera transform from the headset's CenterEye pose every
    /// LateUpdate. Used on the BattleMR Main Camera because the AR rig we
    /// auto-generate has no TrackedPoseDriver — without this, the camera
    /// is stuck at its scene-time transform (world origin) while
    /// passthrough is composited around the player's real head pose,
    /// making the rendered scene look "floating in nowhere" relative to
    /// where the player is actually standing.
    ///
    /// Place on the AR Main Camera. Reads from
    /// CommonUsages.centerEyePosition / centerEyeRotation, which are
    /// reported in the active OpenXR reference space (Floor mode here),
    /// and writes them into the camera's localPosition / localRotation
    /// (i.e., relative to the Camera Offset). This matches what
    /// TrackedPoseDriver does, but without the InputAction setup.
    /// </summary>
    [DisallowMultipleComponent]
    public class MRHeadPoseDriver : MonoBehaviour
    {
        private void LateUpdate()
        {
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!dev.isValid) return;

            if (dev.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 pos))
                transform.localPosition = pos;
            if (dev.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rot))
                transform.localRotation = rot;
        }
    }
}
