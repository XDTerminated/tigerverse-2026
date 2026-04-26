using UnityEngine;

namespace Tigerverse.Net
{
    [CreateAssetMenu(fileName = "BackendConfig", menuName = "Tigerverse/Backend Config")]
    public class BackendConfig : ScriptableObject
    {
        public string backendBaseUrl = "https://tigerverse-2026.vercel.app";
        public bool useMock = false;

        [Tooltip("Client-side ElevenLabs key for Scribe STT and TTS announcer")]
        public string elevenLabsApiKey = "";

        [Tooltip("Optional voice ID for announcer TTS")]
        public string elevenLabsTtsVoiceId = "";

        [Tooltip("Groq Cloud API key (for LLM-driven Professor Q&A in tutorial)")]
        public string groqApiKey = "";

        [Tooltip("Groq model id, e.g. llama-3.3-70b-versatile or llama-3.1-8b-instant")]
        public string groqModel = "llama-3.3-70b-versatile";

        public float pollIntervalSec = 3f;

        [Tooltip("Photon Fusion 2 App ID")]
        public string photonAppId = "";

        public string photonRegion = "us";

        private static BackendConfig _cache;

        public static BackendConfig Load()
        {
            if (_cache != null) return _cache;

            _cache = Resources.Load<BackendConfig>("BackendConfig");
            if (_cache == null)
            {
                Debug.LogWarning("[BackendConfig] Resources/BackendConfig.asset not found. Returning a default in-memory instance.");
                _cache = ScriptableObject.CreateInstance<BackendConfig>();
                _cache.backendBaseUrl = "https://tigerverse-2026.vercel.app";
                _cache.useMock = false;
                _cache.elevenLabsApiKey = "";
                _cache.elevenLabsTtsVoiceId = "";
                _cache.pollIntervalSec = 3f;
                _cache.photonAppId = "";
                _cache.photonRegion = "us";
            }
            return _cache;
        }
    }
}
