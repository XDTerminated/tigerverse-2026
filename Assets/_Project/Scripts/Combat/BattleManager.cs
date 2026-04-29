using System.Collections;
using TMPro;
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

        // Idempotency for OnBattleEnd. Both the StateAuthority RPC path and
        // the Render-side WinnerIndex watcher can fire the event; we only
        // want one invocation per match per client.
        private bool _battleEndFired;

        public override void Spawned()
        {
            if (catalog == null) catalog = MoveCatalog.Instance;
            if (HasStateAuthority) { WinnerIndex = -1; }
        }

        public void Initialize(MonsterStatsSO a, MonsterStatsSO b)
        {
            statsA = a;
            statsB = b;
            _battleEndFired = false;

            // Always set HP, regardless of network authority. The previous
            // auth-gated init left HPa/HPb at 0 on non-authority clients,
            // which made ResolveMove's local-fallback path subtract damage
            // from a zero starting value (so nothing visibly changed). On
            // networked sessions the authority's write replicates and the
            // local write here is harmlessly overwritten next tick. On
            // non-networked sessions (Object null), this local write is
            // the ONLY source of HP and persists fine.
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
            Debug.Log($"[Battle] Initialize: HPa={HPa} HPb={HPb} statsA={(a!=null?a.displayName:"NULL")} statsB={(b!=null?b.displayName:"NULL")} hasObject={(Object!=null)} hasAuth={(Object!=null && HasStateAuthority)}");
        }

        public void SubmitMove(MoveSO move, int casterIndex)
        {
            if (move == null) { Debug.LogWarning("[Battle] SubmitMove called with null move."); return; }
            // Auto-resolve catalog if it wasn't wired in inspector — the
            // BattleManager.Spawned() callback only fires when the NetworkObject
            // is properly spawned. If it isn't, catalog stays null and every
            // SubmitMove silently no-ops. Loading from the singleton here
            // guarantees move lookup works even pre-spawn.
            if (catalog == null) catalog = MoveCatalog.Instance;
            if (catalog == null) { Debug.LogError("[Battle] No MoveCatalog available — cannot submit move."); return; }

            int id = catalog.IndexOf(move);
            Debug.Log($"[Battle] SubmitMove '{move.displayName}' caster={casterIndex} catalogIdx={id} hasObject={(Object!=null)} hasAuth={(Object!=null && HasStateAuthority)}");
            if (id < 0 || id > byte.MaxValue)
            {
                Debug.LogWarning($"[BattleManager] Move '{move.displayName}' not in catalog; cannot submit.");
                return;
            }

            // Network path: NetworkObject spawned → RPC routes to state
            // authority → ResolveMove runs there → [Networked] HP replicates.
            // Local fallback: if the NetworkObject isn't spawned for any
            // reason (scene authoring miss, runner not started, etc), call
            // ResolveMove directly so damage at LEAST applies on the local
            // client. Multiplayer sync is degraded in this fallback path
            // (the other player won't see the HP drop) but the alternative
            // is silently doing nothing, which is worse.
            if (Object != null && Object.IsValid)
            {
                RPC_RequestMove((byte)id, casterIndex);
            }
            else
            {
                Debug.LogWarning("[Battle] NetworkObject invalid — applying move LOCALLY (no multiplayer sync).");
                ResolveMove((byte)id, casterIndex);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestMove(byte moveId, int casterIndex)
        {
            Debug.Log($"[Battle] RPC_RequestMove received moveId={moveId} caster={casterIndex} hasAuth={HasStateAuthority}");
            if (!HasStateAuthority) return;
            ResolveMove(moveId, casterIndex);
        }

        private void ResolveMove(byte moveId, int casterIndex)
        {
            // Free-fire mode: either player can submit at any time. Per-move
            // cooldowns live on the caster's VoiceCommandRouter. The only
            // hard gate here is "battle is over" (Phase=End).
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
                DispatchResolved(moveId, casterIndex, casterIsA ? HPb : HPa, (byte)skipFlags, 0);
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

            Debug.Log($"[Battle] Resolved {move.displayName} dmg={damage} HPa={HPa} HPb={HPb} phase={Phase}");

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
            DispatchResolved(moveId, casterIndex, newDefenderHP, (byte)effects, damage);
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

        // Dispatcher: prefer the RPC path (replicates to all clients) when
        // we have a valid spawned NetworkObject AND state authority. Fall
        // back to a direct local call otherwise — the local fallback in
        // SubmitMove (when Object is null) ends up here and still drives
        // the HP UI + animation pipeline for the caller, even if the other
        // client won't receive the resolution.
        private void DispatchResolved(byte moveId, int casterIndex, int newDefenderHp, byte effectFlags, int damageDealt)
        {
            if (Object != null && Object.IsValid && HasStateAuthority)
            {
                RPC_PlayResolved(moveId, casterIndex, newDefenderHp, effectFlags, damageDealt);
            }
            else
            {
                PlayResolvedLocal(moveId, casterIndex, newDefenderHp, effectFlags, damageDealt);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayResolved(byte moveId, int casterIndex, int newDefenderHp, byte effectFlags, int damageDealt)
        {
            PlayResolvedLocal(moveId, casterIndex, newDefenderHp, effectFlags, damageDealt);
        }

        // Plain (non-RPC) version of the resolved-move handler. The RPC
        // delegates to this so the local fallback path in SubmitMove (which
        // can't fire RPCs without a NetworkObject) can still drive the HP
        // bar / animation pipeline directly.
        private void PlayResolvedLocal(byte moveId, int casterIndex, int newDefenderHp, byte effectFlags, int damageDealt)
        {
            if (catalog == null || catalog.moves == null || moveId >= catalog.moves.Length) return;
            var move = catalog.moves[moveId];

            int maxA = (statsA != null) ? statsA.maxHP : Mathf.Max(HPa, 1);
            int maxB = (statsB != null) ? statsB.maxHP : Mathf.Max(HPb, 1);
            OnHPChanged.Invoke(HPa, maxA, HPb, maxB);
            OnMoveResolved.Invoke(move, casterIndex);

            EffectFlags flags = (EffectFlags)effectFlags;

            if ((flags & EffectFlags.FinalBlow) != 0 && !_battleEndFired)
            {
                _battleEndFired = true;
                int winner = (HPa <= 0) ? 1 : 0;
                Debug.Log($"[Battle] Final blow detected via RPC. Winner={winner}. Firing OnBattleEnd.");
                OnBattleEnd.Invoke(winner);
                if (winner == 0) { if (cryA != null) cryA.PlayWin();  if (cryB != null) cryB.PlayLose(); }
                else             { if (cryB != null) cryB.PlayWin();  if (cryA != null) cryA.PlayLose(); }
            }

            StartCoroutine(PlayMoveSequence(move, casterIndex, damageDealt));
        }

        private IEnumerator PlayMoveSequence(MoveSO move, int casterIndex, int damageDealt)
        {
            if (move == null) yield break;

            bool casterIsA = (casterIndex == 0);
            var casterCry = casterIsA ? cryA : cryB;
            var casterPivot = casterIsA ? monsterAPivot : monsterBPivot;
            var defenderPivot = casterIsA ? monsterBPivot : monsterAPivot;

            float castDuration = Mathf.Max(0.05f, move.castDurationSec);

            // 1. Cry before attack.
            if (casterCry != null) casterCry.PlayBeforeAttack();

            // Trigger the casting trainer's "Point" pose on every PaperHumanoid
            // currently in the scene. The local player doesn't render their
            // own PaperHumanoid (we see our controllers), so on each device
            // the only humanoid present is the opponent's body — meaning
            // calling on all of them effectively only fires for the visible
            // remote avatar. The animation only plays if their Casual /
            // MaleCasual.controller actually defines a "Point" trigger; the
            // PaperHumanoid.PlayPoint helper safely no-ops otherwise.
            var humanoids = FindObjectsByType<Tigerverse.Net.PaperHumanoid>(FindObjectsSortMode.None);
            for (int i = 0; i < humanoids.Length; i++)
                if (humanoids[i] != null) humanoids[i].PlayPoint();

            // 2. Cast SFX at caster. Falls back to a procedurally-synthesized
            // element-flavoured clip if the MoveSO doesn't have one wired —
            // most of the canned MoveSO assets have castSfx unassigned, which
            // used to leave combat completely silent.
            if (casterPivot != null)
            {
                AudioClip castClip = move.castSfx
                    ?? ProceduralMoveSfx.Get(move.element, ProceduralMoveSfx.Role.Cast);
                if (castClip != null) AudioSource.PlayClipAtPoint(castClip, casterPivot.position);
            }

            // 3. Caster lunge animation (parallel, don't yield).
            if (casterPivot != null && move.specialFlag != MoveSO.SpecialFlag.HealSelf)
            {
                StartCoroutine(LungeCoroutine(casterPivot, 0.3f, castDuration * 0.5f));
            }

            // Special-flag short-circuits: HealSelf / NegateNext / Taunt-style effects.
            if (move.specialFlag == MoveSO.SpecialFlag.HealSelf)
            {
                if (casterPivot != null) SpawnHealRing(casterPivot.position);
                yield return new WaitForSeconds(castDuration);
                // Damage popup is skipped because dmg <= 0.
                yield break;
            }

            if (move.specialFlag == MoveSO.SpecialFlag.NegateNext ||
                move.specialFlag == MoveSO.SpecialFlag.BuffNextAttack)
            {
                if (casterPivot != null) SpawnAuraDome(casterPivot.position, move.element);
                yield return new WaitForSeconds(castDuration);
                yield break;
            }

            // 4. Either play assigned vfxPrefab or spawn procedural orb.
            GameObject vfxInstance = null;
            ProceduralOrbState orb = null;
            float waitForArrival = castDuration;

            if (move.vfxPrefab != null && casterPivot != null)
            {
                vfxInstance = Instantiate(move.vfxPrefab, casterPivot.position, casterPivot.rotation);
                waitForArrival = castDuration;
            }
            else if (casterPivot != null && defenderPivot != null)
            {
                orb = SpawnProceduralOrb(casterPivot.position, defenderPivot, move.element, castDuration * 0.6f);
                waitForArrival = castDuration * 0.6f;
            }

            // 5. Wait for cast/arrival.
            yield return new WaitForSeconds(waitForArrival);

            // Clean up procedural orb on arrival.
            if (orb != null && orb.gameObject != null)
            {
                Destroy(orb.gameObject);
            }

            // 6. Impact: SFX at defender. Same fallback logic as the cast SFX.
            if (defenderPivot != null)
            {
                AudioClip hitClip = move.hitSfx
                    ?? ProceduralMoveSfx.Get(move.element, ProceduralMoveSfx.Role.Hit);
                if (hitClip != null) AudioSource.PlayClipAtPoint(hitClip, defenderPivot.position);
            }

            // Move prefab vfx to defender if it was an assigned one.
            if (vfxInstance != null && defenderPivot != null)
            {
                vfxInstance.transform.position = defenderPivot.position;
                Destroy(vfxInstance, 2f);
            }

            // 7. Procedural impact burst + defender hit-shake.
            if (defenderPivot != null)
            {
                SpawnImpactBurst(defenderPivot.position, move.element);
                StartCoroutine(HitShakeCoroutine(defenderPivot, 0.05f, 0.25f));

                // Damage popup floating text (skip on heal/status).
                if (damageDealt > 0)
                    SpawnDamagePopup(defenderPivot.position, damageDealt, move.element);
            }

            // 8. Tail wait so the shake/impact have time to be seen before next move.
            yield return new WaitForSeconds(0.25f);
        }

        // --- Procedural FX helpers ----------------------------------------

        private static Color ElementTint(ElementType e)
        {
            switch (e)
            {
                case ElementType.Fire:     return new Color(1.00f, 0.45f, 0.20f);
                case ElementType.Water:    return new Color(0.30f, 0.60f, 1.00f);
                case ElementType.Electric: return new Color(1.00f, 0.95f, 0.30f);
                case ElementType.Earth:    return new Color(0.55f, 0.40f, 0.25f);
                case ElementType.Grass:    return new Color(0.40f, 0.85f, 0.40f);
                case ElementType.Ice:      return new Color(0.70f, 0.95f, 1.00f);
                case ElementType.Dark:     return new Color(0.45f, 0.25f, 0.55f);
                case ElementType.Neutral:
                default:                   return new Color(1.00f, 0.95f, 0.85f);
            }
        }

        private static Material MakeUnlitMaterial(Color tint)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            return mat;
        }

        private class ProceduralOrbState
        {
            public GameObject gameObject;
        }

        private IEnumerator LungeCoroutine(Transform t, float distance, float duration)
        {
            if (t == null || duration <= 0f) yield break;
            Vector3 startLocal = t.localPosition;
            float elapsed = 0f;
            while (elapsed < duration && t != null)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                float curve = Mathf.Sin(p * Mathf.PI); // 0 → 1 → 0
                t.localPosition = startLocal + new Vector3(0f, 0f, distance * curve);
                yield return null;
            }
            if (t != null) t.localPosition = startLocal;
        }

        private IEnumerator HitShakeCoroutine(Transform t, float amplitude, float duration)
        {
            if (t == null || duration <= 0f) yield break;
            Vector3 startLocal = t.localPosition;
            float elapsed = 0f;
            // 3-4 wiggle cycles, diminishing.
            float frequency = 14f;
            while (elapsed < duration && t != null)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                float decay = 1f - p;
                float offset = Mathf.Sin(elapsed * frequency) * amplitude * decay;
                t.localPosition = startLocal + new Vector3(offset, 0f, 0f);
                yield return null;
            }
            if (t != null) t.localPosition = startLocal;
        }

        private ProceduralOrbState SpawnProceduralOrb(Vector3 startPos, Transform defender, ElementType element, float duration)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "ProcOrb";
            var col = orb.GetComponent<Collider>();
            if (col != null) Destroy(col);
            orb.transform.position = startPos;
            orb.transform.localScale = Vector3.one * 0.18f;

            var rend = orb.GetComponent<Renderer>();
            if (rend != null)
            {
                Color tint = ElementTint(element);
                rend.sharedMaterial = MakeUnlitMaterial(tint);
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            var state = new ProceduralOrbState { gameObject = orb };
            StartCoroutine(AnimateOrb(orb, startPos, defender, duration, state));
            return state;
        }

        private IEnumerator AnimateOrb(GameObject orb, Vector3 startPos, Transform defender, float duration, ProceduralOrbState state)
        {
            if (orb == null) yield break;
            float elapsed = 0f;
            while (elapsed < duration && orb != null && state != null && state.gameObject == orb)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 endPos = (defender != null) ? defender.position : startPos;
                Vector3 pos = Vector3.Lerp(startPos, endPos, t);
                pos.y += 0.2f * Mathf.Sin(t * Mathf.PI); // parabolic arc
                orb.transform.position = pos;
                yield return null;
            }
        }

        private void SpawnImpactBurst(Vector3 position, ElementType element)
        {
            var go = new GameObject("ImpactBurst");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            Color tint = ElementTint(element);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            psr.sharedMaterial = mat;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor    = new ParticleSystem.MinMaxGradient(tint, Color.Lerp(tint, Color.white, 0.4f));
            main.maxParticles  = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            ps.Play();
            Destroy(go, 1.5f);
        }

        private void SpawnHealRing(Vector3 position)
        {
            var go = new GameObject("HealRing");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            Color soft = new Color(0.55f, 1f, 0.55f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", soft);
            else mat.color = soft;
            psr.sharedMaterial = mat;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 1f, 0.55f), new Color(0.85f, 1f, 0.80f));
            main.maxParticles  = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(60f);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = 0.45f;
            shape.donutRadius = 0.05f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            // Spiral upward.
            var velOL = ps.velocityOverLifetime;
            velOL.enabled = true;
            velOL.space = ParticleSystemSimulationSpace.World;
            velOL.y = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
            velOL.orbitalY = new ParticleSystem.MinMaxCurve(1.5f);

            ps.Play();
            Destroy(go, 2f);
        }

        private void SpawnAuraDome(Vector3 position, ElementType element)
        {
            var go = new GameObject("AuraDome");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            Color tint = Color.Lerp(ElementTint(element), Color.white, 0.6f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            psr.sharedMaterial = mat;

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
            main.startColor    = new ParticleSystem.MinMaxGradient(tint, new Color(1f, 1f, 1f, 0.6f));
            main.maxParticles  = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.5f;

            ps.Play();
            Destroy(go, 1.5f);
        }

        private void SpawnDamagePopup(Vector3 position, int damage, ElementType element)
        {
            var go = new GameObject("DamagePopup");
            go.transform.position = position + Vector3.up * 0.6f;

            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(go.transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(2f, 1f);
            rt.localScale = Vector3.one * 0.01f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(canvasGO.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = damage.ToString();
            tmp.fontSize = 36;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = ElementTint(element);
            tmp.alignment = TextAlignmentOptions.Center;
            var trt = tmp.rectTransform;
            trt.sizeDelta = new Vector2(2f, 1f);
            trt.anchoredPosition = Vector2.zero;

            StartCoroutine(AnimateDamagePopup(go, tmp));
        }

        private IEnumerator AnimateDamagePopup(GameObject go, TMP_Text tmp)
        {
            if (go == null) yield break;
            Vector3 startPos = go.transform.position;
            float duration = 0.8f;
            float scaleInDuration = 0.15f;
            float elapsed = 0f;
            Camera cam = Camera.main;

            while (elapsed < duration && go != null)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                go.transform.position = startPos + Vector3.up * (0.6f * p);

                // Scale pop-in 0.5 → 1.0 in first 0.15s.
                float s;
                if (elapsed < scaleInDuration)
                    s = Mathf.Lerp(0.5f, 1f, elapsed / scaleInDuration);
                else
                    s = 1f;

                // Billboard to camera if available.
                if (cam != null)
                {
                    Quaternion camRot = cam.transform.rotation;
                    go.transform.rotation = Quaternion.LookRotation(
                        go.transform.position - cam.transform.position, camRot * Vector3.up);
                }

                go.transform.localScale = Vector3.one * s;

                if (tmp != null)
                {
                    var c = tmp.color;
                    c.a = 1f - p;
                    tmp.color = c;
                }
                yield return null;
            }
            if (go != null) Destroy(go);
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

            // Backup win-trigger: the StateAuthority drives WinnerIndex via
            // [Networked] state. If the RPC_PlayResolved fan-out missed a
            // client (or that client's OnBattleEnd listener wasn't wired in
            // time), watching the replicated WinnerIndex here makes sure
            // every client still gets HandleBattleEnd called → win banner →
            // scene reload, so both players loop back to the menu instead of
            // just the one whose RPC handler fired.
            if (!_battleEndFired && Phase == BattlePhase.End && WinnerIndex >= 0)
            {
                _battleEndFired = true;
                Debug.Log($"[Battle] Final blow detected via Networked WinnerIndex={WinnerIndex} (RPC backup path). Firing OnBattleEnd.");
                OnBattleEnd.Invoke(WinnerIndex);
                if (WinnerIndex == 0) { if (cryA != null) cryA.PlayWin();  if (cryB != null) cryB.PlayLose(); }
                else                  { if (cryB != null) cryB.PlayWin();  if (cryA != null) cryA.PlayLose(); }
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
