using UnityEngine;

namespace Tigerverse.Combat
{
    [CreateAssetMenu(fileName = "Move", menuName = "Tigerverse/Move")]
    public class MoveSO : ScriptableObject
    {
        public enum SpecialFlag
        {
            None,
            FreezeChance,
            IgnoreDodgeOnce,
            HealSelf,
            BuffNextAttack,
            NegateNext
        }

        [Header("Identity")]
        public string displayName;
        public string[] triggerPhrases; // lowercase recommended
        public Sprite icon;

        [Header("Combat")]
        public ElementType element;
        public int baseDamage;
        public float castDurationSec = 0.6f;
        public string animTrigger = "Attack";

        [Tooltip("Seconds the caster must wait before this specific move can be used again. Tune higher for stronger moves so big hits aren't spammable.")]
        public float cooldownSeconds = 3f;

        [Header("FX/SFX")]
        public GameObject vfxPrefab;
        public AudioClip castSfx;
        public AudioClip hitSfx;

        [Header("Special")]
        public SpecialFlag specialFlag = SpecialFlag.None;
        public float specialValue; // varies by flag (chance 0-1, heal amount, etc.)
    }
}
