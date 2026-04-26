using System.Collections;
using System.Threading.Tasks;
using Tigerverse.Combat;
using Tigerverse.Drawing;
using Tigerverse.Meshy;
using Tigerverse.MR;
using Tigerverse.Net;
using Tigerverse.UI;
using Tigerverse.Voice;
using UnityEngine;
using UnityEngine.Events;

namespace Tigerverse.Core
{
    /// <summary>
    /// Central orchestrator for the Tigerverse session lifecycle. Coordinates networking,
    /// drawing/projection, voice routing, monster spawn, hatch sequence, and battle.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        [System.Serializable]
        public class StateChangedEvent : UnityEvent<AppState> { }

        [Header("Net / Backend")]
        [SerializeField] private BackendConfig config;
        [SerializeField] private SessionRunner runner;
        [SerializeField] private SessionApiClient apiClient;

        [Header("Combat")]
        [SerializeField] private MoveCatalog catalog;
        [SerializeField] private BattleManager battle;
        [SerializeField] private ModelFetcher modelFetcher;

        [Header("Scene References")]
        [SerializeField] private TabletAnchor[] tabletAnchors; // [0] = local, [1] = remote
        [SerializeField] private HatchingEggSequence[] eggs;   // [0], [1]
        [SerializeField] private MonsterCry[] cries;           // populated as monsters spawn
        [SerializeField] private HPBar[] hpBars;
        [SerializeField] private PreBattleRevealCard[] revealCards;
        [SerializeField] private QRCodeDisplay[] qrDisplays;   // shown in Lobby and DrawWait
        [SerializeField] private VoiceCommandRouter voiceRouter;
        [SerializeField] private AudioSource announcer;

        public AppState currentState { get; private set; }
        public string sessionCode;
        public int localCasterIndex;
        public StateChangedEvent OnStateChanged;

        private MonsterStatsSO statsA;
        private MonsterStatsSO statsB;
        private GameObject monsterAGo;
        private GameObject monsterBGo;

        // Public accessors so other systems (ReadyHandshake's MR transition,
        // BattleManager hookups) can grab the spawned monster GameObjects
        // without having to GameObject.Find them by name.
        public GameObject MonsterAGameObject => monsterAGo;
        public GameObject MonsterBGameObject => monsterBGo;
        public TabletAnchor[] TabletAnchors => tabletAnchors;

        private void Awake()
        {
            if (config == null)
            {
                config = BackendConfig.Load();
                if (config == null)
                {
                    Debug.LogWarning("[GameStateManager] BackendConfig.Load() returned null. Drop a BackendConfig asset under Resources/.");
                }
            }

            if (catalog == null)
            {
                catalog = MoveCatalog.Load();
                if (catalog == null)
                {
                    Debug.LogWarning("[GameStateManager] MoveCatalog.Load() returned null. Drop a MoveCatalog asset under Resources/.");
                }
            }

            if (OnStateChanged == null)
            {
                OnStateChanged = new StateChangedEvent();
            }

            SetState(AppState.Title);
        }

        public void StartHosting()
        {
            sessionCode = RoomCodeGenerator.Generate();
            localCasterIndex = 0;
            Debug.Log($"[GameStateManager] StartHosting → code={sessionCode}");
            StartCoroutine(HostFlow());
        }

        public void JoinByCode(string code)
        {
            sessionCode = code;
            localCasterIndex = 1;
            Debug.Log($"[GameStateManager] JoinByCode → code={code}");
            StartCoroutine(JoinFlow());
        }

        private IEnumerator HostFlow()
        {
            SetState(AppState.Lobby);

            // HTTP session polling (which drives the eggs / tutorial / hatch)
            // is independent of Photon. Kick it off immediately so the player
            // sees their own egg appear the moment the website registers
            // their drawing — even if Photon connect is slow, fails, or
            // hasn't been configured (empty photonAppId in BackendConfig).
            BeginDrawWait();

            if (runner == null)
            {
                Debug.LogWarning("[GameStateManager] SessionRunner missing — Photon room won't host, but HTTP polling continues.");
                yield break;
            }

            Task<bool> t = runner.CreateRoom(sessionCode);
            yield return new WaitUntil(() => t.IsCompleted);

            if (!t.Result)
            {
                Debug.LogWarning("[GameStateManager] CreateRoom failed — HTTP polling continues anyway.");
            }
        }

