using System;
using System.Collections;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Drives the egg cracking visual: cracks in shader, idle spin, and the burst-to-monster reveal.
    /// </summary>
    public class HatchingEggSequence : MonoBehaviour
    {
        [SerializeField] private Renderer eggRenderer;
        [SerializeField] private Material eggMaterialInstance; // assigned at runtime via eggRenderer.material
        [SerializeField] private ParticleSystem hatchBurst;
        [SerializeField] private AudioSource sfx;
        [SerializeField] private AudioClip hatchSfx;
        [SerializeField] private Transform eggTransform;
        [SerializeField] private float spinSpeedDeg = 30f;

        [Range(0f, 1f)] public float progress01;

        private readonly string crackProp = "_CrackAmount";
        private int crackPropId;
        private bool hasCrackProp;

        private Vector3 baseScale = Vector3.one;

        private void Awake()
        {
            if (eggMaterialInstance == null && eggRenderer != null)
            {
                eggMaterialInstance = eggRenderer.material;
            }

            crackPropId = Shader.PropertyToID(crackProp);
            hasCrackProp = eggMaterialInstance != null && eggMaterialInstance.HasProperty(crackPropId);

            if (eggTransform != null)
            {
                baseScale = eggTransform.localScale;
            }
        }

        private void Update()
        {
            if (eggMaterialInstance != null && hasCrackProp)
            {
                eggMaterialInstance.SetFloat(crackPropId, progress01);
            }

            if (eggTransform != null)
            {
                eggTransform.Rotate(0f, spinSpeedDeg * Time.deltaTime, 0f, Space.Self);

                if (progress01 > 0.3f)
                {
                    float pulse = Mathf.Sin(Time.time * 3f) * 0.02f + 1f;
                    eggTransform.localScale = baseScale * pulse;
                }
                else
                {
                    eggTransform.localScale = baseScale;
                }
            }
        }

        public IEnumerator BeginHatchSequence(GameObject monster, Vector3 spawnOrigin, Action onComplete)
        {
            if (hatchBurst != null)
            {
                hatchBurst.Play();
            }

            if (sfx != null && hatchSfx != null)
            {
                sfx.PlayOneShot(hatchSfx);
            }

            yield return new WaitForSeconds(0.4f);

            if (eggRenderer != null)
            {
                eggRenderer.gameObject.SetActive(false);
            }

            if (monster != null)
            {
                Transform mt = monster.transform;
                Vector3 startPos = spawnOrigin;
                // Forward in this context = -Z relative to player (monster steps toward camera).
                Vector3 endPos = spawnOrigin + new Vector3(0f, 0.4f, -0.3f);

                mt.position = startPos;

                const float dur = 2.0f;
                float t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    // ease-out cubic for emergence feel
                    float eased = 1f - Mathf.Pow(1f - k, 3f);
                    mt.position = Vector3.Lerp(startPos, endPos, eased);
                    yield return null;
                }

                mt.position = endPos;
            }

            onComplete?.Invoke();
        }
    }
}
