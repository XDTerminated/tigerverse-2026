#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Tigerverse.Combat;

namespace Tigerverse.EditorTools
{
    public static class SetupCatalog
    {
        private const string MovesFolder    = "Assets/_Project/ScriptableObjects/Moves";
        private const string ResourcesFolder = "Assets/_Project/Resources";
        private const string CatalogPath    = "Assets/_Project/Resources/MoveCatalog.asset";
        private const string BackendPath    = "Assets/_Project/Resources/BackendConfig.asset";

        [MenuItem("Tigerverse/Setup -> Generate Default Moves & Catalog")]
        public static void GenerateDefaults()
        {
            EnsureFolder(MovesFolder);
            EnsureFolder(ResourcesFolder);

            var thunderbolt = CreateMove(
                MovesFolder + "/Thunderbolt.asset", "Thunderbolt",
                ElementType.Electric, 25, 0.5f, "AttackElectric",
                new[] { "thunderbolt", "thunder", "lightning", "zap", "shock" },
                MoveSO.SpecialFlag.None, 0f);

            var fireball = CreateMove(
                MovesFolder + "/Fireball.asset", "Fireball",
                ElementType.Fire, 22, 0.7f, "AttackFire",
                new[] { "fireball", "fire", "flame", "blast", "incinerate" },
                MoveSO.SpecialFlag.None, 0f);

            var iceshard = CreateMove(
                MovesFolder + "/Iceshard.asset", "Iceshard",
                ElementType.Ice, 18, 0.5f, "AttackIce",
                new[] { "iceshard", "ice", "frost", "freeze", "cold" },
                MoveSO.SpecialFlag.FreezeChance, 0.20f);

            var rocksmash = CreateMove(
                MovesFolder + "/Rocksmash.asset", "Rocksmash",
                ElementType.Earth, 28, 1.0f, "AttackEarth",
                new[] { "rocksmash", "rock", "smash", "earthquake", "quake" },
                MoveSO.SpecialFlag.None, 0f);

            var watergun = CreateMove(
                MovesFolder + "/Watergun.asset", "Watergun",
                ElementType.Water, 20, 0.5f, "AttackWater",
                new[] { "watergun", "water", "aqua", "splash", "stream" },
                MoveSO.SpecialFlag.None, 0f);

            var leafblade = CreateMove(
                MovesFolder + "/Leafblade.asset", "Leafblade",
                ElementType.Grass, 22, 0.6f, "AttackGrass",
                new[] { "leafblade", "leaf", "blade", "vine", "grass" },
                MoveSO.SpecialFlag.HealSelf, 5f);

            var shadowbite = CreateMove(
                MovesFolder + "/Shadowbite.asset", "Shadowbite",
                ElementType.Dark, 24, 0.6f, "AttackDark",
                new[] { "shadowbite", "shadow", "bite", "dark", "void" },
                MoveSO.SpecialFlag.IgnoreDodgeOnce, 1f);

            var healingaura = CreateMove(
                MovesFolder + "/Healingaura.asset", "Healingaura",
                ElementType.Neutral, 0, 0.7f, "Heal",
                new[] { "heal", "healing", "aura", "restore", "mend" },
                MoveSO.SpecialFlag.HealSelf, 20f);

            var dodge = CreateMove(
                MovesFolder + "/Dodge.asset", "Dodge",
                ElementType.Neutral, 0, 0.3f, "Dodge",
                new[] { "dodge", "evade", "duck", "avoid" },
                MoveSO.SpecialFlag.NegateNext, 0.6f);

            var taunt = CreateMove(
                MovesFolder + "/Taunt.asset", "Taunt",
                ElementType.Neutral, 0, 0.3f, "Taunt",
                new[] { "taunt", "mock", "jeer", "insult" },
                MoveSO.SpecialFlag.BuffNextAttack, 0.10f);

            // Catalog
            if (AssetDatabase.LoadAssetAtPath<MoveCatalog>(CatalogPath) != null)
                AssetDatabase.DeleteAsset(CatalogPath);

            var catalog = ScriptableObject.CreateInstance<MoveCatalog>();
            catalog.moves = new[]
            {
                thunderbolt, fireball, iceshard, rocksmash, watergun,
                leafblade, shadowbite, healingaura, dodge, taunt
            };
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            EditorUtility.SetDirty(catalog);

            // BackendConfig
            CreateBackendConfig();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Tigerverse] SetupCatalog complete: 10 moves + MoveCatalog + BackendConfig generated.");
        }

        private static MoveSO CreateMove(string path, string displayName, ElementType element, int dmg,
                                         float castDur, string anim, string[] phrases,
                                         MoveSO.SpecialFlag flag, float specialValue)
        {
            if (AssetDatabase.LoadAssetAtPath<MoveSO>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var move = ScriptableObject.CreateInstance<MoveSO>();
            move.displayName = displayName;
            move.element = element;
            move.baseDamage = dmg;
            move.castDurationSec = castDur;
            move.animTrigger = anim;
            move.triggerPhrases = phrases;
            move.specialFlag = flag;
            move.specialValue = specialValue;
            // icon, vfxPrefab, castSfx, hitSfx left null - to be authored later.

            AssetDatabase.CreateAsset(move, path);
            EditorUtility.SetDirty(move);
            return move;
        }

        private static void CreateBackendConfig()
        {
            // Look up the BackendConfig type via reflection because it may live in another agent's namespace
            // and may not exist yet. If we cannot find it, skip silently.
            var backendType = FindTypeInProject("BackendConfig");
            if (backendType == null)
            {
                Debug.LogWarning("[Tigerverse] BackendConfig type not found - skipping default asset creation. " +
                                 "Re-run this menu after the Net agent's BackendConfig.cs is added.");
                return;
            }

            if (AssetDatabase.LoadMainAssetAtPath(BackendPath) != null)
                AssetDatabase.DeleteAsset(BackendPath);

            var backend = ScriptableObject.CreateInstance(backendType);
            AssetDatabase.CreateAsset(backend, BackendPath);
            EditorUtility.SetDirty(backend);
        }

        private static System.Type FindTypeInProject(string typeName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == typeName && typeof(ScriptableObject).IsAssignableFrom(t))
                        return t;
                }
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }

            // ensure asset DB picks up the directory
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
#endif