        private IEnumerator JoinFlow()
        {
            SetState(AppState.Lobby);

            // Same reasoning as HostFlow: don't gate the HTTP poll on Photon.
            BeginDrawWait();

            if (runner == null)
            {
                Debug.LogWarning("[GameStateManager] SessionRunner missing — cannot join Photon room, but HTTP polling continues.");
                yield break;
            }

            Task<bool> t = runner.JoinRoom(sessionCode);
            yield return new WaitUntil(() => t.IsCompleted);

            if (!t.Result)
            {
                Debug.LogWarning("[GameStateManager] JoinRoom failed — HTTP polling continues anyway.");
            }
        }

        private void BeginDrawWait()
        {
            SetState(AppState.DrawWait);

            if (qrDisplays == null || qrDisplays.Length == 0)
            {
                Debug.LogWarning("[GameStateManager] No QRCodeDisplay refs assigned.");
            }
            else
            {
                string baseUrl = config != null ? config.backendBaseUrl : string.Empty;
                int slot = localCasterIndex + 1; // /join/CODE?p=1 for host, p=2 for joiner
                foreach (var qr in qrDisplays)
                {
                    if (qr != null)
                    {
                        qr.ShowCode(baseUrl, sessionCode, slot);
                    }
                }
            }

            // Wire each tablet anchor's expected payload to the join URL for marker-based detection.
            if (tabletAnchors != null)
            {
                string url = qrDisplays != null && qrDisplays.Length > 0 && qrDisplays[0] != null
                    ? qrDisplays[0].LastUrl
                    : sessionCode;
                foreach (var t in tabletAnchors)
                {
                    if (t != null)
                    {
                        t.ExpectedQrPayload = url;
                    }
                }
            }

            if (apiClient == null)
            {
                Debug.LogWarning("[GameStateManager] SessionApiClient missing — cannot poll.");
                return;
            }

            StartCoroutine(apiClient.PollUntilReady(sessionCode, OnPollUpdate, OnBothPlayersReady));
        }

        private void OnPollUpdate(SessionData data)
        {
            if (data == null)
            {
                return;
            }

            if (eggs != null)
            {
                if (data.p1 != null && eggs.Length > 0 && eggs[0] != null)
                {
                    eggs[0].progress01 = MapStatus(data.p1.status);
                }
                if (data.p2 != null && eggs.Length > 1 && eggs[1] != null)
                {
                    eggs[1].progress01 = MapStatus(data.p2.status);
                }
            }
        }

        private void OnBothPlayersReady(SessionData data)
        {
            StartCoroutine(SpawnFlow(data));
        }

