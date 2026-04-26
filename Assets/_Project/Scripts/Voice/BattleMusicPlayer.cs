using System.Collections;
using UnityEngine;

namespace Tigerverse.Voice
{
    /// <summary>
    /// Lazy self-spawning singleton that plays a looping background music
    /// track during battles. Call Instance.Play() to fade the track in,
    /// Instance.Stop() to fade it back out.
    /// </summary>
    public class BattleMusicPlayer : MonoBehaviour
    {
        private const string ResourcePath = "Music/BattleMusic";
        private const float FadeSeconds = 1.0f;
        private const float PlayingVolume = 0.18f;
        private const float StoppedVolume = 0f;

        private static BattleMusicPlayer _instance;
        public static BattleMusicPlayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BattleMusicPlayer");
                    _instance = go.AddComponent<BattleMusicPlayer>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private AudioSource _audio;
        private AudioClip _clip;
        private Coroutine _fadeRoutine;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f; // 2D, ignores camera position.
            _audio.volume = StoppedVolume;

            _clip = Resources.Load<AudioClip>(ResourcePath);
            if (_clip == null)
            {
                Debug.LogWarning($"[BattleMusicPlayer] Could not load AudioClip at Resources/{ResourcePath}.");
            }
            else
            {
                _audio.clip = _clip;
            }
        }

        public void Play()
        {
            if (_audio == null) return;
            if (_clip == null)
            {
                // Try once more in case the clip arrived after Awake.
                _clip = Resources.Load<AudioClip>(ResourcePath);
                if (_clip != null) _audio.clip = _clip;
            }
            if (_audio.clip == null) return;

            if (!_audio.isPlaying) _audio.Play();
            StartFade(PlayingVolume);
        }

        public void Stop()
        {
            if (_audio == null) return;
            StartFade(StoppedVolume, stopAfterFade: true);
        }

        private void StartFade(float target, bool stopAfterFade = false)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeTo(target, stopAfterFade));
        }

        private IEnumerator FadeTo(float target, bool stopAfterFade)
        {
            float start = _audio.volume;
            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FadeSeconds);
                _audio.volume = Mathf.Lerp(start, target, k);
                yield return null;
            }
            _audio.volume = target;
            if (stopAfterFade && _audio.isPlaying) _audio.Stop();
            _fadeRoutine = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
