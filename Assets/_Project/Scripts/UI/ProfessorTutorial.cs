using System;
using System.Collections;
using System.Collections.Generic;
using GLTFast;
using Tigerverse.Drawing;
using Tigerverse.Net;
using Tigerverse.Voice;
using UnityEngine;
using UnityEngine.Networking;

namespace Tigerverse.UI
{
    /// <summary>
    /// Per-player tutorial: spawns a paper-craft Professor next to the
    /// player, plays a scripted welcome via ElevenLabs TTS (George — old
    /// kindly wizard voice), runs a brief practice fight (player shouts
    /// THUNDER BOLT, the borrowed scribble one-shots a target dummy),
    /// then opens a Groq-powered Q&amp;A loop until the egg hatches.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProfessorTutorial : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector3 professorOffset = new Vector3(0.9f, 0f, 0.4f);
        [SerializeField] private float   professorYawDeg = -30f;

        [Header("Voice")]
        [SerializeField] private BackendConfig config;
        [Tooltip("ElevenLabs voice ID. Defaults to George (old kindly British male).")]
        [SerializeField] private string overrideVoiceId = "JBFqnCBsd6RMkjVDRZzb";
        [SerializeField] private string ttsModelId = "eleven_turbo_v2_5";

        [Header("Practice fight")]
        [Tooltip("Cached UploadThing GLB used as the Professor's borrowed scribble during the practice fight.")]
        [SerializeField] private string borrowedScribbleGlbUrl = "https://ueggfh303j.ufs.sh/f/hqoaI3f7pqQl6koHkKXdhzSPI8ykOc45FrwKeWNpJfbAYMB6";
        [Tooltip("How long the player has to say THUNDER BOLT before the dummy 'wins' anyway and we move on.")]
        [SerializeField] private float practiceListenWindowSec = 12f;

        [Header("Behaviour")]
        [SerializeField] private bool allowQandA = true;
        [SerializeField] private TMPro.TextMeshPro subtitleLabel;

        [Header("Refs (auto-found if null)")]
        [SerializeField] private VoiceCommandRouter voiceRouter;

        // ─── Runtime ─────────────────────────────────────────────────────
        private PaperProfessor _professor;
        private AudioSource    _audio;
        private bool           _stopRequested;
        private bool           _qaMode;
        private bool           _practiceMode;
        private bool           _practiceTriggered;
        private GameObject     _borrowedScribble;
        private GameObject     _dummy;
        private readonly Queue<string> _pendingQuestions = new Queue<string>();
        private readonly List<string>  _conversationLog = new List<string>();

        private const int MaxConversationTurns = 8;
        private const string FallbackVoice = "21m00Tcm4TlvDq8ikWAM"; // Rachel — works on every ElevenLabs free key

        private const string SystemPrompt =
            "You are Professor Hooten, a kindly elderly paper-craft wizard who teaches new trainers " +
            "the world of Scribble Showdown, a turn-based VR battle game where two players draw monsters " +
            "that come to life as paper-craft creatures and fight each other. " +
            "Combat is turn-based and every attack is voice-activated. The player says the move name out loud " +
            "(for example: thunder bolt, fire punch, water blast, vine whip, ice shard, dark pulse, earth slam). " +
            "Keep replies WARM, BRIEF (one short sentence), and stay in character as a slightly-doddering kind old wizard. " +
            "Never use em-dashes. Never use emojis. If asked something unrelated, redirect to the game.";

        // 6 lines, no em-dashes, no dodge, no type matchups. Line 4 marks
        // where we trigger the practice listening window.
        private static readonly string[] ScriptedLines =
        {
            "Welcome to the world of Scribble Showdown! I'm Professor Hooten.",
            "Your egg is still hatching, so why don't I lend you one of my old scribbles to teach you the ropes.",
            "Combat in this world is turn based, so you and your opponent take turns making a move.",
            "And here's the magical part. Every attack is voice activated. You simply say the move out loud.",
            "Let's give it a try. Aim at that practice dummy and shout, THUNDER BOLT!",
            "Beautiful! Just keep an eye on your scribble's HP, and you'll be just fine. Got any questions before we begin?"
        };

