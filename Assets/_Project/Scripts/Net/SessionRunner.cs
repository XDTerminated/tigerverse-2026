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
        // out after ~10 s, and you get kicked from the room. When the
        // player puts the headset back on we get OnApplicationFocus(true)
        // and OnApplicationPause(false). If the runner is no longer
        // running at that point, automatically dial back into the same
        // session code — the player rejoins their match without losing
        // their egg / opponent / state.
        //
        // Requires the player to put the headset back on within Photon's
        // empty-room TTL (~5 min by default for shared rooms). Past that
        // the room is gone and we'd need a host-migration flow.

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
            // Only rejoin if we WERE in a session that has since dropped.
            if (Runner != null && Runner.IsRunning) return;

            Debug.Log($"[SessionRunner] Auto-rejoin triggered ({reason}) — re-entering room '{_lastSessionCode}'.");
            _ = AutoRejoinAsync();
        }

        private async Task AutoRejoinAsync()
        {
            _rejoining = true;
            try
            {
                bool ok = await StartShared(_lastSessionCode);
                Debug.Log($"[SessionRunner] Auto-rejoin {(ok ? "SUCCEEDED" : "FAILED")} for '{_lastSessionCode}'.");
            }
            catch (Exception e) { Debug.LogException(e); }
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
