using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

#if FUSION2
using Fusion;
using Fusion.Sockets;
#endif

namespace Tigerverse.Net
{
#if FUSION2
    /// <summary>
    /// Owns the Fusion NetworkRunner, hosts/joins a Shared-mode room by code,
    /// and spawns the SessionManager once connected (host only).
    /// </summary>
    // No [RequireComponent(typeof(NetworkRunner))] — we destroy + recreate it on each
    // host/join cycle (Fusion runners are single-use), and RequireComponent would block DestroyImmediate.
    public class SessionRunner : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Tooltip("Prefab containing the SessionManager NetworkBehaviour. Drag the SessionManager.prefab here.")]
        public GameObject sessionManagerPrefab;

        [SerializeField] private BackendConfig config;

        public int playerCount = 2;

        public NetworkRunner Runner { get; private set; }

        public event Action OnRunnerConnected;
        public event Action OnRunnerDisconnected;

        // Last session code we successfully started — used by the
        // headset-removed → resume rejoin path so we know what room
        // to dial back into.
        private string _lastSessionCode;
        // While true we're in the middle of an automatic rejoin and
        // shouldn't fire another one in parallel.
        private bool _rejoining;

        private void Awake()
        {
            if (config == null) config = BackendConfig.Load();
            Runner = GetComponent<NetworkRunner>();
            if (Runner == null) Runner = gameObject.AddComponent<NetworkRunner>();
            Runner.AddCallbacks(this);
        }

        public Task<bool> CreateRoom(string code) => StartShared(code);
        public Task<bool> JoinRoom(string code) => StartShared(code);

        private bool _starting;

        private async Task<bool> StartShared(string code)
        {
            if (_starting)
            {
                Debug.LogWarning("[SessionRunner] StartShared ignored — already starting.");
                return false;
            }
            _starting = true;
            try
            {
                if (string.IsNullOrEmpty(code))
                {
                    Debug.LogWarning("[SessionRunner] StartShared called with empty code; aborting.");
                    return false;
                }

                // Fusion's NetworkRunner is single-use. If it's already been used (started or shut down),
                // destroy it and create a fresh component on the same GameObject.
                if (Runner != null && (Runner.IsRunning || Runner.IsShutdown))
                {
                    Debug.Log("[SessionRunner] Recycling NetworkRunner (was used previously).");
                    if (Runner.IsRunning) await Runner.Shutdown();
                    DestroyImmediate(Runner);
                    Runner = null;
                }
                if (Runner == null)
                {
                    Runner = GetComponent<NetworkRunner>();
                    if (Runner == null)
                    {
                        Runner = gameObject.AddComponent<NetworkRunner>();
                    }
                    if (Runner == null)
                    {
                        Debug.LogError("[SessionRunner] Failed to add NetworkRunner. RequireComponent or DisallowMultipleComponent conflict?");
                        return false;
                    }
                    Runner.AddCallbacks(this);
                }

                Debug.Log($"[SessionRunner] StartShared session='{code}' mode=Shared. AppId set: {!string.IsNullOrEmpty(Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion)}");
                Runner.ProvideInput = true;

                // Remember the last code we started so we can rejoin
                // automatically when the player puts the headset back on
                // after taking it off to draw / browse on their phone.
                _lastSessionCode = code;

            // Scene manager: prefer a NetworkSceneManagerDefault on this GameObject.
            var sceneManager = GetComponent<INetworkSceneManager>();
            if (sceneManager == null)
            {
                sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
            }

            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = code,
                PlayerCount = playerCount,
                SceneManager = sceneManager
            };

            var result = await Runner.StartGame(args);
            if (!result.Ok)
            {
                Debug.LogError($"[SessionRunner] StartGame failed: {result.ShutdownReason} {result.ErrorMessage}");
                return false;
            }

            // Only the Shared-mode master client (initial host) spawns the singleton SessionManager.
            if (Runner.IsSharedModeMasterClient && sessionManagerPrefab != null)
            {
                var no = sessionManagerPrefab.GetComponent<NetworkObject>();
                if (no != null)
                {
                    Runner.Spawn(no, Vector3.zero, Quaternion.identity, Runner.LocalPlayer);
                }
                else
                {
                    Debug.LogError("[SessionRunner] sessionManagerPrefab has no NetworkObject component.");
                }
            }
            else if (Runner.IsSharedModeMasterClient && sessionManagerPrefab == null)
            {
                Debug.LogWarning("[SessionRunner] sessionManagerPrefab unassigned — SessionManager will NOT spawn. Drag the prefab into the inspector field on Bootstrap.");
            }

                try { OnRunnerConnected?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                return true;
            }
            finally
            {
                _starting = false;
            }
        }

        public async Task Shutdown()
        {
            if (Runner != null)
            {
                await Runner.Shutdown();
            }
        }

        // ─── Resume-from-headset-removal ────────────────────────────────
        // Quest pauses the app when you take the headset off (proximity
        // sensor). Photon Fusion's heartbeats stop, the connection times
        // out after ~10 s, and you get kicked from the room. We trigger a
        // rejoin from THREE sources so we cover every drop pattern:
        //   • OnApplicationFocus(true) / OnApplicationPause(false)
        //     — the player put the headset back on; we may or may not
        //       still be technically "running".
        //   • OnDisconnectedFromServer
        //     — Photon told us we're out, regardless of focus state
        //       (network blip while headset is on, etc).
        //   • OnShutdown
        //     — covers explicit shutdowns we didn't initiate.
        //
        // Auto-rejoin runs StartShared with the cached session code on a
        // background coroutine with retry-with-backoff (every ~3 s for up
        // to 60 s), so even if Photon's server takes a few seconds to
        // notice the drop / clean up the old player slot, we land back in
        // the room as soon as it accepts us.
        //
        // Caveat: if the room itself gets garbage-collected by Photon
        // (default ~5 min empty-room TTL on shared rooms), the rejoin
        // creates a brand-new room with the same code. The egg / opponent
        // state is gone in that case.

        private float _lastRejoinAttemptAt;
        private const float MinRejoinSpacing = 2.0f;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) TryAutoRejoin("focus regained");
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (!isPaused) TryAutoRejoin("unpaused");
        }

        private void TryAutoRejoin(string reason)
        {
            if (string.IsNullOrEmpty(_lastSessionCode)) return;
            if (_rejoining) return;
            // Don't spam attempts — the focus + pause + disconnect events
            // all tend to fire within milliseconds of each other.
            if (Time.unscaledTime - _lastRejoinAttemptAt < MinRejoinSpacing) return;
            _lastRejoinAttemptAt = Time.unscaledTime;

            // No "already running" early-out — Runner.IsRunning can lag
            // the actual connection state by hundreds of ms on resume.
            // Just let StartShared handle the recycle: if we are still
            // genuinely connected it'll reuse the runner cleanly, and if
            // we aren't it'll tear down the dead one and dial back in.
            Debug.Log($"[SessionRunner] Auto-rejoin triggered ({reason}) — re-entering room '{_lastSessionCode}'.");
            _ = AutoRejoinAsync();
        }

        private async Task AutoRejoinAsync()
        {
            _rejoining = true;
            try
            {
                // Retry-with-fixed-spacing: Photon's server can take a
                // few seconds after a disconnect to clear our old player
                // slot, and rejoining before that returns false. Try
                // every ~3 s for 60 s before giving up.
                float deadline = Time.unscaledTime + 60f;
                int attempt = 0;
                while (Time.unscaledTime < deadline)
                {
                    attempt++;
                    bool ok = false;
                    try { ok = await StartShared(_lastSessionCode); }
                    catch (Exception e) { Debug.LogException(e); }
                    if (ok)
                    {
                        Debug.Log($"[SessionRunner] Auto-rejoin SUCCEEDED on attempt {attempt} for '{_lastSessionCode}'.");
                        return;
                    }
                    Debug.LogWarning($"[SessionRunner] Auto-rejoin attempt {attempt} failed — retrying in 3s.");
                    await Task.Delay(3000);
                }
                Debug.LogError($"[SessionRunner] Auto-rejoin GAVE UP after 60s for '{_lastSessionCode}'.");
            }
            finally { _rejoining = false; }
        }

        // ---------- INetworkRunnerCallbacks (only what we need) ----------

        public void OnConnectedToServer(NetworkRunner runner)
        {
            try { OnRunnerConnected?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            try { OnRunnerDisconnected?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            // Photon told us we're out (could be headset-removal timeout
            // OR a transient network blip). Try to climb right back in
            // using the cached session code — see TryAutoRejoin.
            TryAutoRejoin($"Photon disconnect ({reason})");
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

        // The rest are required by the interface but unused here.
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            try { OnRunnerDisconnected?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            // Treat any shutdown we didn't initiate ourselves (i.e.,
            // Shutdown() wasn't called externally) as a drop and try to
            // recover. The MinRejoinSpacing guard de-dupes against the
            // disconnect callback firing a moment earlier.
            TryAutoRejoin($"runner shutdown ({shutdownReason})");
        }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
#else
    /// <summary>
    /// Pre-Fusion stub so the project compiles without the Fusion 2 SDK.
    /// CreateRoom / JoinRoom return true immediately (offline dev mode).
    /// </summary>
    public class SessionRunner : MonoBehaviour
    {
        public Task<bool> CreateRoom(string code) => Task.FromResult(true);
        public Task<bool> JoinRoom(string code) => Task.FromResult(true);
        public event Action OnRunnerConnected;
        public event Action OnRunnerDisconnected;

        private void _SuppressUnused()
        {
            OnRunnerConnected?.Invoke();
            OnRunnerDisconnected?.Invoke();
        }
    }
#endif
}