        private const int LineIdx_LendScribble = 1;
        private const int LineIdx_PracticeCue  = 4;
        private const int LineIdx_Wrapup       = 5;

        // ─── Lifecycle ──────────────────────────────────────────────────
        private void Awake()
        {
            if (config == null) config = BackendConfig.Load();
            if (voiceRouter == null) voiceRouter = FindFirstObjectByType<VoiceCommandRouter>();

            // Match Announcer.cs's AudioSource config exactly — fully 2D
            // (no spatial attenuation), playOnAwake off, volume 1.
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0f;
            _audio.playOnAwake  = false;
            _audio.volume       = 1.0f;
            _audio.bypassEffects        = true;
            _audio.bypassListenerEffects = true;
            _audio.bypassReverbZones     = true;
        }

        private void Start()
        {
            BuildScene();
            // Switch the voice router into open-mic mode for the duration of
            // the tutorial so the player can just speak naturally — no
            // push-to-talk friction during practice or Q&A.
            if (voiceRouter != null) voiceRouter.SetOpenMicMode(true);
            StartCoroutine(RunTutorial());
        }

        private void OnDestroy()
        {
            if (voiceRouter != null)
            {
                voiceRouter.OnTranscript.RemoveListener(OnPlayerSpoke);
                voiceRouter.SetOpenMicMode(false); // restore push-to-talk for combat
            }
        }

        public void Stop() { _stopRequested = true; }

        // ─── Scene build ────────────────────────────────────────────────
        private void BuildScene()
        {
            var profGo = new GameObject("PaperProfessor");
            profGo.transform.SetParent(transform, worldPositionStays: false);
            profGo.transform.localPosition = professorOffset;
            profGo.transform.localRotation = Quaternion.Euler(0, professorYawDeg, 0);
            _professor = profGo.AddComponent<PaperProfessor>();

            if (subtitleLabel == null)
            {
                var lblGo = new GameObject("ProfessorSubtitle");
                lblGo.transform.SetParent(profGo.transform, false);
                lblGo.transform.localPosition = new Vector3(0, 1.45f, 0);
                subtitleLabel = lblGo.AddComponent<TMPro.TextMeshPro>();
                subtitleLabel.text = "";
                subtitleLabel.fontSize = 0.5f;
                subtitleLabel.alignment = TMPro.TextAlignmentOptions.Center;
                subtitleLabel.color = new Color(0.07f, 0.06f, 0.10f, 1);
                subtitleLabel.outlineColor = new Color32(255, 255, 255, 230);
                subtitleLabel.outlineWidth = 0.18f;
                subtitleLabel.enableWordWrapping = true;
                subtitleLabel.rectTransform.sizeDelta = new Vector2(2.2f, 0.6f);
            }
        }

        // ─── Tutorial flow ──────────────────────────────────────────────
        private IEnumerator RunTutorial()
        {
            // Kick off the borrowed-scribble GLB download in parallel so it
            // is ready by the time we actually need it for the practice beat.
            StartCoroutine(LoadBorrowedScribble());

            for (int i = 0; i < ScriptedLines.Length; i++)
            {
                if (_stopRequested) yield break;

                yield return SpeakLine(ScriptedLines[i]);
                yield return new WaitForSeconds(0.20f);

                if (i == LineIdx_LendScribble)
                {
                    SpawnDummy();
                }
                else if (i == LineIdx_PracticeCue)
                {
                    yield return RunPracticeListenWindow();
                }
            }

            if (allowQandA && voiceRouter != null && !_stopRequested)
            {
                voiceRouter.OnTranscript.AddListener(OnPlayerSpoke);
                _qaMode = true;
                ShowSubtitle("(Listening — ask me anything!)");

                while (!_stopRequested)
                {
                    if (_pendingQuestions.Count > 0)
                    {
                        string q = _pendingQuestions.Dequeue();
                        yield return AnswerQuestion(q);
                        if (!_stopRequested) ShowSubtitle("(Listening — ask me anything else!)");
                    }
                    yield return null;
                }

                voiceRouter.OnTranscript.RemoveListener(OnPlayerSpoke);
                _qaMode = false;
            }

            yield return SpeakLine("Good luck out there, trainer. Your scribble is almost ready.");
        }

