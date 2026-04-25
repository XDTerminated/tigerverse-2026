using Unity.Netcode;
using UnityEngine;

namespace Tigerverse.Networking
{
    public class NetworkedXRPlayer : NetworkBehaviour
    {
        [Header("Body parts on this prefab (avatar visuals)")]
        [SerializeField] Transform headTarget;
        [SerializeField] Transform leftHandTarget;
        [SerializeField] Transform rightHandTarget;

        [Header("Hide these from the local owner (e.g. their own head mesh)")]
        [SerializeField] GameObject[] hideForOwner;

        readonly NetworkVariable<PoseTriple> netPose = new(
            writePerm: NetworkVariableWritePermission.Owner);

        Transform xrHead;
        Transform xrLeft;
        Transform xrRight;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                BindLocalXRRig();
                foreach (var go in hideForOwner)
                {
                    if (go != null) go.SetActive(false);
                }
            }
        }

        void BindLocalXRRig()
        {
            var rig = LocalXRRigRegistry.Instance;
            if (rig == null)
            {
                Debug.LogWarning("[NetworkedXRPlayer] No LocalXRRigRegistry found in scene. " +
                                 "Add the component to your XR Origin and assign Head/LeftHand/RightHand.");
                return;
            }

            xrHead = rig.head;
            xrLeft = rig.leftHand;
            xrRight = rig.rightHand;
        }

        void LateUpdate()
        {
            if (IsOwner)
            {
                if (xrHead == null) return;

                var pose = new PoseTriple
                {
                    headPos = xrHead.position,
                    headRot = xrHead.rotation,
                    leftPos = xrLeft != null ? xrLeft.position : Vector3.zero,
                    leftRot = xrLeft != null ? xrLeft.rotation : Quaternion.identity,
                    rightPos = xrRight != null ? xrRight.position : Vector3.zero,
                    rightRot = xrRight != null ? xrRight.rotation : Quaternion.identity
                };
                netPose.Value = pose;

                ApplyPose(pose);
            }
            else
            {
                ApplyPose(netPose.Value);
            }
        }

        void ApplyPose(PoseTriple p)
        {
            if (headTarget != null)
            {
                headTarget.SetPositionAndRotation(p.headPos, p.headRot);
            }
            if (leftHandTarget != null)
            {
                leftHandTarget.SetPositionAndRotation(p.leftPos, p.leftRot);
            }
            if (rightHandTarget != null)
            {
                rightHandTarget.SetPositionAndRotation(p.rightPos, p.rightRot);
            }
        }

        struct PoseTriple : INetworkSerializeByMemcpy
        {
            public Vector3 headPos;
            public Quaternion headRot;
            public Vector3 leftPos;
            public Quaternion leftRot;
            public Vector3 rightPos;
            public Quaternion rightRot;
        }
    }
}
