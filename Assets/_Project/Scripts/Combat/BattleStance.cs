using UnityEngine;
using Unity.XR.CoreUtils;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Reusable Pokemon-battle stance helper. Snaps the local XR rig so the
    /// player's eyes are 1.2m behind their own monster, at eye height,
    /// looking toward the opponent's monster. Works regardless of XR
    /// tracking origin mode (Device / Floor) by using the XROrigin's
    /// MoveCameraToWorldLocation API. Both the production battle flow and
    /// the editor dev menu route through this so both paths produce the
    /// same camera framing.
    /// </summary>
    public static class BattleStance
    {
        public const float DefaultBehindOffsetMeters = 2.0f;
        public const float DefaultEyeHeightMeters    = 1.55f;

        public static void PositionBehindMonster(GameObject localMonster, GameObject opponentMonster,
            float behindOffsetMeters = DefaultBehindOffsetMeters,
            float eyeHeightMeters    = DefaultEyeHeightMeters)
        {
            if (localMonster == null)
            {
                Debug.LogWarning("[BattleStance] localMonster is null — skipping.");
                return;
            }

            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("[BattleStance] No XROrigin in scene — skipping.");
                return;
            }

            // Pick the world position the player should be FACING. Order:
            //   1. The opponent monster, if there is one.
            //   2. Otherwise, the OTHER MonsterSpawnPivot (so dev mode with
            //      a single monster on PivotA still faces toward PivotB).
            //   3. Finally, fall back to monster.forward.
            Vector3 facingTarget;
            string facingSource;
            if (opponentMonster != null && opponentMonster != localMonster)
            {
                facingTarget = opponentMonster.transform.position;
                facingSource = "opponentMonster";
            }
            else
            {
                Transform otherPivot = FindOpponentPivot(localMonster.transform.position);
                if (otherPivot != null)
                {
                    facingTarget = otherPivot.position;
                    facingSource = $"opponentPivot('{otherPivot.name}')";
                }
                else
                {
                    facingTarget = localMonster.transform.position + localMonster.transform.forward;
                    facingSource = "monster.forward fallback";
                }
            }

            Vector3 forward = facingTarget - localMonster.transform.position;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            Vector3 cameraTarget = localMonster.transform.position - forward * behindOffsetMeters;
            cameraTarget.y = eyeHeightMeters;

            origin.MatchOriginUpCameraForward(Vector3.up, forward);
            origin.MoveCameraToWorldLocation(cameraTarget);
            Debug.Log($"[BattleStance] Camera→{cameraTarget} forward={forward} (facing {facingSource}) localMonster='{localMonster.name}' opponent='{(opponentMonster != null ? opponentMonster.name : "<none>")}'.");
        }

        // Find the MonsterSpawnPivot* that *isn't* nearest to the local
        // monster. Lets dev mode (single-monster spawn) still face the
        // empty opposing pivot like a real two-player battle would.
        private static Transform FindOpponentPivot(Vector3 localPos)
        {
            var pivotA = GameObject.Find("MonsterSpawnPivotA");
            var pivotB = GameObject.Find("MonsterSpawnPivotB");
            if (pivotA == null && pivotB == null) return null;
            if (pivotA == null) return pivotB.transform;
            if (pivotB == null) return pivotA.transform;

            float dA = Vector3.SqrMagnitude(pivotA.transform.position - localPos);
            float dB = Vector3.SqrMagnitude(pivotB.transform.position - localPos);
            // Local monster is on whichever pivot is closer; opponent is the other.
            return dA < dB ? pivotB.transform : pivotA.transform;
        }
    }
}
