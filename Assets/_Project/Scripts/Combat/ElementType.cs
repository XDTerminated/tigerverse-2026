using UnityEngine;

namespace Tigerverse.Combat
{
    public enum ElementType
    {
        Neutral = 0,
        Fire = 1,
        Water = 2,
        Electric = 3,
        Earth = 4,
        Grass = 5,
        Ice = 6,
        Dark = 7
    }

    public static class ElementTypeExtensions
    {
        public static ElementType Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return ElementType.Neutral;
            switch (s.Trim().ToLowerInvariant())
            {
                case "fire": return ElementType.Fire;
                case "water": return ElementType.Water;
                case "electric":
                case "electricity":
                case "lightning":
                    return ElementType.Electric;
                case "earth":
                case "ground":
                case "rock":
                    return ElementType.Earth;
                case "grass":
                case "plant":
                case "leaf":
                    return ElementType.Grass;
                case "ice":
                case "frost":
                    return ElementType.Ice;
                case "dark":
                case "shadow":
                    return ElementType.Dark;
                case "neutral":
                case "normal":
                case "":
                    return ElementType.Neutral;
                default:
                    return ElementType.Neutral;
            }
        }

        public static Color ToColor(this ElementType e)
        {
            switch (e)
            {
                case ElementType.Fire:     return new Color(0.93f, 0.20f, 0.16f); // red
                case ElementType.Water:    return new Color(0.18f, 0.45f, 0.93f); // blue
                case ElementType.Electric: return new Color(1.00f, 0.92f, 0.20f); // yellow
                case ElementType.Earth:    return new Color(0.55f, 0.36f, 0.20f); // brown
                case ElementType.Grass:    return new Color(0.27f, 0.78f, 0.30f); // green
                case ElementType.Ice:      return new Color(0.66f, 0.86f, 0.96f); // light blue
                case ElementType.Dark:     return new Color(0.40f, 0.20f, 0.55f); // purple
                case ElementType.Neutral:
                default:                   return new Color(0.55f, 0.55f, 0.55f); // gray
            }
        }
    }
}
