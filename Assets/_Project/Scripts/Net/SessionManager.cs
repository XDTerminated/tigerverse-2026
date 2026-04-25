using System;
using UnityEngine;

#if FUSION2
using Fusion;
#endif

namespace Tigerverse.Net
{
#if FUSION2
    /// <summary>
    /// Networked source-of-truth for the current room (Photon Fusion 2, Shared mode).
    /// Spawned by SessionRunner once the runner is connected.
    /// </summary>
    public class SessionManager : NetworkBehaviour
    {
        public static SessionManager Instance { get; private set; }

        // Player display names (NetworkString of capacity 8 chars).
        [Networked] public NetworkString<_8> Player1Name { get; set; }
        [Networked] public NetworkString<_8> Player2Name { get; set; }

        // GLB / image / cry URLs (capacity 64).
        [Networked] public NetworkString<_64> GlbUrlP1 { get; set; }
        [Networked] public NetworkString<_64> GlbUrlP2 { get; set; }
        [Networked] public NetworkString<_64> ImageUrlP1 { get; set; }
        [Networked] public NetworkString<_64> ImageUrlP2 { get; set; }
        [Networked] public NetworkString<_64> CryUrlP1 { get; set; }
        [Networked] public NetworkString<_64> CryUrlP2 { get; set; }

        // HP per side (current).
        [Networked] public int HPa { get; set; }
        [Networked] public int HPb { get; set; }

        // Phase: 0=Lobby,1=Draw,2=Hatch,3=PreReveal,4=Battle,5=Result
        [Networked] public int Phase { get; set; }

        // Whose turn (0 or 1).
        [Networked] public int CurrentTurn { get; set; }

        // Last move ID resolved (for late-joiners / observation).
        [Networked] public int LastMoveId { get; set; }

        // Stats per side.
        [Networked] public int MaxHpA { get; set; }
        [Networked] public int MaxHpB { get; set; }
        [Networked] public float AtkMultA { get; set; }
        [Networked] public float AtkMultB { get; set; }
        [Networked] public float SpeedA { get; set; }
        [Networked] public float SpeedB { get; set; }
        [Networked] public int ElementA { get; set; }
        [Networked] public int ElementB { get; set; }

        // Move catalog indices: 4 slots per side (3 moves + Dodge appended downstream).
        [Networked, Capacity(4)] public NetworkArray<int> MovesA => default;
        [Networked, Capacity(4)] public NetworkArray<int> MovesB => default;

        // Local C# events (not networked) — observers (BattleManager, FX) subscribe.
        public event Action<byte, int> OnMoveSubmitted;
        public event Action<byte, int, int, byte> OnMoveResolved;

        public override void Spawned()
        {
            base.Spawned();
            Instance = this;

            // SessionManager survives scene loads.
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
            base.Despawned(runner, hasState);
        }

        /// <summary>
        /// Caster (any peer) requests a move. State Authority resolves it.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestSubmitMove(byte moveId, int casterIndex)
        {
            try
            {
                OnMoveSubmitted?.Invoke(moveId, casterIndex);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// State Authority broadcasts the resolved result to all peers (for VFX/anim).
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_BroadcastMoveResolved(byte moveId, int casterIndex, int newHpDefender, byte effectFlags)
        {
            try
            {
                OnMoveResolved?.Invoke(moveId, casterIndex, newHpDefender, effectFlags);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
#else
    /// <summary>
    /// Pre-Fusion stub so the rest of the project compiles without the Fusion 2 SDK.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance;
        public event Action<byte, int> OnMoveSubmitted;
        public event Action<byte, int, int, byte> OnMoveResolved;
        public void RPC_RequestSubmitMove(byte m, int c) { }
        public void RPC_BroadcastMoveResolved(byte m, int c, int newHp, byte flags) { }

        // Suppress "never used" warnings for stub events.
        private void _SuppressUnused()
        {
            OnMoveSubmitted?.Invoke(0, 0);
            OnMoveResolved?.Invoke(0, 0, 0, 0);
        }
    }
#endif
}
