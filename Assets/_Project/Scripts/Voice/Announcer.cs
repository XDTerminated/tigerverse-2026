using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Tigerverse.Net;

namespace Tigerverse.Voice
{
    /// <summary>
    /// Lightweight ElevenLabs TTS wrapper. Pass a string, hear it spoken.
    /// Uses BackendConfig.elevenLabsApiKey + elevenLabsTtsVoiceId.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Announcer : MonoBehaviour
    {
        [SerializeField] private BackendConfig config;
        [SerializeField] private string defaultVoiceId = "21m00Tcm4TlvDq8ikWAM"; // ElevenLabs "Rachel" — works for any free key
        [SerializeField] private string modelId = "eleven_turbo_v2_5";

        private AudioSource _audio;

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            if (config == null) config = BackendConfig.Load();
        }

        public void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            StartCoroutine(Speak(text));
        }

        private IEnumerator Speak(string text)
        {
            if (config == null || string.IsNullOrEmpty(config.elevenLabsApiKey))
            {
                Debug.LogWarning("[Announcer] elevenLabsApiKey not set — skipping TTS.");
                yield break;
            }

            string voiceId = !string.IsNullOrEmpty(config.elevenLabsTtsVoiceId) ? config.elevenLabsTtsVoiceId : defaultVoiceId;
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

            string body = JsonUtility.ToJson(new TtsBody { text = text, model_id = modelId });

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "audio/mpeg");
                req.timeout = 15;

                yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                bool err = req.result != UnityWebRequest.Result.Success;
#else
                bool err = req.isNetworkError || req.isHttpError;
#endif
                if (err || req.responseCode >= 400)
                {
                    Debug.LogWarning($"[Announcer] TTS failed HTTP {req.responseCode}: {req.error}");
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip != null && _audio != null)
                {
                    _audio.PlayOneShot(clip);
                }
            }
        }

        [System.Serializable]
        private class TtsBody { public string text; public string model_id; }
    }
}
