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
        private const string GroqWhisperUrl = "https://api.groq.com/openai/v1/audio/transcriptions";

        [SerializeField] private BackendConfig config;
        [SerializeField] private BattleManager battle; // assigned by GameStateManager
        [SerializeField] private int   casterIndex;
        [SerializeField] private int   sampleRate     = 16000;
        [SerializeField] private int   maxRecordSec   = 6;
        [SerializeField] private float matchThreshold = 0.35f;

        [Tooltip("Substring to prefer when picking the mic. e.g. 'Quest' for the headset, 'Realtek' or empty for the laptop. If empty, the first device is used.")]
        [SerializeField] private string preferredMicSubstring = "";

        private MoveSO[]   availableMoves;

        /// <summary>The local player's currently-bound moveset, or null if no battle is active. Read-only view for HUDs.</summary>
        public MoveSO[] AvailableMoves => availableMoves;

        // Per-move cooldown gate. Maps a move asset to the earliest Time.time
        // at which it can be cast again. Cleared on Bind() so a fresh battle
        // doesn't inherit the previous one's stale cooldowns.
        private readonly Dictionary<MoveSO, float> _nextCastAt = new Dictionary<MoveSO, float>();

        /// <summary>Seconds remaining on this move's cooldown, or 0 if it's ready. For HUD/UI.</summary>
        public float GetCooldownRemaining(MoveSO move)
        {
            if (move == null) return 0f;
            if (!_nextCastAt.TryGetValue(move, out float t)) return 0f;
            return Mathf.Max(0f, t - Time.time);
        }
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

            PickMicDevice();
            // XR subsystems start a frame or two after Awake, so the first
            // pick can mis-detect whether we're really on a headset. Re-pick
            // shortly after to land on the laptop mic when no HMD ever
            // comes online (laptop play / simulator runs).
            StartCoroutine(RepickAfterXrInit());
        }

        private IEnumerator RepickAfterXrInit()
        {
            yield return new WaitForSeconds(1.5f);
            PickMicDevice();
        }

        /// <summary>
        /// Pick a mic device. If <see cref="preferredMicSubstring"/> is set,
        /// the first device whose name contains that substring is chosen
        /// (case-insensitive). Otherwise the system default (devices[0]) is
        /// used. Useful for switching between laptop mic in editor and the
        /// VR headset mic on Quest.
        /// </summary>
        public void PickMicDevice()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[VoiceCommandRouter] No microphone devices found.", this);
                micDeviceName = null;
                return;
            }

            if (!string.IsNullOrEmpty(preferredMicSubstring))
            {
                foreach (var d in devices)
                {
                    if (d != null && d.IndexOf(preferredMicSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        micDeviceName = d;
                        Debug.Log($"[VoiceCommandRouter] Mic = '{d}' (matched preferred substring '{preferredMicSubstring}'). Available: [{string.Join(", ", devices)}]");
                        return;
                    }
                }
                Debug.LogWarning($"[VoiceCommandRouter] preferredMicSubstring='{preferredMicSubstring}' didn't match any device. Trying auto-pick. Available: [{string.Join(", ", devices)}]");
            }

            // Only prefer the Oculus/Quest virtual mic if a real XR display
            // is actually running. Otherwise the virtual mic is silent (no
            // headset on the user's head) and we'd pick an input that never
            // captures anything, breaking voice commands on laptop / sim
            // play. Detect via XRDisplaySubsystem.running which is the same
            // signal used by XRSimulatorVRGuard.
            bool xrRunning = IsAnyXRDisplayRunning();

            string[] preferredAuto = { "Oculus", "Quest", "Headset" };
            if (xrRunning)
            {
                foreach (var pref in preferredAuto)
                {
                    foreach (var d in devices)
                    {
                        if (d != null && d.IndexOf(pref, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            micDeviceName = d;
                            Debug.Log($"[VoiceCommandRouter] Mic = '{d}' (auto-picked via '{pref}', XR active). Available: [{string.Join(", ", devices)}]");
                            return;
                        }
                    }
                }
            }
            else
            {
                // Laptop / simulator path: actively SKIP virtual XR mics and
                // pick the first real input instead.
                foreach (var d in devices)
                {
                    if (d == null) continue;
                    bool isVirtual = false;
                    foreach (var pref in preferredAuto)
                    {
                        if (d.IndexOf(pref, StringComparison.OrdinalIgnoreCase) >= 0) { isVirtual = true; break; }
                    }
                    if (d.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0) isVirtual = true;
                    if (isVirtual) continue;

                    micDeviceName = d;
                    Debug.Log($"[VoiceCommandRouter] Mic = '{d}' (laptop mode — no XR display running, skipping virtual headset mics). Available: [{string.Join(", ", devices)}]");
                    return;
                }
            }

            micDeviceName = devices[0];
            Debug.Log($"[VoiceCommandRouter] Mic = '{micDeviceName}' (first device fallback). Available: [{string.Join(", ", devices)}]");
        }

        private static readonly List<XRDisplaySubsystem> _xrDisplaysScratch = new List<XRDisplaySubsystem>();
        private static bool IsAnyXRDisplayRunning()
        {
            SubsystemManager.GetSubsystems(_xrDisplaysScratch);
            for (int i = 0; i < _xrDisplaysScratch.Count; i++)
            {
                if (_xrDisplaysScratch[i] != null && _xrDisplaysScratch[i].running) return true;
            }
            return false;
        }

        public void SetPreferredMicSubstring(string substring)
        {
            preferredMicSubstring = substring ?? "";
            PickMicDevice();
        }

        public void Bind(BattleManager battle, int casterIndex, MoveSO[] availableMoves)
        {
            this.battle         = battle;
            this.casterIndex    = casterIndex;
            this.availableMoves = availableMoves;
            _nextCastAt.Clear();
        }

        // Tap-to-record mode: press once → records for `tapRecordSec` seconds → auto-processes.
        // Trigger is the UI click button, so voice activates on RIGHT GRIP (and Spacebar in editor).
        [SerializeField] private float tapRecordSec = 4f;

        [Header("Open-mic mode (Voice-Activity-Detection)")]
        [Tooltip("If true, the router uses VAD: continuously listens, fires the transcription the instant the player stops speaking — no chunk timer.")]
        [SerializeField] private bool openMicMode = false;
        [Tooltip("RMS amplitude required to BEGIN counting as 'speaking' (0..1). Should be ABOVE typical room noise.")]
        [SerializeField] private float vadStartThreshold = 0.006f;
        [Tooltip("Once speech has started, RMS must drop below THIS to count as silence (hysteresis). Should be lower than vadStartThreshold.")]
        [SerializeField] private float vadContinueThreshold = 0.003f;
        [Tooltip("How long the player must stay quiet after speaking before the utterance is sent for transcription.")]
        [SerializeField] private float vadEndSilenceSec = 0.18f;
        [Tooltip("Window of recent audio (ms) analysed for RMS each tick.")]
        [SerializeField] private int   vadWindowMs = 60;
        [Tooltip("Minimum utterance length before sending (s). Prevents random clicks from being transcribed.")]
        [SerializeField] private float vadMinUtteranceSec = 0.12f;

        // Push-to-talk silence threshold (legacy; only used by the chunked path if VAD is off but openMicMode is true. Keep for compatibility.)
        [Tooltip("Peak amplitude below which a recorded clip is treated as silence and skipped.")]
        [SerializeField] private float openMicSilenceThreshold = 0.02f;

        private float _autoStopAt;
        private float _nextAutoTapAt;

        // VAD state for open-mic mode.
        private bool   _vadStarted;
        private bool   _vadInSpeech;
        private int    _vadSpeechStartPos;
        private int    _vadLastSpeechPos;
        private float  _vadLastTickAt;
        private float[] _vadAnalysisBuf;

        public void SetOpenMicMode(bool on)
        {
            openMicMode = on;
            Debug.Log($"[VoiceCommandRouter] openMicMode = {on}");
        }

        // Mute gate — used by the Professor tutorial (and combat announcer)
        // to prevent the mic from picking up its own TTS output through the
        // laptop speakers, which would otherwise loop back as fake questions.
        private bool _muted;

        public void SetMuted(bool muted)
        {
            if (_muted == muted) return;
            _muted = muted;
            // IMPORTANT: do NOT call Microphone.End / Microphone.Start here.
            // On Quest, restarting the mic device causes a ~150 ms main-thread
            // hitch — and we used to do that at the START AND END of every
            // single Professor TTS line, which the player perceived as the
            // game freezing on every new line. The mic stream is left
            // running; the muted flag below tells VadTick to drop samples
            // (so the speaker echo can't be transcribed as a fake question).
            if (muted && _vadInSpeech)
            {
                // If we were mid-utterance when the mute flipped, abort it
                // so the trailing TTS audio can't be spliced into a
                // transcription. Just reset the state machine — the mic
                // keeps streaming into the same circular clip.
                _vadInSpeech = false;
            }
            Debug.Log($"[VoiceCommandRouter] muted = {muted} (mic kept alive)");
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Editor-only test shortcut: press 1/2/3/4 to fire availableMoves[0..3]
            // directly, skipping voice transcription. Useful for testing the
            // wrist HUD + battle hookup without a working mic.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && availableMoves != null)
            {
                int idx = -1;
                if (kb.digit1Key.wasPressedThisFrame) idx = 0;
                else if (kb.digit2Key.wasPressedThisFrame) idx = 1;
                else if (kb.digit3Key.wasPressedThisFrame) idx = 2;
                else if (kb.digit4Key.wasPressedThisFrame) idx = 3;
                if (idx >= 0 && idx < availableMoves.Length && availableMoves[idx] != null)
                {
                    var move = availableMoves[idx];
                    Debug.Log($"[Voice/EditorTest] Forcing move {idx} '{move.displayName}'");
                    if (_nextCastAt.TryGetValue(move, out float readyAt) && Time.time < readyAt)
                    {
                        Debug.Log($"[Voice/EditorTest] '{move.displayName}' on cooldown — skipping.");
                    }
                    else
                    {
                        if (battle != null) battle.SubmitMove(move, casterIndex);
                        _nextCastAt[move] = Time.time + Mathf.Max(0f, move.cooldownSeconds);
                        OnMoveCast?.Invoke(move);
                    }
                }
            }
#endif

            // ─── Open-mic / VAD path ─────────────────────────────────────
            if (openMicMode)
            {
                if (_muted)
                {
                    // Mic stays running (avoids the per-line Quest hitch),
                    // we just skip the analysis tick so nothing gets
                    // transcribed during TTS playback.
                    return;
                }
                VadTick();
                return;
            }
            else if (_vadStarted)
            {
                // Open-mic mode itself just flipped off (not just a mute) —
                // OK to stop the continuous recording.
                StopVad();
            }

            // ─── Push-to-talk path ──────────────────────────────────────
            if (isRecording && Time.unscaledTime >= _autoStopAt)
            {
                EndRecord();
                return;
            }
            if (isRecording) return;

            bool tap = false;

            InputDevice rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rh.isValid && rh.TryGetFeatureValue(CommonUsages.gripButton, out bool gripPressed))
            {
                if (gripPressed && !triggerWasPressed) tap = true;
                triggerWasPressed = gripPressed;
            }

            bool spaceNow = UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
            if (spaceNow && !spaceWasPressed) tap = true;
            spaceWasPressed = spaceNow;

            if (tap)
            {
                BeginRecord();
                _autoStopAt = Time.unscaledTime + tapRecordSec;
            }
        }

        // ─── VAD continuous open-mic ────────────────────────────────────
        private void StartVad()
        {
            if (string.IsNullOrEmpty(micDeviceName)) return;
            // Loop=true → 10 s circular buffer that never stops on its own.
            currentClip = Microphone.Start(micDeviceName, true, 10, sampleRate);
            isRecording = true;
            _vadStarted = true;
            _vadInSpeech = false;
            int windowSamples = Mathf.Max(64, sampleRate * vadWindowMs / 1000);
            if (_vadAnalysisBuf == null || _vadAnalysisBuf.Length != windowSamples)
                _vadAnalysisBuf = new float[windowSamples];
            Debug.Log($"[VoiceCommandRouter] VAD STARTED on '{micDeviceName}' (sampleRate={sampleRate}, window={windowSamples} samples).");
        }

        private void StopVad()
        {
            if (_vadStarted)
            {
                try { Microphone.End(micDeviceName); } catch { }
                _vadStarted = false;
                isRecording = false;
                _vadInSpeech = false;
                Debug.Log("[VoiceCommandRouter] VAD STOPPED.");
            }
        }

        private void VadTick()
        {
            if (!_vadStarted) StartVad();
            if (!_vadStarted || currentClip == null) return;

            // Throttle to ~50 Hz so we don't burn CPU sampling every frame.
            if (Time.unscaledTime - _vadLastTickAt < 0.020f) return;
            _vadLastTickAt = Time.unscaledTime;

            int total   = currentClip.samples;
            int currentPos = Microphone.GetPosition(micDeviceName);
            if (currentPos < 0) return;
            int win = _vadAnalysisBuf.Length;
            int startSample = (currentPos - win + total) % total;
            currentClip.GetData(_vadAnalysisBuf, startSample);

            // RMS over the last window.
            float sumSq = 0f;
            for (int i = 0; i < win; i++) sumSq += _vadAnalysisBuf[i] * _vadAnalysisBuf[i];
            float rms = Mathf.Sqrt(sumSq / win);

            // Hysteresis: use higher threshold to BEGIN, lower to STAY.
            // This prevents word-mid-syllable RMS dips from prematurely
            // ending the utterance.
            float threshold = _vadInSpeech ? vadContinueThreshold : vadStartThreshold;
            bool active = rms > threshold;

            if (active)
            {
                if (!_vadInSpeech)
                {
                    _vadInSpeech = true;
                    _vadSpeechStartPos = startSample;
                    // Speech-START log silenced — kept too noisy in VR.
                }
                _vadLastSpeechPos = currentPos;
            }
            else if (_vadInSpeech)
            {
                int silenceSamples = (currentPos - _vadLastSpeechPos + total) % total;
                float silenceSec = silenceSamples / (float)sampleRate;
                if (silenceSec >= vadEndSilenceSec)
                {
                    int speechLen = (_vadLastSpeechPos - _vadSpeechStartPos + total) % total;
                    float utteranceSec = speechLen / (float)sampleRate;
                    if (utteranceSec >= vadMinUtteranceSec)
                    {
                        // Add a small head + tail pad (50 ms each side) so we
                        // don't clip onset/offset of short words like "ready".
                        int padSamples = sampleRate / 20; // 50 ms
                        int padStart   = (_vadSpeechStartPos - padSamples + total) % total;
                        int padLen     = Mathf.Min(speechLen + padSamples * 2, total - 1);

                        float[] data = new float[padLen];
                        currentClip.GetData(data, padStart);

                        var seg = AudioClip.Create("vadSeg", padLen, 1, sampleRate, false);
                        seg.SetData(data, 0);
                        byte[] wav = WavEncoder.EncodeWav(seg, padLen);
                        Destroy(seg);

                        StartCoroutine(SendToScribe(wav));
                    }
                    _vadInSpeech = false;
                }
            }
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

            // Open-mic silence skip: peak-amplitude check on the recorded
            // samples. Quiet chunks (no speech) are dropped to avoid burning
            // Scribe credits and spamming OnTranscript with empty strings.
            if (openMicMode)
            {
                var samples = new float[pos];
                currentClip.GetData(samples, 0);
                float peak = 0f;
                for (int i = 0; i < samples.Length; i++)
                {
                    float a = samples[i] < 0 ? -samples[i] : samples[i];
                    if (a > peak) peak = a;
                }
                if (peak < openMicSilenceThreshold)
                {
                    // Silent chunk — skip transcription entirely.
                    return;
                }
            }

            byte[] wavBytes = WavEncoder.EncodeWav(currentClip, pos);
            if (wavBytes == null || wavBytes.Length == 0) return;

            StartCoroutine(SendToScribe(wavBytes));
        }

        private IEnumerator SendToScribe(byte[] wavBytes)
        {
            // Prefer Groq's Whisper (whisper-large-v3-turbo) for sub-100ms
            // transcription. Fall back to ElevenLabs Scribe only if Groq
            // key isn't configured.
            bool useGroq = config != null && !string.IsNullOrEmpty(config.groqApiKey);
            string url = useGroq ? GroqWhisperUrl : ScribeUrl;

            List<IMultipartFormSection> form;
            if (useGroq)
            {
                form = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("model", "whisper-large-v3-turbo"),
                    new MultipartFormDataSection("response_format", "json"),
                    new MultipartFormDataSection("temperature", "0"),
                    new MultipartFormFileSection("file", wavBytes, "audio.wav", "audio/wav"),
                };
            }
            else
            {
                form = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("model_id", "scribe_v1"),
                    new MultipartFormFileSection("file", wavBytes, "audio.wav", "audio/wav"),
                };
            }

            float t0 = Time.realtimeSinceStartup;
            using UnityWebRequest req = UnityWebRequest.Post(url, form);
            if (useGroq)
            {
                req.SetRequestHeader("Authorization", "Bearer " + config.groqApiKey);
            }
            else if (config != null && !string.IsNullOrEmpty(config.elevenLabsApiKey))
            {
                req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
            }
            else
            {
                Debug.LogWarning("[VoiceCommandRouter] No transcription API key (groqApiKey or elevenLabsApiKey) on BackendConfig.", this);
            }

            yield return req.SendWebRequest();
            float dt = Time.realtimeSinceStartup - t0;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VoiceCommandRouter] Transcription request failed ({(useGroq ? "Groq" : "Scribe")}): HTTP {req.responseCode} {req.error} body={req.downloadHandler?.text}", this);
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
                Debug.LogError($"[VoiceCommandRouter] Failed to parse transcription response: {ex.Message}\n{body}", this);
            }

            LastTranscript = resp != null && resp.text != null ? resp.text : string.Empty;
            Debug.Log($"[VoiceCommandRouter] Transcribed ({(useGroq ? "Groq Whisper" : "ElevenLabs Scribe")}, {dt:F2}s): '{LastTranscript}'");
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
                Debug.Log($"[Voice] Match → '{bestMove.displayName}', battle={(battle!=null?"OK":"NULL")}, casterIdx={casterIndex}");

                // Per-move cooldown gate. Each move locks itself out for
                // bestMove.cooldownSeconds after a successful cast — stronger
                // moves are tuned with longer cooldowns. Other moves stay
                // available, so the player can still cycle through their kit.
                if (_nextCastAt.TryGetValue(bestMove, out float readyAt) && Time.time < readyAt)
                {
                    Debug.Log($"[VoiceCommandRouter] '{bestMove.displayName}' on cooldown ({(readyAt - Time.time):F1}s remaining).");
                    OnNoMatch?.Invoke(transcript);
                    return;
                }

                if (battle != null) battle.SubmitMove(bestMove, casterIndex);
                _nextCastAt[bestMove] = Time.time + Mathf.Max(0f, bestMove.cooldownSeconds);
                OnMoveCast?.Invoke(bestMove);
            }
            else
            {
                OnNoMatch?.Invoke(transcript);
            }
        }
    }
}
