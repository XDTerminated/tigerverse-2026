#if FUSION2
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace Tigerverse.Net
{
    /// <summary>
    /// In Shared mode, every peer is responsible for spawning their own networked
    /// objects (with their own InputAuthority). When the local player joins the
    /// runner, this component spawns a PlayerAvatar prefab for them.
    /// </summary>
    public class LobbyAvatarSpawner : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Tooltip("PlayerAvatar prefab (must have a NetworkObject + PlayerAvatar component).")]
        public GameObject playerAvatarPrefab;

        private NetworkRunner _runner;
        private bool _spawnedLocal;

        private void Update()
        {
            if (_runner == null)
            {
                _runner = GetComponent<NetworkRunner>() ?? FindFirstObjectByType<NetworkRunner>();
                if (_runner != null)
                {
                    _runner.AddCallbacks(this);
                }
            }
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[LobbyAvatarSpawner] OnPlayerJoined player={player.PlayerId} local={runner.LocalPlayer.PlayerId} prefab={(playerAvatarPrefab != null ? playerAvatarPrefab.name : "NULL")} alreadySpawned={_spawnedLocal}");

            // Each peer spawns its own avatar exactly once.
            if (player == runner.LocalPlayer && !_spawnedLocal)
            {
                if (playerAvatarPrefab == null)
                {
                    Debug.LogError("[LobbyAvatarSpawner] playerAvatarPrefab is NULL, drag PlayerAvatar.prefab into the field on the Bootstrap GO, or run 'Tigerverse → Lobby → Build Avatar Prefab + Wire Spawner + WASD + Joystick'.");
                    return;
                }
                _spawnedLocal = true;
                var no = playerAvatarPrefab.GetComponent<NetworkObject>();
                if (no == null)
                {
                    Debug.LogError("[LobbyAvatarSpawner] playerAvatarPrefab has no NetworkObject, cannot spawn.");
                    return;
                }

                // Pick a deterministic spawn point so the two players don't overlap at origin.
                // Use the named scene markers if present; fall back to ±1.2m on X.
                Vector3 spawnPos;
                int idx = player.PlayerId % 2;
                var marker = GameObject.Find(idx == 0 ? "SpawnP0" : "SpawnP1");
                if (marker != null)
                {
                    spawnPos = marker.transform.position + Vector3.up * 1.6f; // head height
                    // Move the local rig to the marker so the player feels positioned, too.
                    var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                    if (origin != null)
                    {
                        origin.transform.position = marker.transform.position;
                        // Face center (origin) so the two players look at each other.
                        Vector3 lookTarget = -marker.transform.position;
                        lookTarget.y = 0;
                        if (lookTarget.sqrMagnitude > 0.01f)
                            origin.transform.rotation = Quaternion.LookRotation(lookTarget.normalized, Vector3.up);
                    }
                }
                else
                {
                    spawnPos = new Vector3(idx == 0 ? -1.2f : 1.2f, 1.6f, 0);
                }

                var spawned = runner.Spawn(no, spawnPos, Quaternion.identity, player);
                if (spawned == null)
                {
                    Debug.LogError("[LobbyAvatarSpawner] Runner.Spawn returned NULL, prefab may not be in Fusion's prefab table. Run 'Tools → Fusion → Rebuild Prefab Table'.");
                    _spawnedLocal = false; // allow retry
                }
                else
                {
                    Debug.Log($"[LobbyAvatarSpawner] Spawned local avatar for player {player.PlayerId} at {spawnPos}, NetworkId={spawned.Id}");
                }
            }
        }

        // Empty INetworkRunnerCallbacks members (required by interface).
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { _spawnedLocal = false; }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { _spawnedLocal = false; }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
#else
namespace Tigerverse.Net
{
    public class LobbyAvatarSpawner : UnityEngine.MonoBehaviour
    {
        public UnityEngine.GameObject playerAvatarPrefab;
    }
}
#endif
