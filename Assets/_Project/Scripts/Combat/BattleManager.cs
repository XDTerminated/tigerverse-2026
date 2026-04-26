using System.Collections;
using UnityEngine;
using UnityEngine.Events;

#if FUSION2
using Fusion;
#endif

namespace Tigerverse.Combat
{
    [System.Serializable] public class HPChangedEvent : UnityEvent<int, int, int, int> { }
    [System.Serializable] public class MoveResolvedEvent : UnityEvent<MoveSO, int> { }
    [System.Serializable] public class BattleEndEvent : UnityEvent<int> { }

#if FUSION2
    public class BattleManager : NetworkBehaviour
    {
        public enum BattlePhase
        {
            WaitingForP1Move,
            ResolvingP1,
            WaitingForP2Move,
            ResolvingP2,
            End
        }

        [System.Flags]
        private enum EffectFlags : byte
        {
            None       = 0,
            FinalBlow  = 1 << 0,
            Heal       = 1 << 1,
            Frozen     = 1 << 2,
            Negated    = 1 << 3,
            DodgePierce= 1 << 4,
            Buffed     = 1 << 5
        }

        [Networked] public BattlePhase Phase { get; set; }
        [Networked] public int HPa { get; set; }
        [Networked] public int HPb { get; set; }
        [Networked] public int CurrentTurn { get; set; }
        [Networked] public int WinnerIndex { get; set; }

        [Header("References")]
        public MoveCatalog catalog;
        public Transform monsterAPivot;
        public Transform monsterBPivot;
        public MonsterCry cryA;
        public MonsterCry cryB;

        [Header("Events")]
        public HPChangedEvent OnHPChanged = new HPChangedEvent();
        public BattleEndEvent OnBattleEnd = new BattleEndEvent();
        public MoveResolvedEvent OnMoveResolved = new MoveResolvedEvent();

        // Local cache (non-networked)
        private MonsterStatsSO statsA;
        private MonsterStatsSO statsB;

        // Per-side flags (state authority only)
        private bool nextNegatedA, nextNegatedB;
        private bool ignoreDodgeNextA, ignoreDodgeNextB;
        private bool freezeNextSkipA, freezeNextSkipB;
        private bool buffNextAttackA, buffNextAttackB;

        // Render-side: track last seen HPs to avoid redundant UI dispatch.
        private int _lastSeenHPa = -1;
        private int _lastSeenHPb = -1;

        public override void Spawned()
        {
            if (catalog == null) catalog = MoveCatalog.Instance;
            if (HasStateAuthority) { WinnerIndex = -1; }
        }

        public void Initialize(MonsterStatsSO a, MonsterStatsSO b)
        {
            statsA = a;
            statsB = b;

            if (Object != null && Object.HasStateAuthority)
            {
                HPa = (a != null) ? a.maxHP : 1;
                HPb = (b != null) ? b.maxHP : 1;

                float sa = (a != null) ? a.speed : 0f;
                float sb = (b != null) ? b.speed : 0f;
                CurrentTurn = (sa >= sb) ? 0 : 1;
                Phase = (CurrentTurn == 0) ? BattlePhase.WaitingForP1Move : BattlePhase.WaitingForP2Move;
                WinnerIndex = -1;
            }

            int maxA = (a != null) ? a.maxHP : 1;
            int maxB = (b != null) ? b.maxHP : 1;
            OnHPChanged.Invoke(HPa, maxA, HPb, maxB);
        }

