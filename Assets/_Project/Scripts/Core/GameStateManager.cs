using System;
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

            // Surface "opponent disconnected" once they leave the Photon room
            // mid-match, so the surviving client doesn't sit there waiting on
            // a player who's never coming back. Wired here in Awake so the
            // subscription survives state changes (battle → result → menu).
            if (runner != null) runner.OnOpponentLeft += HandleOpponentLeft;

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
            Tigerverse.Voice.LobbyMusicPlayer.Instance.Play();

            // HTTP session polling (which drives the eggs / tutorial / hatch)
            // is independent of Photon. Kick it off immediately so the player
            // sees their own egg appear the moment the website registers
            // their drawing, even if Photon connect is slow, fails, or
            // hasn't been configured (empty photonAppId in BackendConfig).
            BeginDrawWait();

            if (runner == null)
            {
                Debug.LogWarning("[GameStateManager] SessionRunner missing, Photon room won't host, but HTTP polling continues.");
                yield break;
            }

            Task<bool> t = runner.CreateRoom(sessionCode);
            yield return new WaitUntil(() => t.IsCompleted);

            // Read t.Result inside try/catch — a faulted Task re-throws on
            // Result access, which would otherwise terminate the coroutine
            // and break Photon room creation entirely. Faulted tasks now
            // surface as a logged exception with HTTP polling continuing.
            bool createOk = false;
            try { createOk = t.Result; }
            catch (Exception e) { Debug.LogError($"[GameStateManager] CreateRoom threw: {e.Message}"); }
            if (!createOk)
                Debug.LogWarning("[GameStateManager] CreateRoom failed, HTTP polling continues anyway.");
        }

        private IEnumerator JoinFlow()
        {
            SetState(AppState.Lobby);
            Tigerverse.Voice.LobbyMusicPlayer.Instance.Play();

            // Same reasoning as HostFlow: don't gate the HTTP poll on Photon.
            BeginDrawWait();

            if (runner == null)
            {
                Debug.LogWarning("[GameStateManager] SessionRunner missing, cannot join Photon room, but HTTP polling continues.");
                yield break;
            }

            Task<bool> t = runner.JoinRoom(sessionCode);
            yield return new WaitUntil(() => t.IsCompleted);

            bool joinOk = false;
            try { joinOk = t.Result; }
            catch (Exception e) { Debug.LogError($"[GameStateManager] JoinRoom threw: {e.Message}"); }
            if (!joinOk)
                Debug.LogWarning("[GameStateManager] JoinRoom failed, HTTP polling continues anyway.");
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
                Debug.LogWarning("[GameStateManager] SessionApiClient missing, cannot poll.");
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
                Debug.LogWarning("[GameStateManager] ModelFetcher missing, cannot spawn monsters.");
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
                // No egg wired, still fire the spawn cry directly.
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

            // Build stat SOs from session data. ALWAYS call FromData even if
            // stats are missing — FromData fills the moveset to 4 with sane
            // defaults (Fireball/Watergun/Thunderbolt/Iceshard + Dodge) when
            // the backend hasn't returned a usable kit yet. Without this the
            // wrist HUD would be empty and voice commands wouldn't match
            // anything because availableMoves was null.
            statsA = MonsterStatsSO.FromData(data?.p1?.stats, catalog, data?.p1?.name);
            statsB = MonsterStatsSO.FromData(data?.p2?.stats, catalog, data?.p2?.name);

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

            Debug.Log($"[GameStateManager] Entering battle. statsA={(statsA!=null?statsA.displayName:"NULL")} statsA.moves.Length={(statsA?.moves?.Length ?? -1)} statsB={(statsB!=null?statsB.displayName:"NULL")} statsB.moves.Length={(statsB?.moves?.Length ?? -1)}");

            // ─── Resolve the local player's moveset ───────────────────────
            // Done UNCONDITIONALLY (outside the battle-null check) so the
            // wrist HUD always shows real names even if the BattleManager
            // ref isn't wired in the scene. The bulletproof fallback chain:
            //   stats.moves → catalog.Find by name → catalog.moves[0..3]
            var statsForLocal = localCasterIndex == 0 ? statsA : statsB;
            var movesForLocal = statsForLocal?.moves;

            if (movesForLocal == null || movesForLocal.Length == 0)
            {
                Debug.LogWarning($"[GameStateManager] statsForLocal.moves was null/empty — falling back to MoveCatalog defaults so battle is playable.");
                var fbCatalog = catalog != null ? catalog : MoveCatalog.Instance;
                if (fbCatalog != null)
                {
                    var list = new System.Collections.Generic.List<MoveSO>();
                    string[] defaults = { "Fireball", "Watergun", "Thunderbolt", "Iceshard" };
                    for (int i = 0; i < defaults.Length; i++)
                    {
                        var m = fbCatalog.Find(defaults[i]);
                        if (m != null) list.Add(m);
                    }
                    if (list.Count == 0 && fbCatalog.moves != null)
                    {
                        // Catalog is missing the named moves entirely, grab the first 4 it has.
                        for (int i = 0; i < fbCatalog.moves.Length && list.Count < 4; i++)
                            if (fbCatalog.moves[i] != null) list.Add(fbCatalog.moves[i]);
                    }
                    movesForLocal = list.ToArray();
                }
            }

            // ─── Find a BattleManager — inspector ref OR scene search ───
            // If the SerializeField wasn't wired, search the scene so RPC-
            // backed damage still works. Without this, voice would match but
            // the SubmitMove call would no-op silently. Spawn is async on the
            // master client (via SessionRunner.StartShared → Runner.Spawn) and
            // replication takes a few Fusion ticks to fan out to joiners, so
            // we poll for it instead of failing on the first miss — fixes the
            // race where SpawnFlow ran before the BattleManager NetworkObject
            // was visible to this client and we ended up with a null battle
            // (no Initialize, no Bind → voice match worked but SubmitMove went
            // nowhere or hit an uninitialised BattleManager).
            var resolvedBattle = battle;
            if (resolvedBattle == null)
            {
                const float waitForBattleManagerSec = 5f;
                float deadline = Time.time + waitForBattleManagerSec;
                while (Time.time < deadline)
                {
                    resolvedBattle = FindFirstObjectByType<BattleManager>();
                    if (resolvedBattle != null) break;
                    yield return new WaitForSeconds(0.20f);
                }
                if (resolvedBattle != null)
                {
                    Debug.LogWarning($"[GameStateManager] battle SerializeField was null — found one in scene after wait: {resolvedBattle.name}");
                }
                else
                {
                    Debug.LogError("[GameStateManager] No BattleManager in scene after waiting 5s for Fusion replication. Damage cannot apply. Check Bootstrap.SessionRunner.battleManagerPrefab is wired AND the master client successfully started Photon.");
                }
            }

            // ─── Initialize and bind ──────────────────────────────────────
            if (resolvedBattle != null)
            {
                // Wire the runtime-spawned monster transforms + cries into the
                // BattleManager. Without these, PlayMoveSequence early-exits
                // every cast because casterPivot/defenderPivot are null —
                // which is why moves had no animation, no SFX, and no
                // damage-popup. The prefab can't carry these refs because
                // they only exist after MonsterSpawnSlotPresenter spawns the
                // hatch-revealed monsters at runtime.
                resolvedBattle.monsterAPivot = (monsterAGo != null) ? monsterAGo.transform : null;
                resolvedBattle.monsterBPivot = (monsterBGo != null) ? monsterBGo.transform : null;
                resolvedBattle.cryA = (monsterAGo != null) ? monsterAGo.GetComponentInChildren<MonsterCry>() : null;
                resolvedBattle.cryB = (monsterBGo != null) ? monsterBGo.GetComponentInChildren<MonsterCry>() : null;
                Debug.Log($"[GameStateManager] BattleManager pivot wiring: monsterAPivot={(resolvedBattle.monsterAPivot != null ? "OK" : "NULL")} monsterBPivot={(resolvedBattle.monsterBPivot != null ? "OK" : "NULL")}");

                resolvedBattle.Initialize(statsA, statsB);
                resolvedBattle.OnHPChanged.RemoveListener(HandleHPChanged);  // idempotent on rematch
                resolvedBattle.OnHPChanged.AddListener(HandleHPChanged);
                resolvedBattle.OnBattleEnd.RemoveListener(HandleBattleEnd);
                resolvedBattle.OnBattleEnd.AddListener(HandleBattleEnd);

                // Battle commentator intentionally disabled per request — was
                // talking over the win banner and the move audio. If we want
                // it back, reinstate the AddComponent + Bind block below.
                // Also unbind any existing commentator so a previous match's
                // listeners don't keep firing.
                var staleCommentator = GetComponent<Tigerverse.Combat.BattleCommentator>();
                if (staleCommentator != null) staleCommentator.Unbind();
            }

            if (voiceRouter != null)
            {
                voiceRouter.Bind(resolvedBattle, localCasterIndex, movesForLocal);
                Debug.Log($"[GameStateManager] Battle voice bound. caster={localCasterIndex} battle={(resolvedBattle != null ? "OK" : "NULL")} statsForLocal={(statsForLocal!=null?statsForLocal.displayName:"NULL")} moves.Length={(movesForLocal?.Length ?? -1)} firstMove={(movesForLocal != null && movesForLocal.Length>0 ? movesForLocal[0]?.displayName : "<none>")}");

                // Force a clean mic state for combat — kills any wedged VAD
                // session inherited from tutorial / ready-handshake teardown.
                voiceRouter.SetMuted(false);
                voiceRouter.SetOpenMicMode(true);
                voiceRouter.RestartMic();

                // Announce the local player's moves via TTS so they know what to call out.
                var ann = FindFirstObjectByType<Tigerverse.Voice.Announcer>();
                if (ann != null && movesForLocal != null && movesForLocal.Length > 0)
                {
                    var moveNames = new System.Collections.Generic.List<string>();
                    foreach (var m in movesForLocal) if (m != null) moveNames.Add(m.displayName);
                    string list = string.Join(", ", moveNames);
                    string monsterName = statsForLocal != null ? statsForLocal.displayName : "Your monster";
                    ann.Say($"{monsterName} knows: {list}. Just shout a move name to attack.");
                }
            }
            else
            {
                Debug.LogError("[GameStateManager] voiceRouter SerializeField is null — voice attacks cannot work.");
            }

            // Lock the trainer's locomotion + spawn the head-locked battle
            // HUD. Now runs AFTER voice bind so the HUD's first Configure
            // call reads real moves directly. Idempotent on rematch.
            SetupBattleLocomotionAndHud();

            // Snap each player behind their own monster so they can see both
            // creatures in front of them with a clear sightline. Order
            // matters: locomotion was just disabled, so setting the rig
            // position now won't be immediately undone by a joystick frame.
            PositionPlayerBehindMonster();
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

            Debug.Log("[GameStateManager] Inspection phase active, waiting for local player to ready up.");
            // Hard cap so we never deadlock if voice + button + bump all fail.
            float deadline = Time.time + 90f;
            while (!ready && Time.time < deadline) yield return null;
            if (!ready) Debug.LogWarning("[GameStateManager] Inspection phase timed out, auto-advancing to battle.");

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
        // Called twice, once when the player readies up (before the VS
        // cutscene) so the snap is instant from the player's POV, and again
        // at SetState(AppState.Battle) as a backstop against any rig
        // movement that snuck in during the cutscene. Locomotion is locked
        // by SetupBattleLocomotionAndHud, so the player stays parked here for
        // the rest of the fight.
        private void PositionPlayerBehindMonster()
        {
            GameObject localMonster    = localCasterIndex == 0 ? monsterAGo : monsterBGo;
            GameObject opponentMonster = localCasterIndex == 0 ? monsterBGo : monsterAGo;
            Combat.BattleStance.PositionBehindMonster(localMonster, opponentMonster);
        }

        // ─── Battle locomotion lock + HUD ─────────────────────────────────
        // Battle is voice-only now: the player stays parked behind their
        // monster and shouts move names to attack. Per-move cooldowns
        // (enforced inside VoiceCommandRouter) keep stronger moves from
        // being spammed. There's no aim, no movement mode, no toggle.
        private void SetupBattleLocomotionAndHud()
        {
            // Tear down a prior HUD (rematch / re-entry into Battle).
            var priorHud = GameObject.Find("BattleHUD");
            if (priorHud != null) Destroy(priorHud);

            // Resolve XR rig + locomotion components and turn them off so
            // the player can't wander mid-fight.
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                var xrMove = origin.gameObject.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>(true);
                if (xrMove != null) xrMove.enabled = false;
                var editorMove = origin.GetComponent<FlatMoveController>();
                if (editorMove != null) editorMove.enabled = false;
            }

            // Battle HUD: 4 moves on the right + event log on the left.
            var hudGo = new GameObject("BattleHUD", typeof(RectTransform));
            var hud = hudGo.AddComponent<BattleHUD>();
            hud.Configure(voiceRouter);

            // Hand off the soundtrack: lobby music fades out, battle music
            // fades in so the two never overlap.
            Tigerverse.Voice.LobbyMusicPlayer.Instance.Stop();
            Tigerverse.Voice.BattleMusicPlayer.Instance.Play();
        }

        private int _lastSeenLocalHp = -1;

        private void HandleHPChanged(int hpA, int maxA, int hpB, int maxB)
        {
            if (hpBars != null)
            {
                if (hpBars.Length > 0 && hpBars[0] != null) hpBars[0].SetHP(hpA, maxA);
                if (hpBars.Length > 1 && hpBars[1] != null) hpBars[1].SetHP(hpB, maxB);
            }

            // Subtle controller haptics when the LOCAL player's monster takes
            // damage. Sells the impact way more than just a bar tick. We
            // pulse both controllers briefly; amplitude/duration tuned to
            // feel like a tap, not a buzz.
            int localHp = (localCasterIndex == 0) ? hpA : hpB;
            if (_lastSeenLocalHp >= 0 && localHp < _lastSeenLocalHp)
            {
                int delta = _lastSeenLocalHp - localHp;
                // Bigger hits get a slightly stronger pulse, capped.
                float amp = Mathf.Clamp(0.35f + delta * 0.015f, 0.35f, 0.85f);
                PulseControllerHaptics(amp, 0.12f);

                // Visual punch on top of the haptics — only for hits big
                // enough to feel weighty (>20 damage). Smaller chip damage
                // stays subtle so the screen isn't constantly red-flashing.
                if (delta > 20) Tigerverse.UI.HitImpactFx.Trigger(delta);
            }
            _lastSeenLocalHp = localHp;
        }

        private static void PulseControllerHaptics(float amplitude, float duration)
        {
            PulseOne(UnityEngine.XR.XRNode.LeftHand,  amplitude, duration);
            PulseOne(UnityEngine.XR.XRNode.RightHand, amplitude, duration);
        }

        private static void PulseOne(UnityEngine.XR.XRNode node, float amplitude, float duration)
        {
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!dev.isValid) return;
            if (!dev.TryGetHapticCapabilities(out var caps) || !caps.supportsImpulse) return;
            dev.SendHapticImpulse(0, Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration));
        }

        // ─── Disconnect handling ────────────────────────────────────────
        private bool _opponentLeftHandled;

        private void HandleOpponentLeft()
        {
            // Only act during an active match — after a clean win the room
            // teardown will fire OnPlayerLeft for the other side too and we
            // don't want to spam an overlay during the normal return flow.
            if (currentState != AppState.Battle && currentState != AppState.Hatch
                && currentState != AppState.DrawWait && currentState != AppState.Lobby) return;
            if (_opponentLeftHandled) return;
            _opponentLeftHandled = true;
            Debug.LogWarning("[GameStateManager] Opponent left mid-match. Showing disconnect overlay and force-returning to title in 10s.");
            StartCoroutine(OpponentLeftFlow());
        }

        private System.Collections.IEnumerator OpponentLeftFlow()
        {
            // Stop battle music + tear down listeners so a stray RPC arrival
            // can't fire a phantom OnBattleEnd while we're showing the
            // disconnect overlay.
            try { Tigerverse.Voice.BattleMusicPlayer.Instance.Stop(); } catch { }
            if (battle != null)
            {
                battle.OnHPChanged.RemoveListener(HandleHPChanged);
                battle.OnBattleEnd.RemoveListener(HandleBattleEnd);
            }

            // Reuse the WinScreenBanner shell — same canvas style, just
            // different text. Spawn one with the disconnect copy.
            try { Tigerverse.UI.WinScreenBanner.Spawn("Opponent disconnected"); }
            catch (Exception e) { Debug.LogWarning($"[GameStateManager] Disconnect banner spawn failed: {e.Message}"); }

            // Same backup-Invoke pattern as EndSequence.
            CancelInvoke(nameof(ForceReloadTitleSceneNow));
            Invoke(nameof(ForceReloadTitleSceneNow), 12f);

            yield return new WaitForSecondsRealtime(10f);
            yield return ReturnToTitle();
            CancelInvoke(nameof(ForceReloadTitleSceneNow));
            _opponentLeftHandled = false;
        }

        private bool _endSequenceStarted;

        private void HandleBattleEnd(int winnerIndex)
        {
            // Idempotency guard. BattleManager fires OnBattleEnd from both
            // RPC_PlayResolved and the Render-side WinnerIndex watcher (the
            // latter is a backup so non-authority clients still see the win
            // even if the RPC didn't fire their listener). Both paths can
            // race; we only want one EndSequence per match.
            if (_endSequenceStarted)
            {
                Debug.Log($"[GameStateManager] HandleBattleEnd called again with winner={winnerIndex}, ignoring (EndSequence already running).");
                return;
            }
            _endSequenceStarted = true;

            SetState(AppState.Result);

            // Fade out the battle background music and play a procedurally
            // synthesised victory sting (rising C-major arpeggio + sustained
            // chord) so the silence between the music cut and the win banner
            // doesn't feel like dead air. Plays at the local camera position
            // so it's audible 2D anywhere.
            Tigerverse.Voice.BattleMusicPlayer.Instance.Stop();
            try
            {
                var sting = Tigerverse.Combat.ProceduralMoveSfx.GetVictorySting();
                if (sting != null)
                {
                    Vector3 pos = (Camera.main != null) ? Camera.main.transform.position : Vector3.zero;
                    AudioSource.PlayClipAtPoint(sting, pos, 0.85f);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[GameStateManager] Victory sting playback threw: {e.Message}"); }

            if (battle != null)
            {
                battle.OnHPChanged.RemoveListener(HandleHPChanged);
                battle.OnBattleEnd.RemoveListener(HandleBattleEnd);
            }

            Debug.Log($"[GameStateManager] HandleBattleEnd winner={winnerIndex}, starting EndSequence (8s → return to title).");
            StartCoroutine(EndSequence(winnerIndex));
        }

        private const float ResultBannerSeconds = 8f;
        private const float ForceReloadBackupSeconds = 14f;

        private IEnumerator EndSequence(int winnerIndex)
        {
            Debug.Log($"[GameStateManager] EndSequence start. winner={winnerIndex} isMaster={(runner != null && runner.Runner != null && runner.Runner.IsSharedModeMasterClient)}");

            // Hard backup: if the coroutine stalls for any reason (timeScale
            // hitting 0, MonoBehaviour disable during Photon teardown,
            // exception inside ReturnToTitle, scene load no-op, etc.) fire
            // a force-reload via Invoke. Invoke runs independently of the
            // coroutine state and survives as long as Bootstrap (the
            // DontDestroyOnLoad host of GameStateManager) is alive.
            CancelInvoke(nameof(ForceReloadTitleSceneNow));
            Invoke(nameof(ForceReloadTitleSceneNow), ForceReloadBackupSeconds);

            // Allow a frame so the result UI can settle, then capture screenshot.
            yield return null;
            try
            {
                string fileName = $"tigerverse_result_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
                ScreenCapture.CaptureScreenshot(fileName);
            }
            catch (Exception e) { Debug.LogWarning($"[GameStateManager] Screenshot capture threw: {e.Message}"); }

            // Surface a download QR. Reuse first QR display, point to a backend recap URL.
            try
            {
                if (qrDisplays != null && qrDisplays.Length > 0 && qrDisplays[0] != null && config != null)
                    qrDisplays[0].ShowCode(config.backendBaseUrl, $"recap/{sessionCode}?winner={winnerIndex}");
            }
            catch (Exception e) { Debug.LogWarning($"[GameStateManager] QR ShowCode threw: {e.Message}"); }

            // Big floating "X WINS!" banner. The voice line is fired by the
            // BattleCommentator via OnBattleEnd, NOT by the banner itself
            // (used to overlap and made the audio unintelligible).
            string winnerName = (winnerIndex == 0)
                ? (statsA != null ? statsA.displayName : "Player 1")
                : (statsB != null ? statsB.displayName : "Player 2");
            try { Tigerverse.UI.WinScreenBanner.Spawn(winnerName); }
            catch (Exception e) { Debug.LogError($"[GameStateManager] WinScreenBanner.Spawn threw: {e.Message}"); }

            Debug.Log("[GameStateManager] EndSequence waiting 8s (realtime) before reloading title…");
            // Realtime so a Photon-disconnect handler tweaking Time.timeScale
            // can't strand the master on the win screen forever.
            yield return new WaitForSecondsRealtime(ResultBannerSeconds);
            Debug.Log("[GameStateManager] EndSequence 8s elapsed, calling ReturnToTitle.");

            // Tear down Photon and reload the title scene so both players
            // loop back to the PLAY/TUTORIAL/SETTINGS menu for a fresh match.
            // Bootstrap (with SessionRunner) is DontDestroyOnLoad so we shut
            // the runner down explicitly first — leaving it live across the
            // reload would have it try to spawn into a stale room state.
            yield return ReturnToTitle();
            Debug.Log("[GameStateManager] EndSequence finished.");
            // ReturnToTitle reaches LoadScene before this point — if we got
            // this far the backup Invoke is unnecessary.
            CancelInvoke(nameof(ForceReloadTitleSceneNow));
        }

        private void ForceReloadTitleSceneNow()
        {
            // Last-ditch fallback fired by Invoke when EndSequence didn't
            // complete its own scene reload within the budget. Skips the
            // Photon shutdown (best-effort) and just slams the Title scene
            // in. If Photon was holding state, the next StartShared on
            // SessionRunner will recycle the runner cleanly anyway.
            Debug.LogError("[GameStateManager] ForceReloadTitleSceneNow fired — coroutine stalled. Forcing scene reload.");
            sessionCode = null;
            statsA = null;
            statsB = null;
            monsterAGo = null;
            monsterBGo = null;
            _endSequenceStarted = false;
            try { UnityEngine.SceneManagement.SceneManager.LoadScene("Title"); }
            catch (Exception e) { Debug.LogError($"[GameStateManager] Force-reload LoadScene threw: {e.Message}"); }
        }

        private IEnumerator ReturnToTitle()
        {
            Debug.Log("[GameStateManager] ReturnToTitle: shutting down runner…");
            if (runner != null)
            {
                Task shutdown = null;
                try { shutdown = runner.Shutdown(); }
                catch (Exception e) { Debug.LogWarning($"[GameStateManager] runner.Shutdown threw synchronously: {e.Message}"); }
                if (shutdown != null)
                {
                    float deadline = Time.time + 3f;
                    yield return new WaitUntil(() => shutdown.IsCompleted || Time.time > deadline);
                    if (!shutdown.IsCompleted)
                        Debug.LogWarning("[GameStateManager] runner.Shutdown timed out after 3s, proceeding to reload anyway.");
                    else
                        Debug.Log("[GameStateManager] runner.Shutdown completed.");
                }
            }

            // Reset our local session state so a fresh hostFlow can start
            // without inheriting stale code / caster index / monster refs.
            sessionCode = null;
            statsA = null;
            statsB = null;
            monsterAGo = null;
            monsterBGo = null;
            _endSequenceStarted = false;
            _lastSeenLocalHp = -1;
            _opponentLeftHandled = false;
            SetState(AppState.Title);

            // Reload by scene NAME — buildIndex returns -1 if the scene is
            // missing from Build Settings, which previously caused
            // SceneManager.LoadScene to silently no-op on the master and
            // leave them stuck on the win banner forever while the joiner
            // (whose scene index happened to resolve) reloaded fine.
            const string titleSceneName = "Title";
            Debug.Log($"[GameStateManager] ReturnToTitle: loading scene '{titleSceneName}' now.");
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(titleSceneName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameStateManager] LoadScene('{titleSceneName}') threw: {e.Message}. Falling back to active-scene buildIndex reload.");
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (active.buildIndex >= 0)
                    UnityEngine.SceneManagement.SceneManager.LoadScene(active.buildIndex);
                else
                    Debug.LogError($"[GameStateManager] Active scene '{active.name}' has buildIndex={active.buildIndex}. Add the Title scene to Build Settings to fix the loop-back.");
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
