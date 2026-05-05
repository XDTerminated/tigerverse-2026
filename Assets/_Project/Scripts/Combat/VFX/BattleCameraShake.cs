using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tigerverse.Combat.VFX
{
    public static class BattleCameraShake
    {
        private static readonly Dictionary<Camera, Coroutine> _activeShakes = new Dictionary<Camera, Coroutine>();

        public static void Shake(float amplitude, float duration, MonoBehaviour coroutineRunner)
        {
            if (coroutineRunner == null) return;
            if (duration <= 0f || amplitude <= 0f) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            if (_activeShakes.TryGetValue(cam, out Coroutine existing) && existing != null)
            {
                coroutineRunner.StopCoroutine(existing);
                _activeShakes.Remove(cam);
            }

            // WHY: capture localPosition as rest pose so shake is additive on top of XR rig motion;
            // setting world position would fight the rig, and using a fixed origin would snap the camera.
            Vector3 restPose = cam.transform.localPosition;

            Coroutine co = coroutineRunner.StartCoroutine(ShakeRoutine(cam, restPose, amplitude, duration));
            _activeShakes[cam] = co;
        }

        private static IEnumerator ShakeRoutine(Camera cam, Vector3 restPose, float amplitude, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (cam == null) yield break;

                float decay = 1f - (elapsed / duration);
                Vector3 offset = Random.insideUnitSphere * (amplitude * decay);
                cam.transform.localPosition = restPose + offset;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (cam != null)
            {
                cam.transform.localPosition = restPose;
                _activeShakes.Remove(cam);
            }
        }
    }
}
