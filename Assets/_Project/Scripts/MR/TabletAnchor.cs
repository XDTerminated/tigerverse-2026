using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using System.Collections.Generic;

namespace Tigerverse.MR
{
    /// <summary>
    /// Anchors a Transform to the position of a tracked tablet QR portal.
    /// Falls back to a controller pointer + trigger placement when marker tracking is unavailable.
    /// </summary>
    public class TabletAnchor : MonoBehaviour
    {
        [SerializeField] private Camera xrCamera;
        [SerializeField] private Transform rightControllerTransform;
        [SerializeField] private LineRenderer pointerLine;
        [SerializeField] private string expectedQrPayload; // session code or join URL — set by GameStateManager
        [SerializeField] private bool useMarkerTracking = true;

        public Transform anchorTransform { get; private set; }
        public bool IsAnchorPlaced { get; private set; }
        public UnityEvent OnAnchorPlaced;

        private bool triggerWasPressed;
        private bool warnedNoMetaSdk;

        public string ExpectedQrPayload
        {
            get => expectedQrPayload;
            set => expectedQrPayload = value;
        }

        private void Awake()
        {
            var anchorGo = new GameObject("TabletAnchor");
            anchorGo.transform.SetParent(transform, false);
            anchorTransform = anchorGo.transform;

            if (OnAnchorPlaced == null)
            {
                OnAnchorPlaced = new UnityEvent();
            }
        }

        private void Update()
        {
            if (IsAnchorPlaced)
            {
                return;
            }

            if (useMarkerTracking)
            {
#if META_XR_SDK_DEFINED
                // TODO: Meta MR Utility Kit marker tracking goes here when SDK is imported.
                // Try MRUK.Instance?.GetCurrentRoom()?.GetPassthroughLayer() etc. and OVR marker tracking
                // to detect a printed QR/tablet, then assign anchorTransform.SetPositionAndRotation(...).
                // For now we fall through to controller pointer.
                UpdateControllerPointer();
#else
                if (!warnedNoMetaSdk)
                {
                    Debug.LogWarning("[TabletAnchor] Meta XR SDK not defined — falling back to controller pointer placement. Define META_XR_SDK_DEFINED after importing Meta XR SDK.");
                    warnedNoMetaSdk = true;
                }
                UpdateControllerPointer();
#endif
            }
            else
            {
                UpdateControllerPointer();
            }
        }

        private void UpdateControllerPointer()
        {
            if (rightControllerTransform == null)
            {
                return;
            }

            Vector3 origin = rightControllerTransform.position;
            Vector3 forward = rightControllerTransform.forward;
            Vector3 endPoint = origin + forward * 3f;

            if (pointerLine != null)
            {
                pointerLine.enabled = true;
                pointerLine.positionCount = 2;
                pointerLine.SetPosition(0, origin);
                pointerLine.SetPosition(1, endPoint);
            }

            bool triggerPressed = ReadRightTrigger();

            if (triggerPressed && !triggerWasPressed && !IsAnchorPlaced)
            {
                Vector3 placementPos;
                Vector3 placementUp = Vector3.up;

                if (Physics.Raycast(origin, forward, out RaycastHit hit, 5f))
                {
                    placementPos = hit.point;
                }
                else
                {
                    placementPos = origin + forward * 1f;
                }

                anchorTransform.position = placementPos;
                anchorTransform.rotation = Quaternion.LookRotation(forward, placementUp);

                IsAnchorPlaced = true;
                if (pointerLine != null)
                {
                    pointerLine.enabled = false;
                }

                OnAnchorPlaced?.Invoke();
            }

            triggerWasPressed = triggerPressed;
        }

        private static bool ReadRightTrigger()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!device.isValid)
            {
                return false;
            }

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed))
            {
                return pressed;
            }

            if (device.TryGetFeatureValue(CommonUsages.trigger, out float axis))
            {
                return axis > 0.5f;
            }

            return false;
        }

        public new void Reset()
        {
            IsAnchorPlaced = false;
            if (pointerLine != null)
            {
                pointerLine.enabled = true;
            }
        }
    }
}
