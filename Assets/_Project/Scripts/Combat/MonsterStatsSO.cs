using System.Collections.Generic;
using UnityEngine;
using Tigerverse.Net;

namespace Tigerverse.Combat
{
    public class MonsterStatsSO : ScriptableObject
    {
        public string displayName;
        public int maxHP;
        public float attackMult;
        public float speed;
        public ElementType element;
        public MoveSO[] moves; // length 4: 3 typed + Dodge
        public string flavorText;
        public Texture2D portrait; // optional (drawing image)

        public static MonsterStatsSO FromData(MonsterStatsData data, MoveCatalog catalog, string nameOverride = null)
        {
            var so = ScriptableObject.CreateInstance<MonsterStatsSO>();

            if (data == null)
            {
                Debug.LogError("[MonsterStatsSO] FromData received null data; returning empty stats.");
                so.displayName = nameOverride ?? "Unknown";
                so.maxHP = 1;
                so.attackMult = 1f;
                so.speed = 1f;
                so.element = ElementType.Neutral;
                so.flavorText = string.Empty;

                // Even with null data, fill the moveset to 4 so the wrist HUD
                // has full pills and the player can attack. Same fill-to-4
                // loop as the normal path below.
                var nullPathMoves = new List<MoveSO>();
                if (catalog != null && catalog.Dodge != null) nullPathMoves.Add(catalog.Dodge);
                string[] nullFillers = { "Fireball", "Watergun", "Thunderbolt", "Iceshard", "Leafblade", "Rocksmash", "Shadowbite" };
                for (int i = 0; i < nullFillers.Length && nullPathMoves.Count < 4; i++)
                {
                    var fb = catalog?.Find(nullFillers[i]);
                    if (fb != null && !nullPathMoves.Contains(fb)) nullPathMoves.Add(fb);
                }
                so.moves = nullPathMoves.ToArray();
                return so;
            }

            so.displayName = !string.IsNullOrEmpty(nameOverride) ? nameOverride : "Monster";
            so.maxHP = data.hp;
            so.attackMult = data.attackMult;
            so.speed = data.speed;
            so.element = ElementTypeExtensions.Parse(data.element);
            so.flavorText = data.flavorText;
            so.portrait = null;

            var resolved = new List<MoveSO>();
            if (catalog == null)
            {
                Debug.LogError("[MonsterStatsSO] FromData received null catalog; cannot resolve moves.");
            }
            else if (data.moves != null)
            {
                for (int i = 0; i < data.moves.Length; i++)
                {
                    var name = data.moves[i];
                    if (string.IsNullOrEmpty(name)) continue;
                    var move = catalog.Find(name);
                    if (move != null && !resolved.Contains(move))
                        resolved.Add(move);
                }
            }

            // Always append Dodge (if not already present).
            if (catalog != null)
            {
                var dodge = catalog.Dodge;
                if (dodge != null && !resolved.Contains(dodge))
                    resolved.Add(dodge);
            }

            // Defensive fallback: if the backend didn't return any usable
            // move names (or none of them matched the catalog), `resolved`
            // ends up containing only Dodge. That leaves the player unable
            // to attack at all — shouting "fireball" matches nothing because
            // Fireball isn't in availableMoves. Warn loudly so we can spot
            // backend regressions in the log.
            if (catalog != null && resolved.Count <= 1)
            {
                Debug.LogWarning($"[MonsterStatsSO] Only Dodge resolved for '{so.displayName}' (data.moves empty or unmatched). Filling to 4 with Fireball/Watergun/Thunderbolt fallbacks.");
            }

            // Always guarantee 4 total moves so the wrist HUD has full pills and the
            // player can attack even if the backend's moveset is short or unmatched.
            string[] fillers = { "Fireball", "Watergun", "Thunderbolt", "Iceshard", "Leafblade", "Rocksmash", "Shadowbite" };
            for (int i = 0; i < fillers.Length && resolved.Count < 4; i++)
            {
                var fb = catalog?.Find(fillers[i]);
                if (fb != null && !resolved.Contains(fb)) resolved.Add(fb);
            }

            if (resolved.Count < 1)
            {
                Debug.LogError($"[MonsterStatsSO] No valid moves resolved for '{so.displayName}'. Falling back to Dodge only.");
                if (catalog != null && catalog.Dodge != null)
                    resolved.Add(catalog.Dodge);
            }

            so.moves = resolved.ToArray();
            return so;
        }
    }
}
