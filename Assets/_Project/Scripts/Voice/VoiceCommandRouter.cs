using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Tigerverse.Combat;
using Tigerverse.Net;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.XR;

namespace Tigerverse.Voice
{
    public class VoiceCommandRouter : MonoBehaviour
    {
        private const string ScribeUrl = "https://api.elevenlabs.io/v1/speech-to-text";

        [SerializeField] private BackendConfig config;
        [SerializeField] private BattleManager battle; // assigned by GameStateManager
        [SerializeField] private int   casterIndex;
        [SerializeField] private int   sampleRate     = 16000;
        [SerializeField] private int   maxRecordSec   = 6;
        [SerializeField] private float matchThreshold = 0.35f;

        private MoveSO[]   availableMoves;
        private bool       isRecording;
        private bool       triggerWasPressed;
        private bool       spaceWasPressed;
        private AudioClip  currentClip;
        private string     micDeviceName;

        public string LastTranscript { get; private set; }

        public UnityEvent<string> OnTranscript = new();
        public UnityEvent<MoveSO> OnMoveCast   = new();
        public UnityEvent<string> OnNoMatch    = new();
        public UnityEvent         OnRecordStart = new();
        public UnityEvent         OnRecordEnd   = new();

        [Serializable]
        private class ScribeResponse
        {
            public string text;
            public float  language_probability;
            public string language_code;
        }

        private void Awake()
        {
            if (config == null) config = BackendConfig.Load();

            if (Microphone.devices != null && Microphone.devices.Length > 0)
            {
                micDeviceName = Microphone.devices[0];
            }
            else
            {
                Debug.LogWarning("[VoiceCommandRouter] No microphone devices found.", this);
            }
        }

        public void Bind(BattleManager battle, int casterIndex, MoveSO[] availableMoves)
        {
            this.battle         = battle;
            this.casterIndex    = casterIndex;
            this.availableMoves = availableMoves;
        }

        private void Update()
        {
            bool pressed = false;

            InputDevice rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rh.isValid && rh.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed))
            {
                pressed = triggerPressed;
            }

            // Editor / desktop fallback: spacebar (uses Input System).
            bool spaceNow = UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
            if (spaceNow != spaceWasPressed)
            {
                spaceWasPressed = spaceNow;
            }
            pressed = pressed || spaceNow;

            if (pressed && !triggerWasPressed)
            {
                BeginRecord();
            }
            else if (!pressed && triggerWasPressed)
            {
                EndRecord();
            }
            triggerWasPressed = pressed;
        }

        private void BeginRecord()
        {
            if (isRecording) return;
            if (string.IsNullOrEmpty(micDeviceName))
            {
                Debug.LogWarning("[VoiceCommandRouter] BeginRecord skipped: no mic device.", this);
                return;
            }
            currentClip = Microphone.Start(micDeviceName, false, maxRecordSec, sampleRate);
            isRecording = true;
            OnRecordStart?.Invoke();
        }

        private void EndRecord()
        {
            if (!isRecording) return;

            int pos = Microphone.GetPosition(micDeviceName);
            Microphone.End(micDeviceName);
            isRecording = false;
            OnRecordEnd?.Invoke();

            if (currentClip == null) return;

            // Reject under ~200ms of audio.
            if (pos < Mathf.RoundToInt(sampleRate * 0.2f))
            {
                return;
            }

            byte[] wavBytes = WavEncoder.EncodeWav(currentClip, pos);
            if (wavBytes == null || wavBytes.Length == 0) return;

            StartCoroutine(SendToScribe(wavBytes));
        }

        private IEnumerator SendToScribe(byte[] wavBytes)
        {
            List<IMultipartFormSection> form = new()
            {
                new MultipartFormDataSection("model_id", "scribe_v1"),
                new MultipartFormFileSection("file", wavBytes, "audio.wav", "audio/wav"),
            };

            using UnityWebRequest req = UnityWebRequest.Post(ScribeUrl, form);
            if (config != null && !string.IsNullOrEmpty(config.elevenLabsApiKey))
            {
                req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
            }
            else
            {
                Debug.LogWarning("[VoiceCommandRouter] ElevenLabs API key missing on BackendConfig.", this);
            }

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VoiceCommandRouter] Scribe request failed: {req.error}", this);
                OnNoMatch?.Invoke($"[net error] {req.error}");
                yield break;
            }

            string body = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
            ScribeResponse resp = null;
            try
            {
                resp = JsonConvert.DeserializeObject<ScribeResponse>(body);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoiceCommandRouter] Failed to parse Scribe response: {ex.Message}\n{body}", this);
            }

            LastTranscript = resp != null && resp.text != null ? resp.text : string.Empty;
            OnTranscript?.Invoke(LastTranscript);
            MatchAndCast(LastTranscript);
        }

        private void MatchAndCast(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                OnNoMatch?.Invoke(string.Empty);
                return;
            }
            if (availableMoves == null || availableMoves.Length == 0)
            {
                OnNoMatch?.Invoke(transcript);
                return;
            }

            string text = transcript.ToLowerInvariant().Trim();

            MoveSO bestMove      = null;
            float  bestScore     = float.MaxValue;
            bool   bestSubstring = false;

            for (int i = 0; i < availableMoves.Length; i++)
            {
                MoveSO move = availableMoves[i];
                if (move == null || move.triggerPhrases == null || move.triggerPhrases.Length == 0) continue;

                int idx = Levenshtein.BestPhraseMatch(text, move.triggerPhrases, out float dist, out bool sub);
                if (idx < 0) continue;

                float score = sub ? 0f : dist;

                bool better = false;
                if (sub && !bestSubstring) better = true;
                else if (sub == bestSubstring && score < bestScore) better = true;

                if (better)
                {
                    bestMove      = move;
                    bestScore     = score;
                    bestSubstring = sub;
                }
            }

            if (bestMove != null && (bestSubstring || bestScore < matchThreshold))
            {
                if (battle != null) battle.SubmitMove(bestMove, casterIndex);
                OnMoveCast?.Invoke(bestMove);
            }
            else
            {
                OnNoMatch?.Invoke(transcript);
            }
        }
    }
}
