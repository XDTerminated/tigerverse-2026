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

        // ─── Synchronized fist-bump / READY handshake ────────────────────
        // Each ReadyHandshake.Fire() RPCs into RPC_PostReady and the
        // resulting [Networked] state replicates to both peers. Both
        // handshakes block until BOTH ReadyP1 && ReadyP2 are true so
        // the MR transition + battle start happens in lockstep on both
        // headsets. The first valid bump midpoint posted wins and is
        // used as the shared MR arena anchor on both clients (so both
        // players' monsters land in the same physical floor spot).
        [Networked] public bool    ReadyP1         { get; set; }
        [Networked] public bool    ReadyP2         { get; set; }
        [Networked] public Vector3 BumpAnchor      { get; set; }
        [Networked] public bool    BumpAnchorValid { get; set; }

        // Local C# events (not networked), observers (BattleManager, FX) subscribe.
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
        /// Each client posts its own ready+bump-midpoint here. The state
        /// authority writes the per-player ready flag and (if not already
        /// set) records the bump midpoint as the shared MR arena anchor.
        /// First valid bump wins so both clients use the same physical
        /// point, the second client's bump is harmless (BumpAnchorValid
        /// is already true).
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_PostReady(int casterIndex, bool hasBumpMidpoint, Vector3 bumpMidpointWorld)
        {
            if (casterIndex == 0) ReadyP1 = true;
            else                   ReadyP2 = true;

            if (hasBumpMidpoint && !BumpAnchorValid)
            {
                BumpAnchor      = bumpMidpointWorld;
                BumpAnchorValid = true;
            }
            Debug.Log($"[SessionManager] RPC_PostReady caster={casterIndex} bump={(hasBumpMidpoint ? bumpMidpointWorld.ToString("F2") : "none")} → ReadyP1={ReadyP1} ReadyP2={ReadyP2}");
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
