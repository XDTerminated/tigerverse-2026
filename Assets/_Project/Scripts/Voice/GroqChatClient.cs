using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Tigerverse.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace Tigerverse.Voice
{
    /// <summary>
    /// Minimal Groq Cloud chat-completions wrapper. Groq's REST API mirrors
    /// OpenAI's chat-completions schema, so the request body is the standard
    /// {"model","messages":[{"role","content"}]} shape.
    ///
    /// Usage:
    ///   yield return GroqChatClient.Ask(systemPrompt, userText, reply => Debug.Log(reply));
    /// </summary>
    public static class GroqChatClient
    {
        private const string CompletionsUrl = "https://api.groq.com/openai/v1/chat/completions";

        [Serializable]
        private class ChatMsg { public string role; public string content; }

        [Serializable]
        private class ChatRequest
        {
            public string model;
            public ChatMsg[] messages;
            public float temperature = 0.7f;
            public int max_tokens = 220;
        }

        [Serializable]
        private class ChatChoiceMessage { public string role; public string content; }
        [Serializable]
        private class ChatChoice { public int index; public ChatChoiceMessage message; }
        [Serializable]
        private class ChatResponse { public ChatChoice[] choices; }

        public static IEnumerator Ask(string systemPrompt, string userText, Action<string> onReply)
        {
            var cfg = BackendConfig.Load();
            if (cfg == null || string.IsNullOrEmpty(cfg.groqApiKey))
            {
                Debug.LogWarning("[GroqChatClient] groqApiKey not set on BackendConfig — returning fallback reply.");
                onReply?.Invoke("Hmm — my memory's foggy today. Try asking again after the egg hatches.");
                yield break;
            }

            string model = string.IsNullOrEmpty(cfg.groqModel) ? "llama-3.3-70b-versatile" : cfg.groqModel;

            var req = new ChatRequest
            {
                model = model,
                messages = new[]
                {
                    new ChatMsg { role = "system", content = systemPrompt ?? "You are a helpful assistant." },
                    new ChatMsg { role = "user",   content = userText ?? "" },
                },
                temperature = 0.7f,
                max_tokens = 220,
            };

            string body;
            try { body = JsonConvert.SerializeObject(req); }
            catch (Exception e)
            {
                Debug.LogException(e);
                onReply?.Invoke(null);
                yield break;
            }

            byte[] payload = Encoding.UTF8.GetBytes(body);
            using (var web = new UnityWebRequest(CompletionsUrl, UnityWebRequest.kHttpVerbPOST))
            {
                web.uploadHandler   = new UploadHandlerRaw(payload);
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Authorization", "Bearer " + cfg.groqApiKey);
                web.SetRequestHeader("Content-Type",  "application/json");
                web.timeout = 30;

                yield return web.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                bool err = web.result != UnityWebRequest.Result.Success;
#else
                bool err = web.isNetworkError || web.isHttpError;
#endif
                if (err)
                {
                    Debug.LogWarning($"[GroqChatClient] HTTP {web.responseCode} {web.error}\n{web.downloadHandler.text}");
                    onReply?.Invoke(null);
                    yield break;
                }

                ChatResponse resp = null;
                try { resp = JsonConvert.DeserializeObject<ChatResponse>(web.downloadHandler.text); }
                catch (Exception e) { Debug.LogException(e); }

                string reply = (resp != null && resp.choices != null && resp.choices.Length > 0
                                && resp.choices[0].message != null)
                               ? resp.choices[0].message.content
                               : null;
                onReply?.Invoke(string.IsNullOrEmpty(reply) ? null : reply.Trim());
            }
        }
    }
}
