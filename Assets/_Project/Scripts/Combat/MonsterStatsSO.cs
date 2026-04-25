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
                so.moves = catalog != null && catalog.Dodge != null
                    ? new MoveSO[] { catalog.Dodge }
                    : new MoveSO[0];
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
