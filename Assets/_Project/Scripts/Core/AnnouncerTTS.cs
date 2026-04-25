using System.Collections;
using System.IO;
using System.Text;
using Tigerverse.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace Tigerverse.Core
{
    /// <summary>
    /// Tiny ElevenLabs TTS bridge for announcer lines. Best-effort: bails out cleanly when
    /// no API key / voice ID is configured.
    /// </summary>
    public class AnnouncerTTS : MonoBehaviour
    {
        [SerializeField] private BackendConfig config;
        [SerializeField] private AudioSource audioSource;

        public IEnumerator Announce(string text)
        {
            if (config == null)
            {
                Debug.LogWarning("[AnnouncerTTS] No BackendConfig — skipping announce.");
                yield break;
            }

            if (string.IsNullOrEmpty(config.elevenLabsTtsVoiceId))
            {
                yield break;
            }

            string voiceId = config.elevenLabsTtsVoiceId;
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

            // Keep payload manual to avoid a dep on JsonUtility quirks for non-public fields.
            string body = "{\"text\":" + EscapeJson(text) + ",\"model_id\":\"eleven_turbo_v2_5\"}";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(bodyBytes) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "audio/mpeg");
                if (!string.IsNullOrEmpty(config.elevenLabsApiKey))
                {
                    req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
                }

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[AnnouncerTTS] TTS request failed: {req.error}");
                    yield break;
                }

                byte[] mp3Bytes = req.downloadHandler.data;
                if (mp3Bytes == null || mp3Bytes.Length == 0)
                {
                    Debug.LogWarning("[AnnouncerTTS] Empty TTS response.");
                    yield break;
                }

                // UnityWebRequestMultimedia.GetAudioClip(MPEG) only accepts a URI, so we stage to a
                // temp file and load that. Cheap and reliable on standalone/Quest.
                string tmpPath = Path.Combine(Application.temporaryCachePath,
                    $"announce_{System.DateTime.Now.Ticks}.mp3");
                File.WriteAllBytes(tmpPath, mp3Bytes);

                string fileUri = "file://" + tmpPath.Replace('\\', '/');
                using (var clipReq = UnityWebRequestMultimedia.GetAudioClip(fileUri, AudioType.MPEG))
                {
                    yield return clipReq.SendWebRequest();

                    if (clipReq.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[AnnouncerTTS] Clip load failed: {clipReq.error}");
                        yield break;
                    }

                    AudioClip clip = DownloadHandlerAudioClip.GetContent(clipReq);
                    if (clip != null && audioSource != null)
                    {
                        audioSource.PlayOneShot(clip);
                    }
                }
            }
        }

        private static string EscapeJson(string s)
        {
            if (s == null)
            {
                return "\"\"";
            }

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
