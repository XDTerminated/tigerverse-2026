using UnityEngine;
using TMPro;

namespace Tigerverse.Combat
{
    public class MonsterCry : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip clip;
        [SerializeField] private GameObject taunSpeechBubblePrefab; // optional

        private bool _warnedNullClip;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 0.5f;
            audioSource.maxDistance = 5f;
            audioSource.playOnAwake = false;

            // If no AudioReverbZone in scene, apply slight reverb via mix.
            var reverb = FindObjectOfType<AudioReverbZone>();
            if (reverb == null)
                audioSource.reverbZoneMix = 1f;
        }

        public void SetClip(AudioClip c)
        {
            clip = c;
        }

        private bool EnsureClip()
        {
            if (clip == null)
            {
                if (!_warnedNullClip)
                {
                    Debug.LogWarning($"[MonsterCry] No clip assigned on '{name}'. Cry calls will be ignored.");
                    _warnedNullClip = true;
                }
                return false;
            }
            return true;
        }

        private void PlayAt(float pitch)
        {
            if (!EnsureClip()) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip);
        }

        public void PlaySpawn()        { PlayAt(1.0f); }
        public void PlayBeforeAttack() { PlayAt(1.0f); }
        public void PlayWin()          { PlayAt(1.05f); }
        public void PlayLose()         { PlayAt(0.85f); }

        public void PlayTaunt(string transcript)
        {
            PlayAt(1.0f);

            if (taunSpeechBubblePrefab == null) return;

            var bubble = Instantiate(taunSpeechBubblePrefab, transform);
            bubble.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            var tmp = bubble.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = transcript;

            Destroy(bubble, 3f);
        }
    }
}
