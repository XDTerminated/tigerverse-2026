using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Tigerverse.Net;

namespace Tigerverse.Voice
{
    /// <summary>
    /// Client-side monster cry generation via ElevenLabs Sound Effects.
    /// Pass a name + element, hand back an AudioClip the caller can assign
    /// to a MonsterCry component.
    /// </summary>
    public static class CryGenerator
    {
        public static IEnumerator Generate(string name, string element, System.Action<AudioClip> onClip)
        {
            var cfg = BackendConfig.Load();
            if (cfg == null || string.IsNullOrEmpty(cfg.elevenLabsApiKey))
            {
                Debug.LogWarning("[CryGenerator] elevenLabsApiKey not set — skipping cry generation.");
                onClip?.Invoke(null);
                yield break;
            }

            string prompt =
                $"A short non-human cartoon creature roar in the style of a Pokemon cry, " +
                $"vaguely yelling its own name \"{name}\" with a {element} elemental texture. " +
                $"Mouthy, energetic, around 2 seconds, no music, no lyrics.";

            string body = JsonUtility.ToJson(new SoundGenBody
            {
                text = prompt,
                duration_seconds = 2.0f,
                prompt_influence = 0.6f
            });

            const string url = "https://api.elevenlabs.io/v1/sound-generation";
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                req.SetRequestHeader("xi-api-key", cfg.elevenLabsApiKey);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "audio/mpeg");
                req.timeout = 30;

                yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                bool err = req.result != UnityWebRequest.Result.Success;
#else
                bool err = req.isNetworkError || req.isHttpError;
#endif
                if (err || req.responseCode >= 400)
                {
                    Debug.LogWarning($"[CryGenerator] sound-gen failed HTTP {req.responseCode}: {req.error}");
                    onClip?.Invoke(null);
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(req);
                onClip?.Invoke(clip);
            }
        }

        [System.Serializable]
        private class SoundGenBody
        {
            public string text;
            public float duration_seconds;
            public float prompt_influence;
        }
    }
}
