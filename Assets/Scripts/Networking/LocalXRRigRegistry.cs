using UnityEngine;

namespace Tigerverse.Networking
{
    public class LocalXRRigRegistry : MonoBehaviour
    {
        public static LocalXRRigRegistry Instance { get; private set; }

        [Tooltip("The Main Camera under XR Origin (the headset pose).")]
        public Transform head;
        [Tooltip("The left controller transform under XR Origin.")]
        public Transform leftHand;
        [Tooltip("The right controller transform under XR Origin.")]
        public Transform rightHand;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
