using UnityEngine;
using TMPro;

namespace Tigerverse.Combat
{
    public class MonsterCry : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip clip;
        [SerializeField] private GameObject taunSpeechBubblePrefab; // optional

        [Tooltip("Base pitch applied on every cry playback. <1 = lower / more monster-y, >1 = higher / cuter creature.")]
        [SerializeField] private float basePitch = 0.78f;

        private bool _warnedNullClip;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();
            }

            // Cries are gameplay-essential, fully 2D so they're always
            // audible at full volume regardless of where the monster is or
            // where the listener is.
            audioSource.spatialBlend = 0f;
            audioSource.volume = 1f;
            audioSource.playOnAwake = false;
            audioSource.bypassEffects = true;
            audioSource.bypassListenerEffects = true;
            audioSource.bypassReverbZones = true;

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
            audioSource.pitch = pitch * basePitch;
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