        // ─── Practice fight ─────────────────────────────────────────────
        private IEnumerator LoadBorrowedScribble()
        {
            if (string.IsNullOrEmpty(borrowedScribbleGlbUrl)) yield break;

            byte[] glbBytes = null;
            using (var req = UnityWebRequest.Get(borrowedScribbleGlbUrl))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 30;
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool err = req.result != UnityWebRequest.Result.Success;
#else
                bool err = req.isNetworkError || req.isHttpError;
#endif
                if (err)
                {
                    Debug.LogWarning($"[ProfessorTutorial] Borrowed-scribble GLB download failed: HTTP {req.responseCode} {req.error}");
                    yield break;
                }
                glbBytes = req.downloadHandler.data;
            }
            if (glbBytes == null || glbBytes.Length == 0) yield break;

            var gltf = new GltfImport();
            var loadTask = gltf.LoadGltfBinary(glbBytes);
            yield return new WaitUntil(() => loadTask.IsCompleted);
            if (loadTask.IsFaulted || (loadTask.IsCompleted && loadTask.Result == false))
            {
                Debug.LogWarning("[ProfessorTutorial] glTFast LoadGltfBinary failed.");
                yield break;
            }

            var container = new GameObject("BorrowedScribble");
            container.transform.SetParent(transform, false);
            // Sit between the Professor and the dummy.
            container.transform.localPosition = new Vector3(professorOffset.x - 0.55f, 0f, professorOffset.z - 0.4f);
            container.transform.localRotation = Quaternion.Euler(0, 90f, 0);

            var instTask = gltf.InstantiateMainSceneAsync(container.transform);
            yield return new WaitUntil(() => instTask.IsCompleted);
            if (instTask.IsFaulted || (instTask.IsCompleted && instTask.Result == false))
            {
                Destroy(container);
                yield break;
            }

            // Auto-scale to ~0.5 m max axis.
            AutoScale(container.transform, 0.55f);
            // Apply paper shader with a small procedural drawing so the body
            // matches the rest of the world.
            try { DrawingColorize.Apply(container, MakeSolidTexture(new Color(0.95f, 0.85f, 0.55f)), 0f); }
            catch (Exception e) { Debug.LogException(e); }

