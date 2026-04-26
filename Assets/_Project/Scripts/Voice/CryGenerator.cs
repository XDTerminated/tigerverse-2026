using System;
using System.Collections;
using Newtonsoft.Json;
using Tigerverse.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace Tigerverse.Voice
{
    /// <summary>
    /// Pokemon-style monster cry generator. Takes the monster's name, picks
    /// the first word, and dramatically yells it twice via ElevenLabs TTS.
    /// MonsterCry's <c>basePitch</c> then squashes the result into a
    /// creature-y register on playback.
    /// </summary>
    public static class CryGenerator
    {
        public static IEnumerator Generate(string name, string element, Action<AudioClip> onClip)
        {
            var cfg = BackendConfig.Load();
            if (cfg == null || string.IsNullOrEmpty(cfg.elevenLabsApiKey))
            {
                Debug.LogWarning("[CryGenerator] elevenLabsApiKey not set, skipping cry generation.");
                onClip?.Invoke(null);
                yield break;
            }

            // Strip to a single word, keeps the cry punchy and avoids the
            // monster reciting a full sentence. Also fall back to a generic
            // creature-y syllable if the name is empty/garbage.
            string firstWord = "Bweh";
            if (!string.IsNullOrWhiteSpace(name))
            {
                var parts = name.Trim().Split(new[] { ' ', '\t', ',', '.', '!', '?' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && parts[0].Length > 0) firstWord = parts[0];
                // Cap length so insanely long names don't take 4 seconds to say.
                if (firstWord.Length > 12) firstWord = firstWord.Substring(0, 12);
            }

            // Pick one of three Pokemon-ish patterns at random for variety.
            string text;
            int variant = UnityEngine.Random.Range(0, 3);
            switch (variant)
            {
                case 0:  text = $"{firstWord}! {firstWord}!"; break;
                case 1:  text = $"{firstWord}, {firstWord}-{firstWord}!"; break;
                default: text = $"{firstWord}! {firstWord}, {firstWord}!"; break;
            }

            string voiceId = !string.IsNullOrEmpty(cfg.elevenLabsTtsVoiceId)
                ? cfg.elevenLabsTtsVoiceId
                : "21m00Tcm4TlvDq8ikWAM"; // Rachel default

            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
            string body = JsonConvert.SerializeObject(new { text = text, model_id = "eleven_turbo_v2_5" });

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
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
                    Debug.LogWarning($"[CryGenerator] TTS failed HTTP {req.responseCode} '{req.error}' (text='{text}')");
                    onClip?.Invoke(null);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.length < 0.05f || clip.samples == 0)
                {
                    Debug.LogWarning($"[CryGenerator] TTS returned invalid clip (voice not in library?). text='{text}'");
                    onClip?.Invoke(null);
                    yield break;
                }

                Debug.Log($"[CryGenerator] '{name}' cry='{text}' length={clip.length:F2}s");
                onClip?.Invoke(clip);
            }
        }
    }
}