        public void SubmitMove(MoveSO move, int casterIndex)
        {
            if (move == null || catalog == null) return;
            int id = catalog.IndexOf(move);
            if (id < 0 || id > byte.MaxValue)
            {
                Debug.LogWarning($"[BattleManager] Move '{move.displayName}' not in catalog; cannot submit.");
                return;
            }
            RPC_RequestMove((byte)id, casterIndex);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestMove(byte moveId, int casterIndex)
        {
            if (!HasStateAuthority) return;
            ResolveMove(moveId, casterIndex);
        }

        private void ResolveMove(byte moveId, int casterIndex)
        {
            // Free-fire mode: either player can submit at any time. Caster-
            // side cooldown lives on MonsterAimController to prevent voice
            // spam. The only hard gate here is "battle is over" (Phase=End).
            if (Phase == BattlePhase.End) return;

            if (catalog == null || catalog.moves == null || moveId >= catalog.moves.Length) return;
            var move = catalog.moves[moveId];
            if (move == null) return;

            // Identify caster vs defender stats.
            bool casterIsA = (casterIndex == 0);
            var attackerStats = casterIsA ? statsA : statsB;
            var defenderStats = casterIsA ? statsB : statsA;
            if (attackerStats == null || defenderStats == null)
            {
                Debug.LogError("[BattleManager] ResolveMove called before Initialize().");
                return;
            }

            // Frozen-skip: if caster is frozen this turn, consume freeze and pass turn.
            ref bool freezeCaster = ref (casterIsA ? ref freezeNextSkipA : ref freezeNextSkipB);
            if (freezeCaster)
            {
                freezeCaster = false;
                EffectFlags skipFlags = EffectFlags.Frozen;
                FlipTurn(casterIndex);
                RPC_PlayResolved(moveId, casterIndex, casterIsA ? HPb : HPa, (byte)skipFlags);
                return;
            }

            EffectFlags effects = EffectFlags.None;

            // Compute base damage.
            float multiplier = ElementMatchup.Calculate(move.element, defenderStats.element);
            bool buffActive = casterIsA ? buffNextAttackA : buffNextAttackB;
            float buffMult = buffActive ? 1.1f : 1.0f;
            if (buffActive)
            {
                if (casterIsA) buffNextAttackA = false; else buffNextAttackB = false;
                effects |= EffectFlags.Buffed;
            }

            float rawDamage = multiplier * move.baseDamage * attackerStats.attackMult * buffMult;

            // Apply defender's nextNegated unless caster has IgnoreDodgeOnce.
            ref bool ignoreDodgeCaster = ref (casterIsA ? ref ignoreDodgeNextA : ref ignoreDodgeNextB);
            ref bool defenderNegated = ref (casterIsA ? ref nextNegatedB : ref nextNegatedA);

            if (defenderNegated)
            {
                if (ignoreDodgeCaster)
                {
                    ignoreDodgeCaster = false;
                    effects |= EffectFlags.DodgePierce;
                    // Damage unaffected; consume the negation as well so it's spent.
                    defenderNegated = false;
                }
                else
                {
                    rawDamage *= 0.4f; // 60% negated
                    defenderNegated = false;
                    effects |= EffectFlags.Negated;
                }
            }

            int damage = Mathf.Max(0, Mathf.RoundToInt(rawDamage));

            // Apply special flags from the move.
            switch (move.specialFlag)
            {
                case MoveSO.SpecialFlag.HealSelf:
                {
                    int healAmount = Mathf.RoundToInt(move.specialValue);
                    if (casterIsA)
                    {
                        int max = attackerStats.maxHP;
                        HPa = Mathf.Clamp(HPa + healAmount, 0, max);
                    }
                    else
                    {
                        int max = attackerStats.maxHP;
                        HPb = Mathf.Clamp(HPb + healAmount, 0, max);
                    }
                    effects |= EffectFlags.Heal;
                    break;
                }
                case MoveSO.SpecialFlag.FreezeChance:
                {
                    float chance = Mathf.Clamp01(move.specialValue);
                    if (Random.value < chance)
                    {
                        if (casterIsA) freezeNextSkipB = true; else freezeNextSkipA = true;
                        effects |= EffectFlags.Frozen;
                    }
                    break;
                }
                case MoveSO.SpecialFlag.IgnoreDodgeOnce:
                {
                    // Buff own next attack to ignore dodge. (This move's hit already applied.)
                    if (casterIsA) ignoreDodgeNextA = true; else ignoreDodgeNextB = true;
                    break;
                }
                case MoveSO.SpecialFlag.BuffNextAttack:
                {
                    if (casterIsA) buffNextAttackA = true; else buffNextAttackB = true;
                    effects |= EffectFlags.Buffed;
                    break;
                }
                case MoveSO.SpecialFlag.NegateNext:
                {
                    if (casterIsA) nextNegatedA = true; else nextNegatedB = true;
                    break;
                }
            }

            // Apply damage to defender HP, clamp >= 0.
            if (damage > 0)
            {
                if (casterIsA) HPb = Mathf.Max(0, HPb - damage);
                else            HPa = Mathf.Max(0, HPa - damage);
            }

            // Victory check.
            bool finalBlow = false;
            if (HPa <= 0)
            {
                WinnerIndex = 1;
                Phase = BattlePhase.End;
                finalBlow = true;
            }
            else if (HPb <= 0)
            {
                WinnerIndex = 0;
                Phase = BattlePhase.End;
                finalBlow = true;
            }
            else
            {
                FlipTurn(casterIndex);
            }

            if (finalBlow) effects |= EffectFlags.FinalBlow;

            int newDefenderHP = casterIsA ? HPb : HPa;
            RPC_PlayResolved(moveId, casterIndex, newDefenderHP, (byte)effects);
        }

        private void FlipTurn(int casterIndex)
        {
            if (casterIndex == 0)
            {
                CurrentTurn = 1;
                Phase = BattlePhase.WaitingForP2Move;
            }
            else
            {
                CurrentTurn = 0;
                Phase = BattlePhase.WaitingForP1Move;
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayResolved(byte moveId, int casterIndex, int newDefenderHp, byte effectFlags)
        {
            if (catalog == null || catalog.moves == null || moveId >= catalog.moves.Length) return;
            var move = catalog.moves[moveId];

            int maxA = (statsA != null) ? statsA.maxHP : Mathf.Max(HPa, 1);
            int maxB = (statsB != null) ? statsB.maxHP : Mathf.Max(HPb, 1);
            OnHPChanged.Invoke(HPa, maxA, HPb, maxB);
            OnMoveResolved.Invoke(move, casterIndex);

            EffectFlags flags = (EffectFlags)effectFlags;

            if ((flags & EffectFlags.FinalBlow) != 0)
            {
                int winner = (HPa <= 0) ? 1 : 0;
                OnBattleEnd.Invoke(winner);
                if (winner == 0) { if (cryA != null) cryA.PlayWin();  if (cryB != null) cryB.PlayLose(); }
                else             { if (cryB != null) cryB.PlayWin();  if (cryA != null) cryA.PlayLose(); }
            }

            StartCoroutine(PlayMoveSequence(move, casterIndex));
        }

        private IEnumerator PlayMoveSequence(MoveSO move, int casterIndex)
        {
            if (move == null) yield break;

            bool casterIsA = (casterIndex == 0);
            var casterCry = casterIsA ? cryA : cryB;
            var casterPivot = casterIsA ? monsterAPivot : monsterBPivot;
            var defenderPivot = casterIsA ? monsterBPivot : monsterAPivot;

            // Cry before attack.
            if (casterCry != null) casterCry.PlayBeforeAttack();

            // Cast SFX at caster.
            if (move.castSfx != null && casterPivot != null)
                AudioSource.PlayClipAtPoint(move.castSfx, casterPivot.position);

            // Spawn VFX at attacker.
            GameObject vfxInstance = null;
            if (move.vfxPrefab != null && casterPivot != null)
            {
                vfxInstance = Instantiate(move.vfxPrefab, casterPivot.position, casterPivot.rotation);
            }

            yield return new WaitForSeconds(Mathf.Max(0f, move.castDurationSec));

            // Impact: SFX at defender, optional VFX move.
            if (move.hitSfx != null && defenderPivot != null)
                AudioSource.PlayClipAtPoint(move.hitSfx, defenderPivot.position);

            if (vfxInstance != null && defenderPivot != null)
            {
                vfxInstance.transform.position = defenderPivot.position;
                Destroy(vfxInstance, 2f);
            }
        }

        public override void Render()
        {
            // Keep UI in sync if networked HPs change from elsewhere.
            if (HPa != _lastSeenHPa || HPb != _lastSeenHPb)
            {
                int maxA = (statsA != null) ? statsA.maxHP : Mathf.Max(HPa, 1);
                int maxB = (statsB != null) ? statsB.maxHP : Mathf.Max(HPb, 1);
                OnHPChanged.Invoke(HPa, maxA, HPb, maxB);
                _lastSeenHPa = HPa;
                _lastSeenHPb = HPb;
            }
        }
    }
#else
    // Stub fallback: lets the project compile before Photon Fusion 2 is installed.
    public class BattleManager : MonoBehaviour
    {
        public enum BattlePhase
        {
            WaitingForP1Move,
            ResolvingP1,
            WaitingForP2Move,
            ResolvingP2,
            End
        }

        public BattlePhase Phase;
        public int HPa;
        public int HPb;
        public int CurrentTurn;
        public int WinnerIndex = -1;

        [Header("References")]
        public MoveCatalog catalog;
        public Transform monsterAPivot;
        public Transform monsterBPivot;
        public MonsterCry cryA;
        public MonsterCry cryB;

        [Header("Events")]
        public HPChangedEvent OnHPChanged = new HPChangedEvent();
        public BattleEndEvent OnBattleEnd = new BattleEndEvent();
        public MoveResolvedEvent OnMoveResolved = new MoveResolvedEvent();

        public void Initialize(MonsterStatsSO a, MonsterStatsSO b)
        {
            HPa = (a != null) ? a.maxHP : 1;
            HPb = (b != null) ? b.maxHP : 1;
            float sa = (a != null) ? a.speed : 0f;
            float sb = (b != null) ? b.speed : 0f;
            CurrentTurn = (sa >= sb) ? 0 : 1;
            Phase = (CurrentTurn == 0) ? BattlePhase.WaitingForP1Move : BattlePhase.WaitingForP2Move;
            WinnerIndex = -1;

            int maxA = (a != null) ? a.maxHP : 1;
            int maxB = (b != null) ? b.maxHP : 1;
            OnHPChanged.Invoke(HPa, maxA, HPb, maxB);
        }

        public void SubmitMove(MoveSO move, int casterIndex)
        {
            // Stub: log, no networked resolution.
            if (move != null)
                Debug.Log($"[BattleManager STUB] SubmitMove('{move.displayName}', caster={casterIndex}). Install Photon Fusion 2 for networked resolution.");
        }
    }
#endif
}
