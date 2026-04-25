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
    [RequireComponent(typeof(NetworkRunner))]
    public class SessionRunner : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Tooltip("Prefab containing the SessionManager NetworkBehaviour. Drag the SessionManager.prefab here.")]
        public GameObject sessionManagerPrefab;

        [SerializeField] private BackendConfig config;

        public int playerCount = 2;

        public NetworkRunner Runner { get; private set; }

        public event Action OnRunnerConnected;
        public event Action OnRunnerDisconnected;

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
                    Runner = gameObject.AddComponent<NetworkRunner>();
                    Runner.AddCallbacks(this);
                }
                else if (Runner == null)
                {
                    Runner = GetComponent<NetworkRunner>() ?? gameObject.AddComponent<NetworkRunner>();
                    Runner.AddCallbacks(this);
                }

                Debug.Log($"[SessionRunner] StartShared session='{code}' mode=Shared. AppId set: {!string.IsNullOrEmpty(Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion)}");
                Runner.ProvideInput = true;

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