            _borrowedScribble = container;
        }

        private void SpawnDummy()
        {
            if (_dummy != null) return;
            // Procedural paper-craft target dummy: head ball + body cylinder
            // + a tiny X face, painted plain white. Faces toward the Professor.
            var root = new GameObject("PracticeDummy");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(professorOffset.x - 1.4f, 0f, professorOffset.z - 0.5f);
            root.transform.localRotation = Quaternion.Euler(0, 90f, 0);

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var paperMat = new Material(sh);
            if (paperMat.HasProperty("_BaseColor")) paperMat.SetColor("_BaseColor", new Color(0.97f, 0.95f, 0.91f));
            else paperMat.color = new Color(0.97f, 0.95f, 0.91f);

            var darkMat = new Material(sh);
            if (darkMat.HasProperty("_BaseColor")) darkMat.SetColor("_BaseColor", new Color(0.10f, 0.10f, 0.12f));
            else darkMat.color = new Color(0.10f, 0.10f, 0.12f);

            MakePrim(root.transform, PrimitiveType.Cylinder, paperMat, "Body", new Vector3(0, 0.30f, 0), new Vector3(0.26f, 0.30f, 0.26f));
            MakePrim(root.transform, PrimitiveType.Sphere,   paperMat, "Head", new Vector3(0, 0.72f, 0), new Vector3(0.26f, 0.26f, 0.26f));

            // Eyes — two black X marks (just two thin cubes crossing).
            var head = root.transform.Find("Head");
            MakePrim(head, PrimitiveType.Cube, darkMat, "EyeLA", new Vector3(-0.07f, 0.0f, -0.12f), new Vector3(0.04f, 0.005f, 0.005f), Quaternion.Euler(0, 0,  35f));
            MakePrim(head, PrimitiveType.Cube, darkMat, "EyeLB", new Vector3(-0.07f, 0.0f, -0.12f), new Vector3(0.04f, 0.005f, 0.005f), Quaternion.Euler(0, 0, -35f));
            MakePrim(head, PrimitiveType.Cube, darkMat, "EyeRA", new Vector3( 0.07f, 0.0f, -0.12f), new Vector3(0.04f, 0.005f, 0.005f), Quaternion.Euler(0, 0,  35f));
            MakePrim(head, PrimitiveType.Cube, darkMat, "EyeRB", new Vector3( 0.07f, 0.0f, -0.12f), new Vector3(0.04f, 0.005f, 0.005f), Quaternion.Euler(0, 0, -35f));

            _dummy = root;
        }

        private static Transform MakePrim(Transform parent, PrimitiveType t, Material mat, string name, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static void AutoScale(Transform root, float targetMaxAxis)
        {
            var rends = root.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            Bounds b = rends[0].bounds; bool first = false;
            foreach (var r in rends) { if (first) b.Encapsulate(r.bounds); else { b = r.bounds; first = true; } }
            if (b.size == Vector3.zero) return;
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (longest <= 1e-6f) return;
            root.localScale *= targetMaxAxis / longest;
        }

        private static Texture2D MakeSolidTexture(Color c)
        {
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color32[64];
            Color32 c32 = c;
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            t.SetPixels32(px); t.Apply(false, false);
            return t;
        }

        private IEnumerator RunPracticeListenWindow()
        {
            if (voiceRouter == null)
            {
                Debug.LogWarning("[ProfessorTutorial] No VoiceCommandRouter in scene — skipping practice listen window.");
                yield break;
            }
            _practiceMode = true;
            _practiceTriggered = false;
            voiceRouter.OnTranscript.AddListener(OnPracticeTranscript);

            ShowSubtitle("(Just shout THUNDER BOLT, the mic is listening!)");
            Debug.Log($"[ProfessorTutorial] Practice listen window OPEN ({practiceListenWindowSec:F0}s). Open-mic mode active — speak any time.");

            float t = 0f;
            while (t < practiceListenWindowSec && !_practiceTriggered && !_stopRequested)
            {
                t += Time.deltaTime;
                yield return null;
            }

            voiceRouter.OnTranscript.RemoveListener(OnPracticeTranscript);
            _practiceMode = false;
            Debug.Log($"[ProfessorTutorial] Practice listen window CLOSED. triggered={_practiceTriggered}");

            if (!_practiceTriggered)
            {
                yield return SpeakLine("That's alright. Let me show you. Watch this.");
            }

            yield return PerformThunderBolt();
        }

        private void OnPracticeTranscript(string transcript)
        {
            Debug.Log($"[ProfessorTutorial] Practice heard transcript: '{transcript}'");
            if (!_practiceMode || _practiceTriggered) return;
            if (string.IsNullOrEmpty(transcript)) return;
            string lower = transcript.ToLowerInvariant();
            bool match = lower.Contains("thunder") || lower.Contains("bolt") || lower.Contains("lightning");
            Debug.Log($"[ProfessorTutorial] Match = {match} for '{lower}'");
            if (match) _practiceTriggered = true;
        }

        private IEnumerator PerformThunderBolt()
        {
            if (_borrowedScribble == null && _dummy == null) yield break;

            // Quick "wind-up" on the borrowed scribble — small jump.
            if (_borrowedScribble != null)
            {
                Vector3 startPos = _borrowedScribble.transform.localPosition;
                float dur = 0.25f, t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Sin((t / dur) * Mathf.PI);
                    _borrowedScribble.transform.localPosition = startPos + new Vector3(0, 0.10f * k, 0);
                    yield return null;
                }
                _borrowedScribble.transform.localPosition = startPos;
            }

            // Lightning particle burst from scribble to dummy.
            SpawnLightningEffect();

            // Dummy reels then falls over.
            if (_dummy != null)
            {
                Vector3 startPos = _dummy.transform.localPosition;
                Quaternion startRot = _dummy.transform.localRotation;

                // Hit flash — quick scale punch.
                float t = 0f, dur = 0.15f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Sin((t / dur) * Mathf.PI);
                    _dummy.transform.localScale = Vector3.one * (1f + 0.18f * k);
                    yield return null;
                }
                _dummy.transform.localScale = Vector3.one;

                // Fall over (rotate around its base).
                t = 0f; dur = 0.55f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    float eased = k * k;
                    _dummy.transform.localRotation = startRot * Quaternion.Euler(0f, 0f, 90f * eased);
                    _dummy.transform.localPosition = startPos + new Vector3(0, -0.05f * eased, 0);
                    yield return null;
                }

                // Fade out + destroy.
                yield return new WaitForSeconds(0.35f);
                FadeAndDestroy(_dummy, 0.4f);
                _dummy = null;
            }

            yield return new WaitForSeconds(0.2f);
        }

        private void SpawnLightningEffect()
        {
            if (_borrowedScribble == null || _dummy == null) return;

            var go = new GameObject("LightningFx");
            go.transform.SetParent(transform, false);
            go.transform.position = _borrowedScribble.transform.position + Vector3.up * 0.4f;

            var ps = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.4f));
            else mat.color = new Color(1f, 0.95f, 0.4f);
            psr.sharedMaterial = mat;
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.lengthScale = 4f;
            psr.velocityScale = 0.3f;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.4f), new Color(1f, 1f, 1f));
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

            // Aim particles toward the dummy.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.05f;
            Vector3 toDummy = (_dummy.transform.position + Vector3.up * 0.4f) - go.transform.position;
            go.transform.rotation = Quaternion.LookRotation(toDummy.normalized, Vector3.up);
            // Cone in PS is along +Z by default if shape doesn't override, which works here.

            ps.Play();
            Destroy(go, 1.5f);
        }

        private void FadeAndDestroy(GameObject go, float duration)
        {
            StartCoroutine(FadeRoutine(go, duration));
        }

        private IEnumerator FadeRoutine(GameObject go, float duration)
        {
            if (go == null) yield break;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            float t = 0f;
            while (t < duration && go != null)
            {
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / duration);
                foreach (var r in rends)
                {
                    if (r == null || r.sharedMaterial == null) continue;
                    if (r.sharedMaterial.HasProperty("_BaseColor"))
                    {
                        var c = r.sharedMaterial.GetColor("_BaseColor"); c.a = a;
                        r.sharedMaterial.SetColor("_BaseColor", c);
                    }
                }
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ─── Q&A ────────────────────────────────────────────────────────
        private void OnPlayerSpoke(string transcript)
        {
            if (!_qaMode) return;
            if (string.IsNullOrWhiteSpace(transcript)) return;

            // Filter junk / micro-fragments. A real question usually has
            // at least a few characters and contains a vowel-like word.
            string trimmed = transcript.Trim();
            if (trimmed.Length < 4)
            {
                Debug.Log($"[ProfessorTutorial] Q&A ignored short transcript: '{trimmed}'");
                return;
            }

            Debug.Log($"[ProfessorTutorial] Q&A heard: '{trimmed}'");
            _pendingQuestions.Enqueue(trimmed);
        }

        private IEnumerator AnswerQuestion(string question)
        {
            ShowSubtitle("(thinking...)");

            string reply = null;
            yield return GroqChatClient.Ask(SystemPrompt, BuildPromptContext(question), r => reply = r);
            if (string.IsNullOrEmpty(reply))
                reply = "Hmm, my crystal ball's a bit cloudy. Ask me once your scribble's out.";

            // Strip em-dashes if the model produced any.
            reply = reply.Replace("—", ", ").Replace(" - ", ", ");

            _conversationLog.Add("Q: " + question);
            _conversationLog.Add("A: " + reply);
            while (_conversationLog.Count > MaxConversationTurns * 2) _conversationLog.RemoveAt(0);

            yield return SpeakLine(reply);
        }

        private string BuildPromptContext(string question)
        {
            if (_conversationLog.Count == 0) return question;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _conversationLog.Count; i++) sb.AppendLine(_conversationLog[i]);
            sb.Append("Q: ").Append(question);
            return sb.ToString();
        }

        // ─── Speech ─────────────────────────────────────────────────────
        private IEnumerator SpeakLine(string text)
        {
            ShowSubtitle(text);
            _professor?.SpeakingPulse(EstimateLineDuration(text));

            Debug.Log($"[ProfessorTutorial] SpeakLine: '{text}' | hasConfig={config != null} | hasKey={(config != null && !string.IsNullOrEmpty(config.elevenLabsApiKey))}");

            // Mute the mic while we speak so the speaker output doesn't loop
            // back into the player's mic and get transcribed as a fake
            // question. We unmute with a small buffer after the audio
            // finishes to let speaker decay die out.
            if (voiceRouter != null) voiceRouter.SetMuted(true);

            if (config == null || string.IsNullOrEmpty(config.elevenLabsApiKey))
            {
                Debug.LogWarning("[ProfessorTutorial] elevenLabsApiKey missing — Professor will be silent (subtitle only). Set BackendConfig.elevenLabsApiKey in Inspector.");
                float hold = Mathf.Clamp(EstimateLineDuration(text), 1.5f, 6f);
                yield return new WaitForSeconds(hold);
                if (voiceRouter != null) voiceRouter.SetMuted(false);
                yield break;
            }

            // Try the requested voice first. If it returns JSON instead of
            // MP3 (e.g., voice not in this account's library), fall back to
            // Rachel which is on every ElevenLabs free key.
            string primaryVoice = !string.IsNullOrEmpty(overrideVoiceId)
                ? overrideVoiceId
                : (string.IsNullOrEmpty(config.elevenLabsTtsVoiceId) ? FallbackVoice : config.elevenLabsTtsVoiceId);

            yield return TryTts(primaryVoice, text, true);

            // 0.4s buffer after speech ends so any speaker reverberation /
            // queued audio device flush is gone before we re-open the mic.
            yield return new WaitForSeconds(0.4f);
            if (voiceRouter != null) voiceRouter.SetMuted(false);
        }

        // Inner TTS attempt. On HTTP error or empty/zero-length clip and
        // tryFallback==true, retries with the Rachel default voice.
        // Uses DownloadHandlerAudioClip directly (as Announcer.cs does)
        // so the clip is fully loaded by the time PlayOneShot fires.
        private IEnumerator TryTts(string voiceId, string text, bool tryFallback)
        {
            string url  = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
            string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { text = text, model_id = ttsModelId });

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
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
                    Debug.LogWarning($"[ProfessorTutorial] TTS HTTP {req.responseCode} voiceId={voiceId} {req.error}");
                    if (tryFallback && voiceId != FallbackVoice)
                    {
                        Debug.Log($"[ProfessorTutorial] Retrying with Rachel ({FallbackVoice})...");
                        yield return TryTts(FallbackVoice, text, false);
                        yield break;
                    }
                    yield return new WaitForSeconds(Mathf.Clamp(EstimateLineDuration(text), 1.5f, 6f));
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.length < 0.05f || clip.samples == 0)
                {
                    Debug.LogWarning($"[ProfessorTutorial] TTS clip invalid (decode failed or voice not in library). voiceId={voiceId} clipNull={(clip == null)} len={(clip != null ? clip.length : -1)} samples={(clip != null ? clip.samples : -1)}");
                    if (tryFallback && voiceId != FallbackVoice)
                    {
                        Debug.Log($"[ProfessorTutorial] Falling back to Rachel ({FallbackVoice})...");
                        yield return TryTts(FallbackVoice, text, false);
                        yield break;
                    }
                    yield return new WaitForSeconds(Mathf.Clamp(EstimateLineDuration(text), 1.5f, 6f));
                    yield break;
                }

                Debug.Log($"[ProfessorTutorial] TTS clip ready: voiceId={voiceId} length={clip.length:F2}s samples={clip.samples} channels={clip.channels} | volume={_audio.volume} loadState={clip.loadState}");
                _audio.PlayOneShot(clip);
                _professor?.SpeakingPulse(clip.length);
                yield return new WaitForSeconds(clip.length);
            }
        }

        private bool IsListenerNearby()
        {
            var listener = FindFirstObjectByType<AudioListener>();
            if (listener == null) return false;
            float dist = Vector3.Distance(listener.transform.position, _audio != null ? _audio.transform.position : transform.position);
            return dist < 25f;
        }

        private void ShowSubtitle(string text)
        {
            if (subtitleLabel != null) subtitleLabel.text = text ?? "";
        }

        private static float EstimateLineDuration(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1.5f;
            int words = text.Split(' ').Length;
            return Mathf.Max(1.5f, words / 2.3f);
        }
    }
}
