using System;
using System.Collections;
using System.Collections.Generic;
using GLTFast;
using Tigerverse.Combat;
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
        private bool           _continuousPractice;
        private GameObject     _borrowedScribble;
        private GameObject     _dummy;
        private BattleHUD      _practiceHud;
        private readonly Queue<string> _pendingQuestions = new Queue<string>();
        private readonly List<string>  _conversationLog = new List<string>();

        // Cached "stage" placement, computed once at BuildScene() time so it
        // doesn't drift when the player moves their head. Stage = a small
        // area ~3.5m in front of the player where the Professor + dummy +
        // borrowed scribble all live. World-space.
        private Vector3 _stageCenter;
        private Vector3 _stageForward; // points from stage toward the player (so professor faces this)
        private Vector3 _stageLeft;    // 90° from forward, "left of professor from the player's POV"

        // Burst color for a move's element — used by the continuous-practice
        // OnMoveCast handler to pick particle hues per move type. Mirrors the
        // saturated/punchy palette the original keyword table used (the
        // shared ElementType.ToColor() palette is muted for HUD use).
        private static Color BurstColorForElement(ElementType e)
        {
            switch (e)
            {
                case ElementType.Electric: return new Color(1f,    0.95f, 0.40f);
                case ElementType.Fire:     return new Color(1f,    0.55f, 0.20f);
                case ElementType.Water:    return new Color(0.30f, 0.65f, 1.00f);
                case ElementType.Ice:      return new Color(0.75f, 0.95f, 1.00f);
                case ElementType.Grass:    return new Color(0.45f, 0.85f, 0.40f);
                case ElementType.Earth:    return new Color(0.60f, 0.45f, 0.30f);
                case ElementType.Dark:     return new Color(0.45f, 0.25f, 0.65f);
                case ElementType.Neutral:
                default:                   return new Color(1.00f, 1.00f, 1.00f);
            }
        }

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
                voiceRouter.OnTranscript.RemoveListener(OnPracticeTranscript);
                voiceRouter.OnMoveCast.RemoveListener(OnContinuousPracticeMoveCast);
                voiceRouter.SetOpenMicMode(false); // restore push-to-talk for combat
            }
            if (_practiceHud != null)
            {
                Destroy(_practiceHud.gameObject);
                _practiceHud = null;
            }
        }

        public void Stop()
        {
            if (_stopRequested) return;
            _stopRequested = true;
            // Tear the whole tutorial GameObject down on Stop so the
            // Professor, his subtitle, the practice dummy, the borrowed
            // scribble, and any spawned FX all disappear when the egg
            // hands off to the hatch sequence. Without this, the
            // Professor would float next to the hatched monster forever.
            if (gameObject != null) Destroy(gameObject);
        }

        /// <summary>
        /// Graceful exit: speaks a goodbye line, plays the Professor's leave
        /// animation, then destroys the whole tutorial. Preferred over Stop()
        /// once both players' eggs are ready — the user gets a clean send-off
        /// instead of an abrupt disappearance. Stop() is still used for
        /// emergencies (e.g. the local egg hatched first).
        /// </summary>
        public void BeginLeave()
        {
            if (_stopRequested) return;
            _stopRequested = true;
            StartCoroutine(LeaveSequence());
        }

        private IEnumerator LeaveSequence()
        {
            // Stop continuous practice listeners immediately.
            if (_continuousPractice && voiceRouter != null)
            {
                voiceRouter.OnMoveCast.RemoveListener(OnContinuousPracticeMoveCast);
                _continuousPractice = false;
            }
            if (_practiceHud != null)
            {
                Destroy(_practiceHud.gameObject);
                _practiceHud = null;
            }

            // Speak a goodbye line (uses SpeakLine directly, NOT the
            // _stopRequested-gated paths in RunTutorial — we want this line
            // to play even though we just set _stopRequested = true).
            yield return SpeakLine("Looks like both eggs are ready! Good luck, trainer.");

            // Tell the professor to do its leave animation.
            if (_professor != null) yield return _professor.PlayLeaveAnimation();

            // Then destroy ourselves so the dummy / borrowed scribble /
            // subtitle / professor all disappear together.
            if (gameObject != null) Destroy(gameObject);
        }

        // ─── Scene build ────────────────────────────────────────────────
        private void BuildScene()
        {
            // Compute a "stage" position ~3.5m in front of the local camera
            // at the slot's floor height. This puts the Professor + dummy +
            // borrowed scribble in a clear area away from the eggs, instead
            // of right next to them where they used to block the view.
            ComputeStageTransform();

            var profGo = new GameObject("PaperProfessor");
            // Keep the GameObject parented so destruction cascades, but use
            // a WORLD-space position so we're not fighting whatever rotation
            // / scaling lives on the slot pivot's parent chain.
            profGo.transform.SetParent(transform, worldPositionStays: true);
            profGo.transform.position = _stageCenter;
            // Face the player. _stageForward points stage->player on Y plane.
            if (_stageForward.sqrMagnitude > 1e-4f)
                profGo.transform.rotation = Quaternion.LookRotation(_stageForward, Vector3.up);
            else
                profGo.transform.rotation = Quaternion.Euler(0, professorYawDeg, 0);
            _professor = profGo.AddComponent<PaperProfessor>();
            // Pop-in animation handled by PaperProfessor (added by another agent).
            StartCoroutine(_professor.PlaySpawnAnimation());

            if (subtitleLabel == null)
            {
                var lblGo = new GameObject("ProfessorSubtitle");
                // Subtitle stays parented to the professor so it tracks any
                // bobbing / movement the figure has during speech.
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

        // Picks the stage center / orientation ONCE at tutorial start, so
        // none of it drifts when the player turns their head later.
        private void ComputeStageTransform()
        {
            var cam = Camera.main;
            float floorY = transform.position.y; // slot's floor height
            if (cam != null)
            {
                Vector3 camFwd = cam.transform.forward;
                camFwd.y = 0f;
                if (camFwd.sqrMagnitude < 1e-4f) camFwd = Vector3.forward;
                camFwd.Normalize();

                _stageCenter  = cam.transform.position + camFwd * 3.5f;
                _stageCenter.y = floorY;
                _stageForward = -camFwd; // from stage back toward player
                _stageLeft    = Vector3.Cross(Vector3.up, _stageForward).normalized;
            }
            else
            {
                // No camera (editor / headless) — fall back to slot-local
                // offset so we still place the stage somewhere reasonable.
                _stageCenter  = transform.position + transform.TransformVector(professorOffset);
                _stageCenter.y = floorY;
                _stageForward = -transform.forward;
                _stageLeft    = Vector3.Cross(Vector3.up, _stageForward).normalized;
            }
        }

        // ─── Tutorial flow ──────────────────────────────────────────────
        private IEnumerator RunTutorial()
        {
            // Kick off the borrowed-scribble GLB download in parallel so it
            // is ready by the time we actually need it for the practice beat.
            StartCoroutine(LoadBorrowedScribble());

            // Prefetch ALL scripted-line audio in parallel up front. Without
            // this, every new line incurred a fresh ElevenLabs round-trip
            // (~1-3s) that the player perceived as a "freeze" between lines.
            // Now line N+1's audio is fetching while line N is still playing.
            var prefetched = new AudioClip[ScriptedLines.Length];
            for (int i = 0; i < ScriptedLines.Length; i++)
            {
                int captured = i;
                StartCoroutine(PrefetchTtsLine(ScriptedLines[captured], clip => prefetched[captured] = clip));
            }

            for (int i = 0; i < ScriptedLines.Length; i++)
            {
                if (_stopRequested) yield break;

                yield return SpeakLineWithPrefetch(ScriptedLines[i], () => prefetched[i]);
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

                // Q&A loop runs UNTIL the player stops asking. We don't
                // wait for _stopRequested here anymore — once the queue
                // goes idle for a few seconds we move on to continuous
                // practice so the player has something to do while waiting
                // on the OTHER player's GLB.
                float idleSec = 0f;
                const float idleTimeoutSec = 12f;
                while (!_stopRequested && idleSec < idleTimeoutSec)
                {
                    if (_pendingQuestions.Count > 0)
                    {
                        idleSec = 0f;
                        string q = _pendingQuestions.Dequeue();
                        yield return AnswerQuestion(q);
                        if (!_stopRequested) ShowSubtitle("(Listening — ask me anything else!)");
                    }
                    else
                    {
                        idleSec += Time.deltaTime;
                    }
                    yield return null;
                }

                voiceRouter.OnTranscript.RemoveListener(OnPlayerSpoke);
                _qaMode = false;
            }

            if (_stopRequested) yield break;

            yield return SpeakLine("Good luck out there, trainer. Your scribble is almost ready.");

            // Continuous practice: keeps the player engaged on the dummy
            // (with simple voice-cast moves) until BeginLeave() / Stop() is
            // invoked when both players' eggs are ready.
            if (!_stopRequested) yield return RunContinuousPractice();
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
            container.transform.SetParent(transform, worldPositionStays: true);
            // Sit BETWEEN the Professor and the dummy along the depth axis,
            // 0.5m further from the player than the Professor. (_stageForward
            // points stage->player, so subtracting it pushes us away from
            // the player toward the dummy.)
            container.transform.position = _stageCenter - _stageForward * 0.5f;
            // The scribble is the "attacker" in this trio — make it face
            // AWAY from the player toward the dummy further forward.
            Vector3 awayFromPlayer = -_stageForward;
            if (awayFromPlayer.sqrMagnitude > 1e-4f)
                container.transform.rotation = Quaternion.LookRotation(awayFromPlayer, Vector3.up);
            else
                container.transform.rotation = Quaternion.Euler(0, 90f, 0);

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
            // + a tiny X face, painted plain white. Sits 1.7m further from
            // the player than the Professor (1.2m further than the borrowed
            // scribble), facing BACK toward the player so its X eyes are
            // visible — and so it reads as the scribble's target.
            var root = new GameObject("PracticeDummy");
            root.transform.SetParent(transform, worldPositionStays: true);
            root.transform.position = _stageCenter - _stageForward * 1.7f;
            // Face back toward the player (and therefore back toward the
            // scribble that's "attacking" it). _stageForward points
            // stage->player, so a LookRotation along _stageForward orients
            // the dummy's forward axis at the player.
            if (_stageForward.sqrMagnitude > 1e-4f)
                root.transform.rotation = Quaternion.LookRotation(_stageForward, Vector3.up);
            else
                root.transform.rotation = Quaternion.Euler(0, 90f, 0);

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

        // ─── Continuous practice ────────────────────────────────────────
        // Runs after the wrap-up line, until BeginLeave/Stop is called.
        // The player can keep voice-casting moves at the dummy; the dummy
        // never dies — it just plays a hit reaction every time. We bind a
        // real practice moveset onto the voice router so the wrist HUD
        // (BattleHUD) shows "Fireball" / "Thunderbolt" / etc instead of
        // placeholders, and the router does all keyword matching itself
        // via the moves' triggerPhrases.
        private IEnumerator RunContinuousPractice()
        {
            // The scripted ThunderBolt demo destroys the dummy. Bring it
            // back so there's something to practice on.
            if (_dummy == null) SpawnDummy();

            // Build a small "starter kit" of MoveSOs and bind them to the
            // voice router so its OnMoveCast event fires whenever the player
            // shouts one of the trigger phrases — and so BattleHUD can
            // read them via voiceRouter.AvailableMoves.
            var practiceMoves = BuildPracticeMoveset();
            if (voiceRouter != null && practiceMoves != null && practiceMoves.Length > 0)
            {
                // No BattleManager during practice — the router won't try to
                // SubmitMove anywhere, it just fires OnMoveCast.
                voiceRouter.Bind(null, 0, practiceMoves);
            }

            // Spawn the wrist HUD so the player can see their practice moves
            // and cooldowns. World-space, NOT parented to the tutorial — the
            // HUD anchors itself to the XR left-controller every frame.
            if (practiceMoves != null && practiceMoves.Length > 0)
            {
                var hudGo = new GameObject("PracticeHUD", typeof(RectTransform));
                hudGo.transform.SetParent(null, worldPositionStays: true);
                _practiceHud = hudGo.AddComponent<BattleHUD>();
                _practiceHud.Configure(voiceRouter, "You");
                // Configure() reads voice.AvailableMoves immediately — the
                // moveset is already bound above so it should pick up the
                // names right away. Update() also auto-refreshes when the
                // moveset reference changes, so we don't need ForceRefresh.
            }

            _continuousPractice = true;
            if (voiceRouter != null)
                voiceRouter.OnMoveCast.AddListener(OnContinuousPracticeMoveCast);

            ShowSubtitle("(Practice freely — call out moves to attack the dummy!)");
            Debug.Log("[ProfessorTutorial] Continuous practice mode active.");

            while (!_stopRequested)
            {
                yield return null;
            }

            if (_continuousPractice && voiceRouter != null)
            {
                voiceRouter.OnMoveCast.RemoveListener(OnContinuousPracticeMoveCast);
            }
            _continuousPractice = false;
        }

        // Picks 4 starter moves out of the global MoveCatalog: Fireball,
        // Thunderbolt, Watergun, Iceshard. Returns null if the catalog
        // isn't loaded so the caller can fail gracefully.
        private MoveSO[] BuildPracticeMoveset()
        {
            var catalog = MoveCatalog.Instance;
            if (catalog == null)
            {
                catalog = Resources.Load<MoveCatalog>("MoveCatalog");
            }
            if (catalog == null)
            {
                Debug.LogWarning("[ProfessorTutorial] MoveCatalog not found in Resources — practice HUD will be empty.");
                return null;
            }

            string[] wanted = { "Fireball", "Thunderbolt", "Watergun", "Iceshard" };
            var picked = new List<MoveSO>(wanted.Length);
            for (int i = 0; i < wanted.Length; i++)
            {
                var m = catalog.Find(wanted[i]);
                if (m != null) picked.Add(m);
            }
            if (picked.Count == 0)
            {
                Debug.LogWarning("[ProfessorTutorial] None of the expected practice moves were found in MoveCatalog.");
                return null;
            }
            return picked.ToArray();
        }

        // VoiceRouter has already done all the keyword matching against the
        // bound moveset's triggerPhrases — we just react with a hit anim +
        // colored particle burst. The dummy NEVER dies in practice mode.
        private void OnContinuousPracticeMoveCast(MoveSO move)
        {
            if (!_continuousPractice || _stopRequested) return;
            if (move == null || _dummy == null) return;

            Color burstColor = BurstColorForElement(move.element);
            Debug.Log($"[ProfessorTutorial] Practice hit: '{move.displayName}' (element={move.element})");
            StartCoroutine(PracticeMoveHit(move.displayName, burstColor));
        }

        private IEnumerator PracticeMoveHit(string moveName, Color fxColor)
        {
            if (_dummy == null) yield break;

            // Particle burst from the borrowed scribble (or stage center if
            // the scribble didn't load) toward the dummy, colored by move.
            SpawnColoredBurst(fxColor);

            // Hit-flash scale punch on the dummy. Same shape as the demo.
            Vector3 startScale = _dummy != null ? _dummy.transform.localScale : Vector3.one;
            float t = 0f, dur = 0.15f;
            while (t < dur && _dummy != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin((t / dur) * Mathf.PI);
                _dummy.transform.localScale = startScale * (1f + 0.18f * k);
                yield return null;
            }
            if (_dummy != null) _dummy.transform.localScale = startScale;
            // Dummy stays standing — no fall, no respawn. Just keep playing
            // hit reactions until the tutorial leaves.
        }

        // Lightning-style burst, but with a parameterized color so different
        // moves look different. Mirrors SpawnLightningEffect's setup.
        private void SpawnColoredBurst(Color color)
        {
            if (_dummy == null) return;

            // Origin: borrowed scribble if present, else somewhere between
            // the stage center and the dummy so the burst still has a clear
            // direction toward the target. Fallback uses the same 0.5m
            // forward offset that LoadBorrowedScribble() applies.
            Vector3 origin = _borrowedScribble != null
                ? _borrowedScribble.transform.position + Vector3.up * 0.4f
                : _stageCenter - _stageForward * 0.5f + Vector3.up * 0.5f;

            var go = new GameObject("PracticeBurstFx");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = origin;

            var ps = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
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
            main.startColor    = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.5f));
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.05f;
            Vector3 toDummy = (_dummy.transform.position + Vector3.up * 0.4f) - go.transform.position;
            if (toDummy.sqrMagnitude > 1e-6f)
                go.transform.rotation = Quaternion.LookRotation(toDummy.normalized, Vector3.up);

            ps.Play();
            Destroy(go, 1.5f);
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

        // ─── Prefetched-line playback ───────────────────────────────────
        // Issued in parallel from RunTutorial so that by the time the
        // Professor finishes line N, line N+1's clip is already decoded
        // and ready to play with no inter-line gap.
        private IEnumerator PrefetchTtsLine(string text, System.Action<AudioClip> onReady)
        {
            if (config == null || string.IsNullOrEmpty(config.elevenLabsApiKey))
            {
                onReady?.Invoke(null);
                yield break;
            }
            string voiceId = !string.IsNullOrEmpty(overrideVoiceId)
                ? overrideVoiceId
                : (string.IsNullOrEmpty(config.elevenLabsTtsVoiceId) ? FallbackVoice : config.elevenLabsTtsVoiceId);

            AudioClip got = null;
            yield return FetchTtsClip(voiceId, text, true, c => got = c);
            onReady?.Invoke(got);
        }

        // SpeakLine variant that uses an already-prefetched clip when
        // available, falling back to a fresh request only if prefetch
        // hasn't returned yet (or returned null).
        private IEnumerator SpeakLineWithPrefetch(string text, System.Func<AudioClip> getClip)
        {
            ShowSubtitle(text);
            _professor?.SpeakingPulse(EstimateLineDuration(text));
            if (voiceRouter != null) voiceRouter.SetMuted(true);

            // Wait briefly for prefetch to finish (it's already in flight),
            // but cap the wait so we never stall if the request died.
            float waited = 0f;
            AudioClip clip = getClip();
            while (clip == null && waited < 4f)
            {
                waited += Time.deltaTime;
                yield return null;
                clip = getClip();
            }

            if (clip == null)
            {
                // Prefetch never delivered — fall back to a one-shot fetch.
                yield return SpeakLine(text);
                yield break;
            }

            _audio.PlayOneShot(clip);
            _professor?.SpeakingPulse(clip.length);
            yield return new WaitForSeconds(clip.length);
            yield return new WaitForSeconds(0.4f);
            if (voiceRouter != null) voiceRouter.SetMuted(false);
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

        // Fetch-only version used by the prefetch path — returns the clip
        // via callback instead of playing it. Same fallback chain as TryTts.
        private IEnumerator FetchTtsClip(string voiceId, string text, bool tryFallback, System.Action<AudioClip> onResult)
        {
            string url  = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
            string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { text = text, model_id = ttsModelId });
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                var dh = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                dh.streamAudio = true;
                dh.compressed  = true;
                req.downloadHandler = dh;
                req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "audio/mpeg");
                req.timeout = 8;

                yield return req.SendWebRequest();

                bool err = req.result != UnityWebRequest.Result.Success || req.responseCode >= 400;
                if (err)
                {
                    Debug.LogWarning($"[ProfessorTutorial] Prefetch HTTP {req.responseCode} voiceId={voiceId} {req.error}");
                    if (tryFallback && voiceId != FallbackVoice)
                    {
                        yield return FetchTtsClip(FallbackVoice, text, false, onResult);
                        yield break;
                    }
                    onResult?.Invoke(null);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.length < 0.05f || clip.samples == 0)
                {
                    if (tryFallback && voiceId != FallbackVoice)
                    {
                        yield return FetchTtsClip(FallbackVoice, text, false, onResult);
                        yield break;
                    }
                    onResult?.Invoke(null);
                    yield break;
                }

                onResult?.Invoke(clip);
            }
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
                // Stream the MP3 + keep it compressed in memory so the
                // synchronous Vorbis/MP3 decode that happens on
                // GetContent() doesn't spike the main thread for 100+ ms
                // every line. Quest is sensitive to this and the user was
                // seeing the whole game freeze per Professor line.
                var dh = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                dh.streamAudio = true;
                dh.compressed  = true;
                req.downloadHandler = dh;
                req.SetRequestHeader("xi-api-key", config.elevenLabsApiKey);
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "audio/mpeg");
                // 30s was way too long — if ElevenLabs hasn't produced
                // anything in 8s the line is dead and we should fall back.
                req.timeout = 8;

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