        private IEnumerator SpawnFlow(SessionData data)
        {
            SetState(AppState.Hatch);

            // Balance the two players' core stats (HP, attack, speed) so
            // neither has a numerical advantage from random color → stat
            // assignment. Element / moves / personality stay per-player.
            if (data?.p1?.stats != null && data?.p2?.stats != null)
            {
                Tigerverse.Combat.StatsBalancer.Equalize(data.p1.stats, data.p2.stats);
                Debug.Log($"[GameStateManager] Stats balanced: hp={data.p1.stats.hp} atk={data.p1.stats.attackMult:F2} speed={data.p1.stats.speed:F2}");
            }

            if (modelFetcher == null)
            {
                Debug.LogWarning("[GameStateManager] ModelFetcher missing — cannot spawn monsters.");
                yield break;
            }

            Transform anchorA = tabletAnchors != null && tabletAnchors.Length > 0 && tabletAnchors[0] != null
                ? tabletAnchors[0].anchorTransform : null;
            Transform anchorB = tabletAnchors != null && tabletAnchors.Length > 1 && tabletAnchors[1] != null
                ? tabletAnchors[1].anchorTransform : null;

            bool fetchADone = false;
            bool fetchBDone = false;
            monsterAGo = null;
            monsterBGo = null;

            if (data?.p1 != null && anchorA != null)
            {
                StartCoroutine(modelFetcher.Fetch(data.p1, anchorA, (go, err) =>
                {
                    if (err != ModelFetcher.FetchError.None)
                        Debug.LogWarning($"[GameStateManager] p1 fetch error: {err}");
                    monsterAGo = go;
                    fetchADone = true;
                }));
            }
            else { fetchADone = true; }

            if (data?.p2 != null && anchorB != null)
            {
                StartCoroutine(modelFetcher.Fetch(data.p2, anchorB, (go, err) =>
                {
                    if (err != ModelFetcher.FetchError.None)
                        Debug.LogWarning($"[GameStateManager] p2 fetch error: {err}");
                    monsterBGo = go;
                    fetchBDone = true;
                }));
            }
            else { fetchBDone = true; }

            yield return new WaitUntil(() => fetchADone && fetchBDone);

            // Cache per-monster cry/projector references.
            MonsterCry cryA = monsterAGo != null ? monsterAGo.GetComponentInChildren<MonsterCry>() : null;
            MonsterCry cryB = monsterBGo != null ? monsterBGo.GetComponentInChildren<MonsterCry>() : null;
            if (cries != null)
            {
                if (cries.Length > 0) cries[0] = cryA;
                if (cries.Length > 1) cries[1] = cryB;
            }

            // Drawing projector lookup (just to ensure it’s present and ready).
            _ = monsterAGo != null ? monsterAGo.GetComponentInChildren<DrawingProjector>() : null;
            _ = monsterBGo != null ? monsterBGo.GetComponentInChildren<DrawingProjector>() : null;

            // Run hatch sequences in parallel and wait for both to complete.
            bool aDone = false;
            bool bDone = false;

            Vector3 spawnA = anchorA != null ? anchorA.position : Vector3.zero;
            Vector3 spawnB = anchorB != null ? anchorB.position : Vector3.zero;

            if (eggs != null && eggs.Length > 0 && eggs[0] != null)
            {
                StartCoroutine(eggs[0].BeginHatchSequence(monsterAGo, spawnA, () =>
                {
                    cryA?.PlaySpawn();
                    aDone = true;
                }));
            }
            else
            {
                // No egg wired — still fire the spawn cry directly.
                cryA?.PlaySpawn();
                aDone = true;
            }

            if (eggs != null && eggs.Length > 1 && eggs[1] != null)
            {
                StartCoroutine(eggs[1].BeginHatchSequence(monsterBGo, spawnB, () =>
                {
                    cryB?.PlaySpawn();
                    bDone = true;
                }));
            }
            else
            {
                cryB?.PlaySpawn();
                bDone = true;
            }

            yield return new WaitUntil(() => aDone && bDone);

            // Build stat SOs from session data.
            statsA = data?.p1?.stats != null ? MonsterStatsSO.FromData(data.p1.stats, catalog) : null;
            statsB = data?.p2?.stats != null ? MonsterStatsSO.FromData(data.p2.stats, catalog) : null;

            // ─── Inspection phase ───────────────────────────────────────
            // Player can walk around their scribble, hover for stats, and
            // confirm via READY button / voice / fist-bump before battle.
            yield return RunInspectionPhase(spawnA, spawnB);

            // ─── Pokemon-style VS transition cutscene ───────────────────
            yield return RunVsCutscene(monsterAGo, data?.p1?.name, monsterBGo, data?.p2?.name);

            // Pre-battle reveals (sequential).
            SetState(AppState.PreBattleReveal);
            if (revealCards != null && revealCards.Length > 0 && revealCards[0] != null)
            {
                yield return revealCards[0].Show(statsA, cryA);
            }
            if (revealCards != null && revealCards.Length > 1 && revealCards[1] != null)
            {
                yield return revealCards[1].Show(statsB, cryB);
            }

            // Tint HP bars by element.
            if (hpBars != null)
            {
                if (hpBars.Length > 0 && hpBars[0] != null && statsA != null)
                {
                    hpBars[0].SetElementColor(statsA.element);
                    hpBars[0].SetHP(statsA.maxHP, statsA.maxHP);
                }
                if (hpBars.Length > 1 && hpBars[1] != null && statsB != null)
                {
                    hpBars[1].SetElementColor(statsB.element);
                    hpBars[1].SetHP(statsB.maxHP, statsB.maxHP);
                }
            }

            SetState(AppState.Battle);

            // Stand up the trainer/scribble control toggle for the battle.
            // Idempotent: tears down any prior manager (e.g. from a rematch).
            SetupBattleControlMode();

            // Snap each player behind their own monster so they can see both
            // creatures in front of them with a clear sightline. Order
            // matters: SetupBattleControlMode disabled the move providers, so
            // setting the rig position now won't be immediately undone by a
            // joystick frame.
            PositionPlayerBehindMonster();

            if (battle != null)
            {
                battle.Initialize(statsA, statsB);
                battle.OnHPChanged.AddListener(HandleHPChanged);
                battle.OnBattleEnd.AddListener(HandleBattleEnd);

                if (voiceRouter != null)
                {
                    var statsForLocal = localCasterIndex == 0 ? statsA : statsB;
                    voiceRouter.Bind(battle, localCasterIndex, statsForLocal != null ? statsForLocal.moves : null);

                    // Announce the local player's moves via TTS so they know what to call out.
                    var ann = FindFirstObjectByType<Tigerverse.Voice.Announcer>();
                    if (ann != null && statsForLocal != null && statsForLocal.moves != null && statsForLocal.moves.Length > 0)
                    {
                        var moveNames = new System.Collections.Generic.List<string>();
                        foreach (var m in statsForLocal.moves) if (m != null) moveNames.Add(m.displayName);
                        string list = string.Join(", ", moveNames);
                        ann.Say($"Your monster {statsForLocal.displayName} knows: {list}. Press grip and shout a move name to attack.");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[GameStateManager] BattleManager missing — battle will not start.");
            }
        }

        // Show the local player a "READY!" prompt next to their monster.
        // Resolves once the player confirms (button press, voice, or fist
        // bump). Multiplayer note: each client runs this independently for
        // its own player; both clients will progress past this before the
        // pre-battle reveals fire.
        private IEnumerator RunInspectionPhase(Vector3 spawnA, Vector3 spawnB)
        {
            // Decide which spawn point belongs to the LOCAL player.
            Vector3 localSpawn = localCasterIndex == 0 ? spawnA : spawnB;

            var hsGo = new GameObject("ReadyHandshake");
            hsGo.transform.position = Vector3.zero;
            var handshake = hsGo.AddComponent<Combat.ReadyHandshake>();

            // Position the button just to the right of the local player's
            // monster, at chest height.
            Vector3 btnPos = localSpawn + new Vector3(0.55f, 1.10f, 0f);
            Quaternion btnRot = Quaternion.identity;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - btnPos;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-4f)
                    btnRot = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
            handshake.Configure(btnPos, btnRot);

            bool ready = false;
            handshake.OnLocalReady += () => ready = true;

            Debug.Log("[GameStateManager] Inspection phase active — waiting for local player to ready up.");
            // Hard cap so we never deadlock if voice + button + bump all fail.
            float deadline = Time.time + 90f;
            while (!ready && Time.time < deadline) yield return null;
            if (!ready) Debug.LogWarning("[GameStateManager] Inspection phase timed out — auto-advancing to battle.");

            if (hsGo != null) Destroy(hsGo);

            // Snap the player into Pokemon-battle stance the *moment* they
            // ready up (before the VS cutscene plays), so the cutscene's
            // snapshot of where their monster sits matches where they'll
            // actually be standing in the fight.
            PositionPlayerBehindMonster();
        }

        // Pokemon-style "VS" transition. Shows snapshots of both monsters
        // side by side with their player names and big yellow VS letters,
        // then yields back to SpawnFlow once the cutscene finishes.
        private IEnumerator RunVsCutscene(GameObject monsterA, string nameA,
                                          GameObject monsterB, string nameB)
        {
            var go = new GameObject("VsCutscene");
            var vs = go.AddComponent<UI.VsCutscene>();

            bool done = false;
            yield return vs.Play(monsterA, string.IsNullOrEmpty(nameA) ? "Player 1" : nameA,
                                 monsterB, string.IsNullOrEmpty(nameB) ? "Player 2" : nameB,
                                 () => done = true);
            // vs.Play already waits for cutscene completion + destroys self.
        }

        // ─── Battle stance: park the local trainer behind their monster ───
        // Sightline becomes: [trainer camera] -> [own monster] -> [opponent].
        // Called twice — once when the player readies up (before the VS
        // cutscene) so the snap is instant from the player's POV, and again
        // at SetState(AppState.Battle) as a backstop against any rig
        // movement that snuck in during the cutscene. Locomotion is locked
        // by SetupBattleControlMode, so the player stays parked here for
        // the rest of the fight.
        private void PositionPlayerBehindMonster()
        {
            GameObject localMonster    = localCasterIndex == 0 ? monsterAGo : monsterBGo;
            GameObject opponentMonster = localCasterIndex == 0 ? monsterBGo : monsterAGo;
            Combat.BattleStance.PositionBehindMonster(localMonster, opponentMonster);
        }

        // ─── Battle control mode (Trainer / Scribble) ──────────────────────
        // The local player toggles with A. In Trainer mode they shout move
        // names; in Scribble mode they steer the local scribble with the
        // left stick to dodge. The trainer's body locomotion is locked in
        // BOTH modes — the toggle only changes who the joystick drives and
        // whether voice attacks fire.
        private void SetupBattleControlMode()
        {
            // Tear down any prior instance (rematch / re-entry into Battle).
            var prior = BattleControlModeManager.Instance;
            if (prior != null) Destroy(prior.gameObject);
            var priorOverlay = GameObject.Find("BattleModeOverlay");
            if (priorOverlay != null) Destroy(priorOverlay);

            // Local scribble = the monster GO that belongs to this client.
            GameObject localScribble = localCasterIndex == 0 ? monsterAGo : monsterBGo;
            if (localScribble == null)
            {
                Debug.LogWarning("[GameStateManager] No local scribble GO — battle control mode setup skipped.");
                return;
            }

            // Resolve XR rig + locomotion components.
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider xrMove = null;
            FlatMoveController editorMove = null;
            if (origin != null)
            {
                xrMove = origin.gameObject.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>(true);
                editorMove = origin.GetComponent<FlatMoveController>();
            }

            // Add the joystick→scribble driver. Disabled here; manager flips
            // it on when the player switches to Scribble mode. Hand it the
            // XR rig so it can snap the camera into the scribble's POV.
            var scribbleMover = localScribble.GetComponent<ScribbleMoveController>();
            if (scribbleMover == null) scribbleMover = localScribble.AddComponent<ScribbleMoveController>();
            scribbleMover.target = localScribble.transform;
            scribbleMover.xrOrigin = origin != null ? origin.transform : null;
            scribbleMover.enabled = false;

            // Head-locked banner that flashes the new mode name on toggle.
            var overlayGo = new GameObject("BattleModeOverlay", typeof(RectTransform));
            var overlay = overlayGo.AddComponent<BattleModeOverlay>();

            // Manager itself.
            var mgrGo = new GameObject("BattleControlModeManager");
            var mgr = mgrGo.AddComponent<BattleControlModeManager>();
            mgr.Configure(xrMove, editorMove, scribbleMover, overlay);

            // Battle HUD: 4 moves on the right + event log on the left.
            var hudGo = new GameObject("BattleHUD", typeof(RectTransform));
            var hud = hudGo.AddComponent<BattleHUD>();
            hud.Configure(mgr, voiceRouter);

            // Aim-and-cast controller on the local monster. Reticle hovers
            // in front in Scribble mode; voice-cast spawns a projectile
            // toward the reticle. Cooldown enforced inside the controller.
            GameObject opponentMonster = localCasterIndex == 0 ? monsterBGo : monsterAGo;
            var aim = localScribble.GetComponent<Combat.MonsterAimController>();
            if (aim == null) aim = localScribble.AddComponent<Combat.MonsterAimController>();
            aim.opponent     = opponentMonster != null ? opponentMonster.transform : null;
            aim.casterIndex  = localCasterIndex;
            aim.battle       = battle;
        }

        private void HandleHPChanged(int hpA, int maxA, int hpB, int maxB)
        {
            if (hpBars == null) return;
            if (hpBars.Length > 0 && hpBars[0] != null) hpBars[0].SetHP(hpA, maxA);
            if (hpBars.Length > 1 && hpBars[1] != null) hpBars[1].SetHP(hpB, maxB);
        }

        private void HandleBattleEnd(int winnerIndex)
        {
            SetState(AppState.Result);

            if (battle != null)
            {
                battle.OnHPChanged.RemoveListener(HandleHPChanged);
                battle.OnBattleEnd.RemoveListener(HandleBattleEnd);
            }

            StartCoroutine(EndSequence(winnerIndex));
        }

        private IEnumerator EndSequence(int winnerIndex)
        {
            // Allow a frame so the result UI can settle, then capture screenshot.
            yield return null;
            string fileName = $"tigerverse_result_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            ScreenCapture.CaptureScreenshot(fileName);

            // Surface a download QR. Reuse first QR display, point to a backend recap URL.
            if (qrDisplays != null && qrDisplays.Length > 0 && qrDisplays[0] != null && config != null)
            {
                qrDisplays[0].ShowCode(config.backendBaseUrl, $"recap/{sessionCode}?winner={winnerIndex}");
            }
        }

        private float MapStatus(string s)
        {
            switch (s)
            {
                case "queued": return 0.05f;
                case "generating": return 0.30f;
                case "rigging": return 0.60f;
                case "cry": return 0.85f;
                case "ready": return 1.0f;
                case "error": return 0.0f;
                default: return 0.0f;
            }
        }

        private void SetState(AppState s)
        {
            currentState = s;
            OnStateChanged?.Invoke(s);
        }
    }
}
