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
        [Tooltip("Local fallback test model loaded from Resources/. Used as the borrowed scribble if it can be found; the GLB download is only attempted if this load fails. Default: a generic untextured humanoid that ships with the project.")]
        [SerializeField] private string testScribbleResourcePath = "BorrowedScribble";
        [Tooltip("How long the player has to say THUNDER BOLT before the dummy 'wins' anyway and we move on.")]
        [SerializeField] private float practiceListenWindowSec = 12f;
        [Tooltip("Vertical offset added to the practice dummy spawn so the FBX (whose pivot sits at the model's centre) stands on the floor instead of half-buried in it.")]
        [SerializeField] private float dummyVerticalOffset = 0f;
        [Tooltip("Vertical offset added to the borrowed scribble after auto-scaling so it floats at chest height instead of clipping the floor. Set to 0 to put it flush with the floor.")]
        [SerializeField] private float borrowedScribbleVerticalOffset = 0.45f;
        [Tooltip("Extra yaw rotation (degrees) applied to the borrowed scribble after the auto-face-the-dummy rotation. Use this to flip a GLB whose authored forward axis points the wrong way.")]
        [SerializeField] private float borrowedScribbleYawOffsetDeg = 90f;
        [Tooltip("Extra PITCH rotation (degrees, X-axis) applied to the borrowed scribble.")]
        [SerializeField] private float borrowedScribblePitchOffsetDeg = 90f;
        [Tooltip("Extra ROLL rotation (degrees, Z-axis) applied to the borrowed scribble.")]
        [SerializeField] private float borrowedScribbleRollOffsetDeg = 90f;

        [Header("Behaviour")]
        [SerializeField] private bool allowQandA = true;
        private RPGDialogueBox _dialogueBox;

        [Header("Refs (auto-found if null)")]
        [SerializeField] private VoiceCommandRouter voiceRouter;

        // ─── Runtime ─────────────────────────────────────────────────────
        private PaperProfessor _professor;
        private ProfessorWander _professorWander;
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

        // Hatching eggs we hid on tutorial start so they don't sit in the
        // player's view while the Professor lectures. Restored OnDestroy.
        private System.Collections.Generic.List<GameObject> _hiddenEggs;

        private void Start()
        {
            BuildScene();
            HideTitleSceneEggs();
            // Spawn the full paper-craft scenery layer (skybox, mountains,
            // clouds, lights, lanterns, fairies, flora, doodles, leaves,
            // tree sway, ambient audio). All children are parented to the
            // returned GameObject so OnDestroy below cleans them up.
            _sceneEnhancer = Tigerverse.UI.PaperSceneEnhancer.Spawn(_stageCenter, Camera.main?.transform);
            // Switch the voice router into open-mic mode for the duration of
            // the tutorial so the player can just speak naturally — no
            // push-to-talk friction during practice or Q&A.
            if (voiceRouter != null) voiceRouter.SetOpenMicMode(true);
            StartCoroutine(RunTutorial());
        }

        private GameObject _sceneEnhancer;

        // Hide every HatchingEggSequence in the scene while the tutorial
        // runs (the title scene preview-spawns one on the player's pad
        // and it ended up framed in the same shot as the Professor).
        private void HideTitleSceneEggs()
        {
            var eggs = FindObjectsByType<HatchingEggSequence>(FindObjectsSortMode.None);
            _hiddenEggs = new System.Collections.Generic.List<GameObject>(eggs.Length);
            foreach (var e in eggs)
            {
                if (e == null || !e.gameObject.activeSelf) continue;
                _hiddenEggs.Add(e.gameObject);
                e.gameObject.SetActive(false);
            }
        }

        private void RestoreHiddenEggs()
        {
            if (_hiddenEggs == null) return;
            foreach (var go in _hiddenEggs)
            {
                if (go != null) go.SetActive(true);
            }
            _hiddenEggs = null;
        }

        private void OnDestroy()
        {
            RestoreHiddenEggs();
            if (_sceneEnhancer != null) Destroy(_sceneEnhancer);
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

            // Wander disabled per request — Professor stays put on the stage.
            // (ProfessorWander.cs is kept around in case we want to re-enable
            // it later; just re-add the AddComponent call here.)

            if (_dialogueBox == null)
            {
                // RPG-style dialogue box (panel + portrait + speaker header +
                // body), parented to the professor so it tracks any bob/sway.
                // Initialize() positions it at localY 2.4 (well above the hat,
                // hat top sits at y=1.47), and the script billboards each
                // LateUpdate to face the player.
                var dialogueGo = new GameObject("ProfessorDialogue");
                _dialogueBox = dialogueGo.AddComponent<RPGDialogueBox>();
                // Pass null so the dialogue box bakes a headshot from the
                // Adventurer model itself instead of the doodle smiley.
                _dialogueBox.Initialize(profGo.transform, "Professor Pastel", null);
                _dialogueBox.Hide();
            }

            // Visual atmosphere: warm key light, paper-cream stage ring on
            // the floor, drifting motes, and a faint mist puff. Purely
            // cosmetic. The component cleans itself up on parent destroy.
            var atmosphereGo = new GameObject("StageAtmosphere");
            atmosphereGo.transform.SetParent(transform, worldPositionStays: true);
            var atmosphere = atmosphereGo.AddComponent<TutorialStageAtmosphere>();
            atmosphere.Setup(_stageCenter, _stageForward);
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
            // Prefer a local test model from Resources/ if one is configured.
            // The UploadThing GLB has been flaky (404s) so this keeps the
            // practice fight working offline / in dev. The GLB path below
            // only runs if this Resources load returns null.
            if (!string.IsNullOrEmpty(testScribbleResourcePath))
            {
                var testPrefab = Resources.Load<GameObject>(testScribbleResourcePath);
                if (testPrefab != null)
                {
                    var localContainer = Instantiate(testPrefab);
                    localContainer.name = "BorrowedScribble";
                    localContainer.transform.SetParent(transform, worldPositionStays: true);
                    localContainer.transform.position = _stageCenter - _stageForward * 0.5f + Vector3.up * borrowedScribbleVerticalOffset;
                    Vector3 localAwayFromPlayer = -_stageForward;
                    // Build rotation directly from Euler angles so X/Y/Z are
                    // directly settable in the inspector (composing with
                    // LookRotation was bleeding non-zero Z into the final
                    // rotation). The auto-yaw faces the dummy; the offsets
                    // are added on top.
                    float autoYawDeg = (localAwayFromPlayer.sqrMagnitude > 1e-4f)
                        ? Mathf.Atan2(localAwayFromPlayer.x, localAwayFromPlayer.z) * Mathf.Rad2Deg
                        : 0f;
                    localContainer.transform.rotation = Quaternion.Euler(
                        borrowedScribblePitchOffsetDeg,
                        autoYawDeg + borrowedScribbleYawOffsetDeg,
                        borrowedScribbleRollOffsetDeg);
                    // Smaller target than the GLB path (0.45 vs 0.55) — the
                    // fallback is a full humanoid, so we shrink it a bit so
                    // it reads as a creature instead of a tiny person.
                    AutoScale(localContainer.transform, 0.90f);
                    try { DrawingColorize.Apply(localContainer, MakeSolidTexture(new Color(0.95f, 0.85f, 0.55f)), 0f); }
                    catch (Exception e) { Debug.LogException(e); }
                    _borrowedScribble = localContainer;
                    StartCoroutine(PopInScale(localContainer.transform, 0.55f));
                    Debug.Log($"[ProfessorTutorial] Using local test scribble model '{testScribbleResourcePath}' at {localContainer.transform.position}.");
                    yield break;
                }
                Debug.LogWarning($"[ProfessorTutorial] Test scribble Resources path '{testScribbleResourcePath}' not found; falling back to GLB download.");
            }

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
            StartCoroutine(PopInScale(container.transform, 0.55f));
            Debug.Log($"[ProfessorTutorial] Borrowed scribble spawned at {container.transform.position} scale={container.transform.localScale.x:F2} childCount={container.transform.childCount}");
        }

        private void SpawnDummy()
        {
            if (_dummy != null) return;
            // Practice dummy is the static CharacterBase FBX, sitting 1.7m
            // further from the player than the Professor.
            var root = new GameObject("PracticeDummy");
            root.transform.SetParent(transform, worldPositionStays: true);
            // CharacterBase prefab is built with a wrapper that internally
            // lifts the FBX so its feet sit at the wrapper's y=0. So we
            // place the wrapper directly at the stage's floor Y (same as
            // the Professor). dummyVerticalOffset stays exposed in the
            // inspector for fine tuning if a particular scene's floor
            // reference is offset.
            root.transform.position = _stageCenter - _stageForward * 1.7f + Vector3.up * dummyVerticalOffset;
            if (_stageForward.sqrMagnitude > 1e-4f)
                root.transform.rotation = Quaternion.LookRotation(_stageForward, Vector3.up);
            else
                root.transform.rotation = Quaternion.Euler(0, 90f, 0);

            // Hoodie has built-in Idle animation via its Animator, so we
            // don't need the procedural DummyIdle bob/sway here.
            var prefab = Resources.Load<GameObject>("Characters/Hoodie");
            if (prefab != null)
            {
                var inst = Instantiate(prefab, root.transform);
                inst.name = "Hoodie";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Debug.LogError("[ProfessorTutorial] Missing Resources/Characters/Hoodie.prefab");
            }

            var fb = root.AddComponent<DummyHitFeedback>();
            fb.Initialize();

            // Apply the project's paper-craft material so the dummy reads as
            // a scribble instead of a default grey humanoid. Null drawing
            // texture → DrawingColorize falls back to the paper material
            // with a neutral grey tint.
            try { DrawingColorize.Apply(root, MakeSolidTexture(new Color(0.85f, 0.78f, 0.65f)), 0f); }
            catch (Exception e) { Debug.LogException(e); }

            _dummy = root;

            // Pop-in scale animation so the dummy doesn't just snap into the
            // scene at full size.
            StartCoroutine(PopInScale(root.transform, 0.55f));
        }

        // Reusable elastic pop-in: starts at scale 0, overshoots slightly,
        // settles at the original scale. Used by SpawnDummy and the borrowed
        // scribble spawn so both creatures appear with a beat of life
        // instead of materialising fully formed.
        private IEnumerator PopInScale(Transform t, float duration)
        {
            if (t == null) yield break;
            Vector3 endScale = t.localScale;
            t.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < duration && t != null)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                // Cubic ease-out + slight overshoot up to 1.12 then settle.
                float k = 1f - Mathf.Pow(1f - p, 3f);
                float scale = Mathf.Lerp(0f, 1.12f, k);
                if (p > 0.85f) scale = Mathf.Lerp(1.12f, 1f, (p - 0.85f) / 0.15f);
                t.localScale = endScale * scale;
                yield return null;
            }
            if (t != null) t.localScale = endScale;
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
            else
            {
                _professor?.Celebrate();
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
            _professor?.Celebrate();
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
            else
            {
                _professor?.Celebrate();
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

            // Professor visibly casts the spell.
            _professor?.Celebrate();

            // (Disabled — the Adventurer FBX's pointing clip read as the
            // Professor "whipping" his own scribble during the attack which
            // confused testers. Leave the cast visuals to the scribble.)
            // if (_professor != null) _professor.PlayPoint();

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

            // Pre-flash: bright burst of light right as the bolt fires.
            SpawnLightningPreFlash();

            // Main lightning particle burst from scribble to dummy.
            SpawnLightningEffect();

            // Secondary forks branching off the main bolt.
            SpawnLightningForks();

            // Brief screen-shake (Camera.main jitter for ~0.15s).
            StartCoroutine(CameraShake(0.15f, 0.04f));

            // Dummy reels but STAYS STANDING. No fall-over, no respawn —
            // the player will keep practicing on this same dummy through
            // the guided tutorial and the freeform continuous practice.
            if (_dummy != null) _dummy.GetComponent<DummyHitFeedback>()?.OnHit(new Color(0.40f, 0.65f, 1.0f), 12f);

            // Ground sparks at the dummy's feet on impact.
            SpawnGroundSparks();

            yield return PunchScale(_dummy, 0.20f, 0.15f);

            yield return new WaitForSeconds(0.2f);
        }

        // Quick camera jitter — translates Camera.main on local axes by a
        // small random amount each frame, then restores its starting offset.
        // Runs in parallel; safe if the camera is already being driven by
        // an XR rig (we restore to the original local position at the end).
        private IEnumerator CameraShake(float duration, float magnitude)
        {
            var cam = Camera.main;
            if (cam == null) yield break;
            var tr = cam.transform;
            Vector3 origin = tr.localPosition;
            float t = 0f;
            while (t < duration && cam != null)
            {
                t += Time.deltaTime;
                float falloff = 1f - Mathf.Clamp01(t / duration);
                Vector3 jitter = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f)) * magnitude * falloff;
                tr.localPosition = origin + jitter;
                yield return null;
            }
            if (cam != null) tr.localPosition = origin;
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
            if (_dummy != null) _dummy.GetComponent<DummyHitFeedback>()?.OnHit(color, 12f);
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
            // Professor visibly casts (Sword_Slash) whenever a move is demoed.
            _professor?.Celebrate();

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

        // FIREBALL — multi-layer: glowing orb + ember trail + heat-shimmer
        // halo travels scribble->dummy, then explodes into a radial ring,
        // an outward shockwave, and a slow smoke puff that rises after.
        private IEnumerator PerformFireball()
        {
            if (!TryGetCastEndpoints(out var origin, out var target)) yield break;

            yield return ScribbleWindup(0.30f, 0.05f, 0.18f);
            if (!TryGetCastEndpoints(out origin, out target)) yield break;

            Color hot    = new Color(1.0f, 0.55f, 0.15f);
            Color core   = new Color(1.0f, 0.85f, 0.40f);
            Color deep   = new Color(0.85f, 0.20f, 0.05f);
            Color shimmer = new Color(1.0f, 0.75f, 0.35f, 0.35f);
            Color smoke  = new Color(0.18f, 0.16f, 0.14f, 0.85f);

            // Stage 1: orb (a small lit sphere) travels from origin to target.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "FireOrb";
            var col = orb.GetComponent<Collider>(); if (col != null) Destroy(col);
            orb.transform.SetParent(transform, worldPositionStays: true);
            orb.transform.position = origin;
            orb.transform.localScale = Vector3.one * 0.22f;
            var orbSh = Shader.Find("Universal Render Pipeline/Lit");
            var orbMat = new Material(orbSh);
            if (orbMat.HasProperty("_BaseColor")) orbMat.SetColor("_BaseColor", core);
            if (orbMat.HasProperty("_EmissionColor")) { orbMat.EnableKeyword("_EMISSION"); orbMat.SetColor("_EmissionColor", hot * 6f); }
            orb.GetComponent<Renderer>().sharedMaterial = orbMat;

            // Outer halo orb — slightly larger, brighter, additive-feel.
            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "FireHalo";
            var hcol = halo.GetComponent<Collider>(); if (hcol != null) Destroy(hcol);
            halo.transform.SetParent(orb.transform, worldPositionStays: false);
            halo.transform.localPosition = Vector3.zero;
            halo.transform.localScale = Vector3.one * 1.7f;
            var haloMat = new Material(orbSh);
            if (haloMat.HasProperty("_BaseColor")) haloMat.SetColor("_BaseColor", new Color(hot.r, hot.g, hot.b, 0.35f));
            if (haloMat.HasProperty("_EmissionColor")) { haloMat.EnableKeyword("_EMISSION"); haloMat.SetColor("_EmissionColor", hot * 3f); }
            halo.GetComponent<Renderer>().sharedMaterial = haloMat;

            // Trailing embers behind the orb (sphere shape, billboard).
            var trail = BuildFx("FireballTrail", origin, target, hot);
            {
                var main = trail.ps.main;
                main.playOnAwake = false;
                main.duration = 0.6f; main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.65f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.2f, 1.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.07f, 0.18f);
                main.startColor    = new ParticleSystem.MinMaxGradient(hot, core);
                main.maxParticles  = 160;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = -0.4f; // embers float upward
                trail.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = trail.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 160f;
                var shape = trail.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.10f;
                var col2 = trail.ps.colorOverLifetime;
                col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(core, 0f), new GradientColorKey(hot, 0.5f), new GradientColorKey(deep, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                var size = trail.ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
                trail.ps.Play();
            }

            // Heat-shimmer ring — soft, slow, transparent particles around the orb.
            var shimFx = BuildFx("FireballShimmer", origin, target, shimmer);
            {
                var main = shimFx.ps.main;
                main.playOnAwake = false;
                main.duration = 0.6f; main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.05f, 0.30f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.18f, 0.36f);
                main.startColor    = new ParticleSystem.MinMaxGradient(shimmer);
                main.maxParticles  = 60;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                shimFx.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = shimFx.ps.emission; emission.enabled = true; emission.rateOverTime = 50f;
                var shape = shimFx.ps.shape; shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.18f;
                var col2 = shimFx.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.4f, 0.4f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                shimFx.ps.Play();
            }

            float travelDur = 0.40f, t = 0f;
            while (t < travelDur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / travelDur);
                Vector3 pos = Vector3.Lerp(origin, target, k);
                // Slight pulsing scale for the orb so it reads as flickering.
                float pulse = 1f + 0.10f * Mathf.Sin(t * 32f);
                orb.transform.position = pos;
                orb.transform.localScale = Vector3.one * 0.22f * pulse;
                if (trail.go != null) trail.go.transform.position = pos;
                if (shimFx.go != null) shimFx.go.transform.position = pos;
                yield return null;
            }

            if (trail.go != null) Destroy(trail.go, 0.5f);
            if (shimFx.go != null) Destroy(shimFx.go, 0.6f);
            Destroy(orb);

            // Stage 2a: bright core flash burst at impact (large hot sparks).
            var burst = BuildFx("FireballBurst", target, target + Vector3.up, hot);
            {
                var main = burst.ps.main;
                main.playOnAwake = false;
                main.duration = 0.30f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.65f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(2.5f, 6.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
                main.startColor    = new ParticleSystem.MinMaxGradient(hot, core);
                main.maxParticles  = 220;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                burst.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = burst.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 160) });
                var shape = burst.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.05f;
                var col2 = burst.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(core, 0f), new GradientColorKey(hot, 0.4f), new GradientColorKey(deep, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                burst.ps.Play();
                Destroy(burst.go, 1.4f);
            }

            // Stage 2b: expanding shockwave RING (donut shape, fast outward).
            var ring = BuildFx("FireballRing", target, target + Vector3.up, hot);
            {
                ring.go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                var main = ring.ps.main;
                main.playOnAwake = false;
                main.duration = 0.25f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(4.0f, 6.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
                main.startColor    = new ParticleSystem.MinMaxGradient(core, hot);
                main.maxParticles  = 90;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                ring.psr.renderMode = ParticleSystemRenderMode.Stretch;
                ring.psr.lengthScale = 2.5f; ring.psr.velocityScale = 0.4f;
                var emission = ring.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
                var shape = ring.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Donut;
                shape.radius = 0.12f; shape.donutRadius = 0.02f;
                var col2 = ring.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(core, 0f), new GradientColorKey(deep, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                ring.ps.Play();
                Destroy(ring.go, 1.2f);
            }

            // Hit reaction — dummy stays standing (fire feel = bigger punch).
            if (_dummy != null) _dummy.GetComponent<DummyHitFeedback>()?.OnHit(new Color(1.0f, 0.55f, 0.15f), 12f);
            yield return PunchScale(_dummy, 0.24f, 0.18f);

            // Stage 3: lingering smoke puff that drifts upward & fades.
            var smokeFx = BuildFx("FireballSmoke", target, target + Vector3.up, smoke);
            {
                var main = smokeFx.ps.main;
                main.playOnAwake = false;
                main.duration = 0.6f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
                main.startColor    = new ParticleSystem.MinMaxGradient(smoke);
                main.maxParticles  = 70;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = -0.30f; // smoke rises
                smokeFx.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = smokeFx.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 35), new ParticleSystem.Burst(0.15f, 20) });
                var shape = smokeFx.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.10f;
                var col2 = smokeFx.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(smoke, 0f), new GradientColorKey(smoke, 1f) },
                    new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(0.85f, 0.25f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                var sizeOL = smokeFx.ps.sizeOverLifetime; sizeOL.enabled = true;
                sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
                smokeFx.ps.Play();
                Destroy(smokeFx.go, 2.4f);
            }
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
            Color deepBlue = new Color(0.10f, 0.40f, 0.80f);

            // Layer A: stretched droplet stream (long thin droplets — the
            // "core" of the hose).
            var spray = BuildFx("WaterSpray", origin, target, cold);
            {
                var main = spray.ps.main;
                main.playOnAwake = false;
                main.duration = 0.65f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(4.0f, 6.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.startColor    = new ParticleSystem.MinMaxGradient(cold, foam);
                main.maxParticles  = 320;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.45f;
                spray.psr.renderMode = ParticleSystemRenderMode.Stretch;
                spray.psr.lengthScale = 3.5f;
                spray.psr.velocityScale = 0.35f;
                var emission = spray.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 320f;
                var shape = spray.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle  = 14f;
                shape.radius = 0.05f;
                var col2 = spray.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(foam, 0f), new GradientColorKey(cold, 0.5f), new GradientColorKey(deepBlue, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                spray.ps.Play();
                Destroy(spray.go, 1.8f);
            }

            // Layer B: round droplets (sphere billboard). Adds volume to the
            // stream so it doesn't look like only thin shards.
            var droplets = BuildFx("WaterDroplets", origin, target, foam);
            {
                var main = droplets.ps.main;
                main.playOnAwake = false;
                main.duration = 0.65f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(3.0f, 5.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                main.startColor    = new ParticleSystem.MinMaxGradient(foam, cold);
                main.maxParticles  = 220;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.55f;
                droplets.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = droplets.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 220f;
                var shape = droplets.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.07f;
                // Push droplets along the cast direction so they flow toward the dummy.
                var vol = droplets.ps.velocityOverLifetime;
                vol.enabled = true; vol.space = ParticleSystemSimulationSpace.Local;
                vol.z = new ParticleSystem.MinMaxCurve(3.5f, 5.0f);
                var sizeOL = droplets.ps.sizeOverLifetime; sizeOL.enabled = true;
                sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f));
                droplets.ps.Play();
                Destroy(droplets.go, 1.8f);
            }

            // Wait for the spray to actually reach the dummy before the hit.
            yield return new WaitForSeconds(0.35f);

            // Splash burst: big radial fan at the dummy + ground droplets.
            var splash = BuildFx("WaterSplash", target, target + Vector3.up, foam);
            {
                var main = splash.ps.main;
                main.playOnAwake = false;
                main.duration = 0.30f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.40f, 0.70f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
                main.startColor    = new ParticleSystem.MinMaxGradient(foam, cold);
                main.maxParticles  = 200;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.7f;
                splash.psr.renderMode = ParticleSystemRenderMode.Stretch;
                splash.psr.lengthScale = 2.0f; splash.psr.velocityScale = 0.4f;
                var emission = splash.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] {
                    new ParticleSystem.Burst(0f, 110),
                    new ParticleSystem.Burst(0.05f, 50),
                });
                var shape = splash.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.12f;
                splash.ps.Play();
                Destroy(splash.go, 1.6f);
            }

            // Splash mist — soft round droplets that linger and fall.
            var mist = BuildFx("WaterMist", target, target + Vector3.up, foam);
            {
                var main = mist.ps.main;
                main.playOnAwake = false;
                main.duration = 0.40f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
                main.startColor    = new ParticleSystem.MinMaxGradient(new Color(foam.r, foam.g, foam.b, 0.55f));
                main.maxParticles  = 90;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.35f;
                mist.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = mist.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
                var shape = mist.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.18f;
                var col2 = mist.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(foam, 0f), new GradientColorKey(cold, 1f) },
                    new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                mist.ps.Play();
                Destroy(mist.go, 1.8f);
            }

            if (_dummy != null) _dummy.GetComponent<DummyHitFeedback>()?.OnHit(new Color(0.30f, 0.65f, 1.00f), 12f);
            yield return PunchScale(_dummy, 0.18f, 0.16f);
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
            Color frost   = new Color(0.92f, 0.98f, 1.00f, 0.7f);

            // Travel: a few angular stretched shards thrown in a tight cone.
            var shards = BuildFx("IceShards", origin, target, icePale);
            {
                var main = shards.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(8.5f, 11f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
                main.startColor    = new ParticleSystem.MinMaxGradient(icePale, iceDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 50;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                shards.psr.renderMode  = ParticleSystemRenderMode.Stretch;
                shards.psr.lengthScale = 6f;
                shards.psr.velocityScale = 0.5f;
                var emission = shards.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
                var shape = shards.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle  = 4f;
                shape.radius = 0.04f;
                shards.ps.Play();
                Destroy(shards.go, 1.0f);
            }

            // Frost trail — fine pale snow drifting along the shard path.
            var frostFx = BuildFx("IceFrostTrail", origin, target, frost);
            {
                var main = frostFx.ps.main;
                main.playOnAwake = false;
                main.duration = 0.30f; main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
                main.startColor    = new ParticleSystem.MinMaxGradient(frost);
                main.maxParticles  = 120;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.05f;
                frostFx.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = frostFx.ps.emission;
                emission.enabled = true; emission.rateOverTime = 200f;
                var shape = frostFx.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.06f;
                var col2 = frostFx.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(icePale, 1f) },
                    new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                frostFx.ps.Play();
            }

            // Animate the frost-trail anchor along the shard path.
            float tFrost = 0f, dFrost = 0.18f;
            while (tFrost < dFrost && frostFx.go != null)
            {
                tFrost += Time.deltaTime;
                float k = Mathf.Clamp01(tFrost / dFrost);
                frostFx.go.transform.position = Vector3.Lerp(origin, target, k);
                yield return null;
            }
            if (frostFx.go != null) Destroy(frostFx.go, 0.6f);

            // Impact: crystallize burst — bright pale flash that grows.
            var crystallize = BuildFx("IceCrystallize", target, target + Vector3.up, icePale);
            {
                var main = crystallize.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
                main.startColor    = new ParticleSystem.MinMaxGradient(Color.white, icePale);
                main.maxParticles  = 60;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                crystallize.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = crystallize.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 45) });
                var shape = crystallize.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.02f;
                var sizeOL = crystallize.ps.sizeOverLifetime; sizeOL.enabled = true;
                sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));
                var col2 = crystallize.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(icePale, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                crystallize.ps.Play();
                Destroy(crystallize.go, 1.0f);
            }

            // Impact: jagged shatter — radial stretched ice fragments.
            var shatter = BuildFx("IceShatter", target, target + Vector3.up, icePale);
            {
                var main = shatter.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(4.5f, 7.0f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                main.startColor    = new ParticleSystem.MinMaxGradient(icePale, iceDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 140;
                main.gravityModifier = 0.9f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                shatter.psr.renderMode  = ParticleSystemRenderMode.Stretch;
                shatter.psr.lengthScale = 1.8f;
                shatter.psr.velocityScale = 0.7f;
                var emission = shatter.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 100) });
                var shape = shatter.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.02f;
                shatter.ps.Play();
                Destroy(shatter.go, 1.4f);
            }

            // Falling ice fragment debris — small chunks that fall and fade.
            var debris = BuildFx("IceDebris", target, target + Vector3.up, iceDeep);
            {
                var main = debris.ps.main;
                main.playOnAwake = false;
                main.duration = 0.30f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                main.startColor    = new ParticleSystem.MinMaxGradient(icePale, iceDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 60;
                main.gravityModifier = 1.6f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                debris.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = debris.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30), new ParticleSystem.Burst(0.08f, 20) });
                var shape = debris.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.10f;
                var rotOL = debris.ps.rotationOverLifetime;
                rotOL.enabled = true;
                rotOL.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                var col2 = debris.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(icePale, 0f), new GradientColorKey(iceDeep, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                debris.ps.Play();
                Destroy(debris.go, 2.0f);
            }

            if (_dummy != null) _dummy.GetComponent<DummyHitFeedback>()?.OnHit(new Color(0.55f, 0.80f, 1.00f), 12f);
            yield return PunchScale(_dummy, 0.20f, 0.16f);
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
            Color afterImg   = new Color(0.70f, 1.00f, 0.55f, 0.55f);

            // Slash streak — single fast stretched line aimed at the dummy.
            var slash = BuildFx("LeafSlash", origin, target, leafBright);
            {
                var main = slash.ps.main;
                main.playOnAwake = false;
                main.duration = 0.10f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(15f, 20f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
                main.startColor    = new ParticleSystem.MinMaxGradient(leafBright, leafDeep);
                main.maxParticles  = 60;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                slash.psr.renderMode  = ParticleSystemRenderMode.Stretch;
                slash.psr.lengthScale = 12f;
                slash.psr.velocityScale = 0.7f;
                var emission = slash.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
                var shape = slash.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle  = 1.5f;
                shape.radius = 0.02f;
                slash.ps.Play();
                Destroy(slash.go, 0.9f);
            }

            // Arc afterimage — slightly offset secondary slash giving the
            // impression of a curved blade swing.
            var arcDir = (target - origin);
            Vector3 arcUp = Vector3.up * 0.18f;
            var arc = BuildFx("LeafArc", origin + arcUp, target + arcUp, afterImg);
            {
                var main = arc.ps.main;
                main.playOnAwake = false;
                main.duration = 0.10f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(12f, 16f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                main.startColor    = new ParticleSystem.MinMaxGradient(afterImg);
                main.maxParticles  = 40;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                arc.psr.renderMode = ParticleSystemRenderMode.Stretch;
                arc.psr.lengthScale = 10f; arc.psr.velocityScale = 0.6f;
                var emission = arc.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
                var shape = arc.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 2.5f; shape.radius = 0.03f;
                var col2 = arc.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(afterImg, 0f), new GradientColorKey(leafDeep, 1f) },
                    new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                arc.ps.Play();
                Destroy(arc.go, 0.9f);
            }

            yield return new WaitForSeconds(0.10f);

            // Impact: green sparkle puff (small omnidirectional billboard).
            var puff = BuildFx("LeafPuff", target, target + Vector3.up, leafBright);
            {
                var main = puff.ps.main;
                main.playOnAwake = false;
                main.duration = 0.20f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(2.0f, 4.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.startColor    = new ParticleSystem.MinMaxGradient(leafBright, leafDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 120;
                main.gravityModifier = 0.2f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                puff.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = puff.ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
                var shape = puff.ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
                puff.ps.Play();
                Destroy(puff.go, 1.4f);
            }

            // Raining leaf petals — billboard particles spawning around the
            // dummy and drifting down with rotation, like falling leaves.
            var petals = BuildFx("LeafPetals", target + Vector3.up * 0.6f, target + Vector3.up, leafBright);
            {
                var main = petals.ps.main;
                main.playOnAwake = false;
                main.duration = 0.5f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.startColor    = new ParticleSystem.MinMaxGradient(leafBright, leafDeep);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles  = 70;
                main.gravityModifier = 0.35f; // gentle fall
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                petals.psr.renderMode = ParticleSystemRenderMode.Billboard;
                var emission = petals.ps.emission;
                emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] {
                    new ParticleSystem.Burst(0f, 25),
                    new ParticleSystem.Burst(0.15f, 20),
                    new ParticleSystem.Burst(0.30f, 15),
                });
                var shape = petals.ps.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.30f;
                // Wobble sideways using velocity over lifetime so they fall like leaves.
                var vol = petals.ps.velocityOverLifetime;
                vol.enabled = true; vol.space = ParticleSystemSimulationSpace.World;
                vol.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                vol.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                var rotOL = petals.ps.rotationOverLifetime;
                rotOL.enabled = true;
                rotOL.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
                var col2 = petals.ps.colorOverLifetime; col2.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(leafBright, 0f), new GradientColorKey(leafDeep, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col2.color = new ParticleSystem.MinMaxGradient(grad);
                petals.ps.Play();
                Destroy(petals.go, 2.6f);
            }

            if (_dummy != null) _dummy.GetComponent<DummyHitFeedback>()?.OnHit(new Color(0.55f, 0.95f, 0.40f), 12f);
            yield return PunchScale(_dummy, 0.18f, 0.14f);
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
            psr.lengthScale = 6f;          // longer streaks for thicker bolt
            psr.velocityScale = 0.4f;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.40f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.4f), new Color(1f, 1f, 1f));
            main.maxParticles = 180;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            // Two pulses so the bolt feels like it strikes twice in quick succession.
            emission.SetBursts(new[] {
                new ParticleSystem.Burst(0f, 120),
                new ParticleSystem.Burst(0.06f, 70),
            });

            // Aim particles toward the dummy.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.05f;
            Vector3 toDummy = (_dummy.transform.position + Vector3.up * 0.4f) - go.transform.position;
            go.transform.rotation = Quaternion.LookRotation(toDummy.normalized, Vector3.up);
            // Cone in PS is along +Z by default if shape doesn't override, which works here.

            // Add a color-over-lifetime so the streaks fade from white-hot to
            // electric-yellow as they age.
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.95f, 0.4f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            ps.Play();
            Destroy(go, 1.5f);
        }

        // Bright omnidirectional pre-flash spawned right at the cast origin.
        // Bigger billboard, very short life — sells the "strike" moment.
        private void SpawnLightningPreFlash()
        {
            if (_borrowedScribble == null || _dummy == null) return;
            Vector3 origin = _borrowedScribble.transform.position + Vector3.up * 0.4f;

            var fx = BuildFx("LightningFlash", origin, _dummy.transform.position, new Color(1f, 0.98f, 0.7f));
            var main = fx.ps.main;
            main.playOnAwake = false;
            main.duration = 0.10f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.16f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.0f, 0.4f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 0.85f));
            main.maxParticles  = 12;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            fx.psr.renderMode = ParticleSystemRenderMode.Billboard;
            var emission = fx.ps.emission; emission.enabled = true; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            var shape = fx.ps.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.02f;
            var sizeOL = fx.ps.sizeOverLifetime; sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1.3f, 1f, 0f));
            var col = fx.ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.95f, 0.4f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            fx.ps.Play();
            Destroy(fx.go, 0.6f);

            // Mirror flash at the dummy too so the impact reads as illuminated.
            Vector3 hit = _dummy.transform.position + Vector3.up * 0.4f;
            var fx2 = BuildFx("LightningFlashHit", hit, hit + Vector3.up, new Color(1f, 0.98f, 0.7f));
            var m2 = fx2.ps.main;
            m2.playOnAwake = false; m2.duration = 0.10f; m2.loop = false;
            m2.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.20f);
            m2.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.2f);
            m2.startSize  = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            m2.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 0.85f));
            m2.maxParticles = 12;
            m2.simulationSpace = ParticleSystemSimulationSpace.World;
            fx2.psr.renderMode = ParticleSystemRenderMode.Billboard;
            var em2 = fx2.ps.emission; em2.enabled = true; em2.rateOverTime = 0;
            em2.SetBursts(new[] { new ParticleSystem.Burst(0.04f, 6) });
            var sh2 = fx2.ps.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Sphere; sh2.radius = 0.02f;
            var sz2 = fx2.ps.sizeOverLifetime; sz2.enabled = true;
            sz2.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1.4f, 1f, 0f));
            var c2 = fx2.ps.colorOverLifetime; c2.enabled = true;
            c2.color = new ParticleSystem.MinMaxGradient(grad);
            fx2.ps.Play();
            Destroy(fx2.go, 0.7f);
        }

        // Several short branching forks shot off the main bolt — each fork
        // is a tiny stretched-particle cone aimed in a randomized direction
        // around the scribble->dummy axis.
        private void SpawnLightningForks()
        {
            if (_borrowedScribble == null || _dummy == null) return;
            Vector3 origin = _borrowedScribble.transform.position + Vector3.up * 0.4f;
            Vector3 target = _dummy.transform.position + Vector3.up * 0.4f;
            Vector3 axis = (target - origin).normalized;

            for (int i = 0; i < 4; i++)
            {
                // Random offset perpendicular to the bolt axis.
                Vector3 perp = Vector3.Cross(axis, UnityEngine.Random.onUnitSphere).normalized;
                Vector3 forkOrigin = Vector3.Lerp(origin, target, UnityEngine.Random.Range(0.25f, 0.75f))
                                     + perp * UnityEngine.Random.Range(0.04f, 0.10f);
                Vector3 forkTarget = forkOrigin + (axis + perp * UnityEngine.Random.Range(0.5f, 1.2f)) * 0.5f;

                var fork = BuildFx($"LightningFork_{i}", forkOrigin, forkTarget, new Color(1f, 0.98f, 0.7f));
                var main = fork.ps.main;
                main.playOnAwake = false;
                main.duration = 0.10f; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(5f, 9f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                main.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 0.85f), new Color(1f, 0.95f, 0.4f));
                main.maxParticles  = 30;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                fork.psr.renderMode = ParticleSystemRenderMode.Stretch;
                fork.psr.lengthScale = 5f; fork.psr.velocityScale = 0.4f;
                var emission = fork.ps.emission; emission.enabled = true; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
                var shape = fork.ps.shape; shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 5f; shape.radius = 0.02f;
                fork.ps.Play();
                Destroy(fork.go, 0.6f);
            }
        }

        // Sparks at the dummy's feet on impact — fast yellow stretched
        // particles spraying outward along the ground.
        private void SpawnGroundSparks()
        {
            if (_dummy == null) return;
            Vector3 ground = _dummy.transform.position + Vector3.up * 0.05f;

            var spark = BuildFx("LightningGroundSparks", ground, ground + Vector3.up, new Color(1f, 0.95f, 0.4f));
            // Aim the cone upward-outward — rotate so +Z points up (radial fan along ground).
            spark.go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            var main = spark.ps.main;
            main.playOnAwake = false;
            main.duration = 0.20f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.45f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(3.0f, 5.5f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 0.7f), new Color(1f, 0.85f, 0.3f));
            main.maxParticles  = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.6f;
            spark.psr.renderMode = ParticleSystemRenderMode.Stretch;
            spark.psr.lengthScale = 3f; spark.psr.velocityScale = 0.5f;
            var emission = spark.ps.emission; emission.enabled = true; emission.rateOverTime = 0;
            emission.SetBursts(new[] {
                new ParticleSystem.Burst(0f, 50),
                new ParticleSystem.Burst(0.06f, 30),
            });
            var shape = spark.ps.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 75f; // very wide cone — almost a dome along the ground
            shape.radius = 0.05f;
            var col = spark.ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.7f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            spark.ps.Play();
            Destroy(spark.go, 1.2f);
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
                // Unity 6 emits "Audio compression is not valid as
                // streamAudio is set to true" if both flags are on. Pick
                // streaming (we want the clip ready ASAP) and leave the
                // compressed-storage flag off.
                dh.streamAudio = true;
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
                // Unity 6 emits "Audio compression is not valid as
                // streamAudio is set to true" if both flags are on. Pick
                // streaming (we want the clip ready ASAP) and leave the
                // compressed-storage flag off.
                dh.streamAudio = true;
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
