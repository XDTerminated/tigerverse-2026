using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.UI
{
    /// <summary>
    /// Watches for a real XR display subsystem to come online and destroys
    /// the XR Device Simulator if one is found. This lets the editor's
    /// auto-spawn behaviour stay aggressive (spawn on every Play Mode entry)
    /// while still getting out of the way when a Quest / Link / etc. is
    /// actually plugged in. Watches for ~5s after start because XR loaders
    /// can take a frame or two to come up after scene load.
    /// </summary>
    public class XRSimulatorVRGuard : MonoBehaviour
    {
        [SerializeField] private float watchSeconds = 5f;

        private float _deadline;
        private readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();

        private void Start()
        {
            _deadline = Time.time + watchSeconds;
            CheckAndDestroyIfRealXR();
        }

        private void Update()
        {
            if (Time.time > _deadline) { Destroy(this); return; }
            CheckAndDestroyIfRealXR();
        }

        private void CheckAndDestroyIfRealXR()
        {
            SubsystemManager.GetSubsystems(_displays);
            for (int i = 0; i < _displays.Count; i++)
            {
                if (_displays[i] != null && _displays[i].running)
                {
                    Debug.Log($"[Tigerverse] Real XR display active ({XRSettings.loadedDeviceName}). Removing XR Device Simulator.");
                    Destroy(gameObject);
                    return;
                }
            }
        }
    }
}
