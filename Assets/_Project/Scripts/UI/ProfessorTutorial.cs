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
        [Tooltip("Vertical offset added to the practice dummy spawn so the FBX (whose pivot sits at the model's centre) stands on the floor instead of half-buried in it.")]
        [SerializeField] private float dummyVerticalOffset = 0.9f;
        [Tooltip("Vertical offset added to the borrowed scribble after auto-scaling so it floats at chest height instead of clipping the floor.")]
        [SerializeField] private float borrowedScribbleVerticalOffset = 0.7f;

        [Header("Behaviour")]
        [SerializeField] private bool allowQandA = true;
        private RPGDialogueBox _dialogueBox;

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
            "You are Professor Pastel, a kindly elderly paper-craft wizard who teaches new trainers " +
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
            "Welcome to the world of Scribble Showdown! I'm Professor Pastel.",
            "Your egg is still hatching, so why don't I lend you one of my old scribbles to teach you the ropes.",
            "Combat in this world is turn based, so you and your opponent take turns making a move.",
            "And here's the magical part. Every attack is voice activated. You simply say the move out loud.",
            "Let's give it a try. Aim at that practice dummy and shout, THUNDER BOLT!",
            "Beautiful! Just keep an eye on your scribble's HP, and you'll be just fine. Got any questions before we begin?"
        };

        private const int LineIdx_LendScribble = 1;
        private const int LineIdx_PracticeCue  = 4;
        private const int LineIdx_Wrapup       = 5;

        /// <summary>
        /// Fires once when the tutorial's GameObject is destroyed (whether via
        /// Stop, BeginLeave's natural finish, or external teardown). The title
        /// screen subscribes to this so it can mark the tutorial complete and
        /// ungray the PLAY button.
        /// </summary>
        public event Action OnTutorialFinished;

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
                voiceRouter.OnTranscript.RemoveListener(OnGuidedMoveTranscript);
                voiceRouter.OnMoveCast.RemoveListener(OnContinuousPracticeMoveCast);
                // Don't gag the mic on tear-down — if SpeakLine was mid-flight
                // when the tutorial got destroyed, its SetMuted(false) never
                // ran and battle would inherit a permanently-muted mic.
                voiceRouter.SetMuted(false);
                // Leave open-mic state alone here — GameStateManager.RunBattlePhase
                // will explicitly turn it back on for combat. Setting false here
                // would create a brief push-to-talk window between tutorial end
                // and battle start that we don't actually want.
            }
            if (_practiceHud != null)
            {
                Destroy(_practiceHud.gameObject);
                _practiceHud = null;
            }
            _dummy = null;
            _borrowedScribble = null;

            // Notify any listeners (TitleScreen) that the tutorial is finished.
            // Fires for every teardown path: graceful BeginLeave, abrupt Stop,
            // or scene-load destruction.
            try { OnTutorialFinished?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
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
            if (voiceRouter != null)
            {
                if (_continuousPractice)
                {
                    voiceRouter.OnMoveCast.RemoveListener(OnContinuousPracticeMoveCast);
                    _continuousPractice = false;
                }
                // Defensive — clear any guided-tutorial listener too in case
                // we leave mid-window.
                voiceRouter.OnTranscript.RemoveListener(OnGuidedMoveTranscript);
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

            if (_dialogueBox == null)
            {
                // RPG-style dialogue box (panel + portrait + speaker header +
                // body), parented to the professor so it tracks any bob/sway.
                // Initialize() positions it at localY 2.4 (well above the hat,
                // hat top sits at y=1.47), and the script billboards each
                // LateUpdate to face the player.
                var dialogueGo = new GameObject("ProfessorDialogue");
                _dialogueBox = dialogueGo.AddComponent<RPGDialogueBox>();
                _dialogueBox.Initialize(profGo.transform, "Professor Pastel",
                    Resources.Load<Texture2D>("face"));
                _dialogueBox.Hide();
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
            // the player toward the dummy.) The vertical offset lifts the
            // model off the floor — _stageCenter sits at floor height so the
            // GLB pivot would otherwise spawn the body half-buried.
            container.transform.position = _stageCenter - _stageForward * 0.5f + Vector3.up * borrowedScribbleVerticalOffset;
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
            Debug.Log($"[ProfessorTutorial] Borrowed scribble spawned at {container.transform.position} scale={container.transform.localScale.x:F2} childCount={container.transform.childCount}");
        }

        private void SpawnDummy()
        {
            if (_dummy != null) return;
            // Practice dummy is the static CharacterBase FBX, sitting 1.7m
            // further from the player than the Professor.
            var root = new GameObject("PracticeDummy");
            root.transform.SetParent(transform, worldPositionStays: true);
            // CharacterBase FBX has its pivot at the model centre, not at the
            // feet, so without the dummyVerticalOffset bump the dummy renders
            // half-buried in the floor.
            root.transform.position = _stageCenter - _stageForward * 1.7f + Vector3.up * dummyVerticalOffset;
            if (_stageForward.sqrMagnitude > 1e-4f)
                root.transform.rotation = Quaternion.LookRotation(_stageForward, Vector3.up);
            else
                root.transform.rotation = Quaternion.Euler(0, 90f, 0);

            var prefab = Resources.Load<GameObject>("Characters/CharacterBase");
            if (prefab != null)
            {
                var inst = Instantiate(prefab, root.transform);
                inst.name = "CharacterBase";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Debug.LogError("[ProfessorTutorial] Missing Resources/Characters/CharacterBase.prefab");
            }

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

            // Guided tour through other moves — professor names each one,
            // player shouts it, custom animation plays. Falls through into
            // freeform practice at the end.
            if (!_stopRequested) yield return RunGuidedMoveTutorial();
        }

        // ─── Guided move tutorial ───────────────────────────────────────
        // After the scripted thunderbolt, the professor walks the player
        // through 4 more moves — calling each by name, listening for the
        // player to shout it, and firing a unique animation. If the player
        // doesn't say it within the listen window the professor demos it
        // anyway so the player still SEES what each move looks like.
        private IEnumerator RunGuidedMoveTutorial()
        {
            // Make sure the dummy is still standing (it should be — we no
            // longer destroy it in PerformThunderBolt).
            if (_dummy == null) SpawnDummy();

            var sequence = new (string moveKey, string promptLine)[]
            {
                ("fireball",  "Now try a Fireball! Just shout the name."),
                ("water gun", "Beautifully done. Try Water Gun next."),
                ("ice shard", "You're a natural. How about an Ice Shard?"),
                ("leaf blade","Try Leaf Blade. It's a quick slash."),
            };

            for (int i = 0; i < sequence.Length; i++)
            {
                if (_stopRequested) yield break;
                var entry = sequence[i];
                yield return SpeakLine(entry.promptLine);
                if (_stopRequested) yield break;
                yield return RunSpecificMoveListenWindow(entry.moveKey, 12f);
            }

            if (_stopRequested) yield break;
            yield return SpeakLine("You've got the basics! Keep practicing on the dummy. Just shout any move name.");
        }

        // One-shot listening window for a SPECIFIC move keyword. If the
        // player says the move within listenSec, we fire its custom anim.
        // If they don't, the professor says a soft prompt and demos the
        // move himself so the player still gets to see it.
        private string _guidedMoveExpected;
        private bool   _guidedMoveTriggered;

        private IEnumerator RunSpecificMoveListenWindow(string moveKey, float listenSec)
        {
            if (string.IsNullOrEmpty(moveKey)) yield break;

            _guidedMoveExpected  = moveKey.ToLowerInvariant();
            _guidedMoveTriggered = false;

            string subtitleName = moveKey.ToUpperInvariant();
            ShowSubtitle($"(Shout: {subtitleName}!)");

            bool listening = false;
            if (voiceRouter != null)
            {
                voiceRouter.OnTranscript.AddListener(OnGuidedMoveTranscript);
                listening = true;
            }

            float t = 0f;
            while (t < listenSec && !_guidedMoveTriggered && !_stopRequested)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (listening && voiceRouter != null)
            {
                voiceRouter.OnTranscript.RemoveListener(OnGuidedMoveTranscript);
            }

            if (_stopRequested) yield break;

            if (!_guidedMoveTriggered)
            {
                yield return SpeakLine("That's alright, let me show you.");
            }

            // Fire the custom animation either way (player-cast or demo).
            yield return PlayMoveAnimation(moveKey);
        }

        private void OnGuidedMoveTranscript(string transcript)
        {
            if (_guidedMoveTriggered) return;
            if (string.IsNullOrEmpty(transcript) || string.IsNullOrEmpty(_guidedMoveExpected)) return;
            string lower = transcript.ToLowerInvariant();

            // Match against the expected key. Accept both spaced and
            // run-together variants so "fireball" / "fire ball" / "ice
            // shard" / "iceshard" all hit.
            string spaced  = _guidedMoveExpected;
            string crushed = spaced.Replace(" ", "");

            bool match = lower.Contains(spaced) || lower.Contains(crushed);

            // Per-move synonym fallbacks — voice transcripts are often
            // missing the second word. Lenient matching here matches the
            // existing thunderbolt handler's spirit.
            if (!match)
            {
                switch (crushed)
                {
                    case "fireball":  match = lower.Contains("fire");  break;
                    case "watergun":  match = lower.Contains("water"); break;
                    case "iceshard":  match = lower.Contains("ice");   break;
                    case "leafblade": match = lower.Contains("leaf") || lower.Contains("blade"); break;
                }
            }

            if (match) _guidedMoveTriggered = true;
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

            // Dummy reels but STAYS STANDING. No fall-over, no respawn —
            // the player will keep practicing on this same dummy through
            // the guided tutorial and the freeform continuous practice.
            yield return PunchScale(_dummy, 0.18f, 0.15f);

            yield return new WaitForSeconds(0.2f);
        }

        // Reusable scale-punch hit reaction. Dummy stays standing.
        private IEnumerator PunchScale(GameObject target, float intensity, float duration)
        {
            if (target == null) yield break;
            Vector3 startScale = target.transform.localScale;
            float t = 0f;
            while (t < duration && target != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin((t / duration) * Mathf.PI);
                target.transform.localScale = startScale * (1f + intensity * k);
                yield return null;
            }
            if (target != null) target.transform.localScale = startScale;
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
            // Dummy should already be standing from the practice + guided
            // sequence (the dummy never dies anymore). This is just a
            // defensive guard in case it was somehow torn down.
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
        // bound moveset's triggerPhrases — we just react with the move's
        // custom animation. The dummy NEVER dies in practice mode.
        private void OnContinuousPracticeMoveCast(MoveSO move)
        {
            if (!_continuousPractice || _stopRequested) return;
            if (move == null || _dummy == null) return;

            Debug.Log($"[ProfessorTutorial] Practice hit: '{move.displayName}' (element={move.element})");
            StartCoroutine(PlayMoveAnimation(move.displayName));
        }

        // Generic fallback when the cast move has no custom animation.
        // Uses the element-color burst we already had.
        private IEnumerator PerformGenericBurst(string moveKey)
        {
            if (_dummy == null) yield break;
            // Best-effort element lookup so the burst color still matches.
            Color color = Color.white;
            var catalog = MoveCatalog.Instance;
            if (catalog != null && !string.IsNullOrEmpty(moveKey))
            {
                var m = catalog.Find(moveKey);
                if (m != null) color = BurstColorForElement(m.element);
            }
            SpawnColoredBurst(color);
            yield return PunchScale(_dummy, 0.18f, 0.15f);
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

        // ─── Per-move custom animations ─────────────────────────────────
        // Central dispatcher used by both the guided tutorial AND freeform
        // continuous practice. Each case fires a unique procedural anim;
        // the default falls back to the colored burst.
        private IEnumerator PlayMoveAnimation(string moveKey)
        {
            string k = (moveKey ?? "").ToLowerInvariant().Trim();
            switch (k)
            {
                case "fireball":     yield return PerformFireball();    break;
                case "water gun":
                case "watergun":     yield return PerformWaterGun();    break;
                case "ice shard":
                case "iceshard":     yield return PerformIceShard();    break;
                case "leaf blade":
                case "leafblade":    yield return PerformLeafBlade();   break;
                case "thunder bolt":
                case "thunderbolt":  yield return PerformThunderBolt(); break;
                default:             yield return PerformGenericBurst(k); break;
            }
        }

        // Tiny wind-up jump on the borrowed scribble. Used as the lead-in
        // for every per-move animation. Crouch + spring + back to rest.
        private IEnumerator ScribbleWindup(float duration, float jumpHeight, float crouch)
        {
            if (_borrowedScribble == null) yield break;
            Vector3 startPos = _borrowedScribble.transform.localPosition;
            Vector3 startScale = _borrowedScribble.transform.localScale;
            float t = 0f;
            while (t < duration && _borrowedScribble != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                // First half: crouch (squash). Second half: jump (release).
                float crouchK = k < 0.5f ? Mathf.Sin(k * Mathf.PI) : 0f;
                float jumpK   = k >= 0.5f ? Mathf.Sin((k - 0.5f) * 2f * Mathf.PI) : 0f;
                _borrowedScribble.transform.localPosition = startPos + new Vector3(0, jumpHeight * jumpK, 0);
                _borrowedScribble.transform.localScale = new Vector3(
                    startScale.x * (1f + 0.10f * crouchK),
                    startScale.y * (1f - crouch * crouchK),
                    startScale.z * (1f + 0.10f * crouchK));
                yield return null;
            }
            if (_borrowedScribble != null)
            {
                _borrowedScribble.transform.localPosition = startPos;
                _borrowedScribble.transform.localScale    = startScale;
            }
        }

        // Returns the world-space "muzzle" point on the borrowed scribble
        // and the world-space "hit" point on the dummy. Used by every anim.
        private bool TryGetCastEndpoints(out Vector3 origin, out Vector3 target)
        {
            origin = Vector3.zero;
            target = Vector3.zero;
            if (_dummy == null) return false;
            origin = _borrowedScribble != null
                ? _borrowedScribble.transform.position + Vector3.up * 0.4f
                : _stageCenter - _stageForward * 0.5f + Vector3.up * 0.5f;
            target = _dummy.transform.position + Vector3.up * 0.4f;
            return true;
        }

        // Builds an empty FX GameObject at `origin` aimed at `target`,
        // with a fresh ParticleSystem + Unlit particle material in the
        // requested color. The caller configures emission/shape/etc.
        private (GameObject go, ParticleSystem ps, ParticleSystemRenderer psr, Material mat) BuildFx(
            string name, Vector3 origin, Vector3 target, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = origin;
            Vector3 dir = target - origin;
            if (dir.sqrMagnitude > 1e-6f)
                go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            var ps  = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            psr.sharedMaterial = mat;
            return (go, ps, psr, mat);
        }

        // FIREBALL — two-stage: glowing orb travels scribble->dummy, then
        // explodes into a ring of orange/red sparks on impact. Sphere-shape
        // burst (not stretched like lightning).
        private IEnumerator PerformFireball()
        {
            if (!TryGetCastEndpoints(out var origin, out var target)) yield break;

            yield return ScribbleWindup(0.30f, 0.05f, 0.18f);
            if (!TryGetCastEndpoints(out origin, out target)) yield break;

            Color hot   = new Color(1.0f, 0.55f, 0.15f);
            Color core  = new Color(1.0f, 0.85f, 0.40f);

            // Stage 1: orb (a small lit sphere) travels from origin to target.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "FireOrb";
            var col = orb.GetComponent<Collider>(); if (col != null) Destroy(col);
            orb.transform.SetParent(transform, worldPositionStays: true);
            orb.transform.position = origin;
            orb.transform.localScale = Vector3.one * 0.18f;
            var orbSh = Shader.Find("Universal Render Pipeline/Lit");
            var orbMat = new Material(orbSh);
            if (orbMat.HasProperty("_BaseColor")) orbMat.SetColor("_BaseColor", core);
            if (orbMat.HasProperty("_EmissionColor")) { orbMat.EnableKeyword("_EMISSION"); orbMat.SetColor("_EmissionColor", hot * 4f); }
            orb.GetComponent<Renderer>().sharedMaterial = orbMat;

            // Trailing embers behind the orb (sphere shape, billboard).
            var trail = BuildFx("FireballTrail", origin, target, hot);
            {
                var main = trail.ps.main;
                main.playOnAwake = false;
                main.duration = 0.6f; main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.45f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.startColor    = new ParticleSystem.MinMaxGradient(hot, core);
                main.maxParticles  = 80;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                trail.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = trail.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 90f;
                var shape = trail.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.08f;
                trail.ps.Play();
            }

            float travelDur = 0.40f, t = 0f;
            while (t < travelDur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / travelDur);
                Vector3 pos = Vector3.Lerp(origin, target, k);
                orb.transform.position = pos;
                if (trail.go != null) trail.go.transform.position = pos;
                yield return null;
            }

            if (trail.go != null) Destroy(trail.go, 0.4f);
            Destroy(orb);

            // Stage 2: explosion ring at target — donut-ish radial burst.
            var burst = BuildFx("FireballBurst", target, target + Vector3.up, hot);
            {
                var main = burst.ps.main;
                main.playOnAwake = false;
                main.duration = 0.30f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(2.5f, 5.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
                main.startColor    = new ParticleSystem.MinMaxGradient(hot, core);
                main.maxParticles  = 140;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                burst.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = burst.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 100) });
                var shape = burst.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
                burst.ps.Play();
                Destroy(burst.go, 1.2f);
            }

            // Hit reaction — dummy stays standing.
            yield return PunchScale(_dummy, 0.20f, 0.18f);
        }

        // WATER GUN — wide cone-shaped continuous spray, longer particle
        // lifetime so it reads as a hose rather than a flash.
        private IEnumerator PerformWaterGun()
        {
            if (!TryGetCastEndpoints(out var origin, out var target)) yield break;

            // Wind-up: scribble rears back (small backward sway).
            if (_borrowedScribble != null)
            {
                Vector3 startPos = _borrowedScribble.transform.localPosition;
                Vector3 backDir  = -_borrowedScribble.transform.forward * 0.06f;
                float t0 = 0f, dur0 = 0.22f;
                while (t0 < dur0 && _borrowedScribble != null)
                {
                    t0 += Time.deltaTime;
                    float k = Mathf.Sin(Mathf.Clamp01(t0 / dur0) * Mathf.PI);
                    _borrowedScribble.transform.localPosition = startPos + backDir * k;
                    yield return null;
                }
                if (_borrowedScribble != null) _borrowedScribble.transform.localPosition = startPos;
            }
            if (!TryGetCastEndpoints(out origin, out target)) yield break;

            Color cold = new Color(0.30f, 0.65f, 1.00f);
            Color foam = new Color(0.85f, 0.95f, 1.00f);

            // Continuous spray cone (wide, slower particles, long lifetime).
            var spray = BuildFx("WaterSpray", origin, target, cold);
            {
                var main = spray.ps.main;
                main.playOnAwake = false;
                main.duration = 0.55f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.startColor    = new ParticleSystem.MinMaxGradient(cold, foam);
                main.maxParticles  = 220;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.4f;
                spray.psr.renderMode = ParticleSystemRenderMode.Stretch;
                spray.psr.lengthScale = 2.5f;
                spray.psr.velocityScale = 0.25f;
                var emission = spray.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 220f;
                var shape = spray.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle  = 18f;   // wide cone
                shape.radius = 0.05f;
                spray.ps.Play();
                Destroy(spray.go, 1.6f);
            }

            // Wait for the spray to actually reach the dummy before the hit.
            yield return new WaitForSeconds(0.35f);

            // Splash burst: outward ring at the dummy.
            var splash = BuildFx("WaterSplash", target, target + Vector3.up, foam);
            {
                var main = splash.ps.main;
                main.playOnAwake = false;
                main.duration = 0.30f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(2.5f, 4.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                main.startColor    = new ParticleSystem.MinMaxGradient(foam, cold);
                main.maxParticles  = 120;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.6f;
                splash.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = splash.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
                var shape = splash.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Donut;
                shape.radius = 0.18f;
                shape.donutRadius = 0.04f;
                splash.ps.Play();
                Destroy(splash.go, 1.4f);
            }

            yield return PunchScale(_dummy, 0.16f, 0.16f);
        }

        // ICE SHARD — angular stretched-particle "shards" flying scribble
        // -> dummy, then a short-life cubic shatter burst on impact.
        private IEnumerator PerformIceShard()
        {
            if (!TryGetCastEndpoints(out var origin, out var target)) yield break;

            // Wind-up: brief pale-cyan scale pulse on the scribble.
            if (_borrowedScribble != null)
            {
                Vector3 startScale = _borrowedScribble.transform.localScale;
                float t0 = 0f, dur0 = 0.22f;
                while (t0 < dur0 && _borrowedScribble != null)
                {
                    t0 += Time.deltaTime;
                    float k = Mathf.Sin(Mathf.Clamp01(t0 / dur0) * Mathf.PI);
                    _borrowedScribble.transform.localScale = startScale * (1f + 0.06f * k);
                    yield return null;
                }
                if (_borrowedScribble != null) _borrowedScribble.transform.localScale = startScale;
            }
            if (!TryGetCastEndpoints(out origin, out target)) yield break;

            Color icePale = new Color(0.80f, 0.95f, 1.00f);
            Color iceDeep = new Color(0.45f, 0.75f, 0.95f);

            // Travel: a few angular stretched shards thrown in a tight cone.
            var shards = BuildFx("IceShards", origin, target, icePale);
            {
                var main = shards.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(7.5f, 10f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                main.startColor    = new ParticleSystem.MinMaxGradient(icePale, iceDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 30;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                shards.psr.renderMode  = ParticleSystemRenderMode.Stretch;
                shards.psr.lengthScale = 5f;
                shards.psr.velocityScale = 0.4f;
                var emission = shards.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
                var shape = shards.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle  = 4f;
                shape.radius = 0.04f;
                shards.ps.Play();
                Destroy(shards.go, 1.0f);
            }

            yield return new WaitForSeconds(0.18f);

            // Impact: jagged shatter — short-life cubic particles spraying
            // outward in all directions.
            var shatter = BuildFx("IceShatter", target, target + Vector3.up, icePale);
            {
                var main = shatter.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(3.5f, 6.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                main.startColor    = new ParticleSystem.MinMaxGradient(icePale, iceDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 80;
                main.gravityModifier = 0.8f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                shatter.psr.renderMode  = ParticleSystemRenderMode.Stretch;
                shatter.psr.lengthScale = 1.5f;
                shatter.psr.velocityScale = 0.6f;
                var emission = shatter.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
                var shape = shatter.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.02f;
                shatter.ps.Play();
                Destroy(shatter.go, 1.2f);
            }

            yield return PunchScale(_dummy, 0.18f, 0.16f);
        }

        // LEAF BLADE — fast green slash: one quick stretched-particle line
        // sweeping scribble -> dummy, then a green sparkle puff at impact.
        private IEnumerator PerformLeafBlade()
        {
            if (!TryGetCastEndpoints(out var origin, out var target)) yield break;

            yield return ScribbleWindup(0.18f, 0.04f, 0.20f);
            if (!TryGetCastEndpoints(out origin, out target)) yield break;

            Color leafBright = new Color(0.55f, 0.95f, 0.40f);
            Color leafDeep   = new Color(0.20f, 0.55f, 0.20f);

            // Slash streak — single fast stretched line aimed at the dummy.
            var slash = BuildFx("LeafSlash", origin, target, leafBright);
            {
                var main = slash.ps.main;
                main.playOnAwake = false;
                main.duration = 0.10f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(14f, 18f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                main.startColor    = new ParticleSystem.MinMaxGradient(leafBright, leafDeep);
                main.maxParticles  = 30;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                slash.psr.renderMode  = ParticleSystemRenderMode.Stretch;
                slash.psr.lengthScale = 9f;     // long line
                slash.psr.velocityScale = 0.6f;
                var emission = slash.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
                var shape = slash.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle  = 1.5f;   // razor-thin line, almost a beam
                shape.radius = 0.02f;
                slash.ps.Play();
                Destroy(slash.go, 0.8f);
            }

            yield return new WaitForSeconds(0.12f);

            // Impact: green sparkle puff (small omnidirectional billboard).
            var puff = BuildFx("LeafPuff", target, target + Vector3.up, leafBright);
            {
                var main = puff.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
                main.startColor    = new ParticleSystem.MinMaxGradient(leafBright, leafDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 80;
                main.gravityModifier = 0.2f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                puff.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = puff.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });
                var shape = puff.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
                puff.ps.Play();
                Destroy(puff.go, 1.2f);
            }

            yield return PunchScale(_dummy, 0.16f, 0.14f);
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
            if (_dialogueBox == null) return;
            if (string.IsNullOrEmpty(text)) { _dialogueBox.Hide(); return; }
            _dialogueBox.Show();
            // Stage directions like "(Listening — ask me anything!)" come
            // wrapped in parentheses; render those without portrait/header
            // and in italics. Speech lines render as full RPG-style cards.
            if (text.Length >= 2 && text[0] == '(' && text[text.Length - 1] == ')')
                _dialogueBox.SetStageDirection(text);
            else
                _dialogueBox.SetText(text);
        }

        private static float EstimateLineDuration(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1.5f;
            int words = text.Split(' ').Length;
            return Mathf.Max(1.5f, words / 2.3f);
        }
    }
}
