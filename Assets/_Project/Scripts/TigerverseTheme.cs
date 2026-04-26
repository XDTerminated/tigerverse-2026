using UnityEngine;

namespace Tigerverse
{
    /// <summary>
    /// Mirrors the Tigerverse companion website's b&w + rounded-sm + 2px-border aesthetic
    /// inside Unity. Used by the Editor restyle menu and by procedural UI builders so
    /// every panel/button/label across Title / Lobby / Battle reads as the same product.
    ///
    /// All values are runtime-side (no Editor refs) so MonoBehaviours can pull from this
    /// directly at runtime if they want to live-style something.
    /// </summary>
    public static class TigerverseTheme
    {
        // ---------- Palette ----------
        // Mirrors website/src/styles/global.css.
        public static readonly Color Black = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color White = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly Color Backdrop = new Color32(0x00, 0x00, 0x00, 0x80); // bg-black/50
        public static readonly Color MutedText = new Color32(0x00, 0x00, 0x00, 0x99); // ~60% opacity

        // Brush palette from the canvas in App.tsx.
        public static readonly Color BrushBlack = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color BrushRed   = new Color32(0xDC, 0x26, 0x26, 0xFF);
        public static readonly Color BrushBlue  = new Color32(0x25, 0x63, 0xEB, 0xFF);
        public static readonly Color BrushGreen = new Color32(0x16, 0xA3, 0x4A, 0xFF);

        // ---------- Sizes ----------
        public const float BorderPx = 2f;
        public const float CornerRadiusPx = 4f; // tailwind rounded-sm
        public const float ButtonHeightPx = 48f; // h-12

        // ---------- Asset paths ----------
        public const string FontTtfPath = "Assets/_Project/Fonts/sophiecomic.ttf";
        public const string FontAssetPath = "Assets/_Project/Fonts/sophiecomic SDF.asset";
        public const string PanelSpritePath = "Assets/_Project/UI/Generated/panel-rounded-sm.png";
        public const string FilledSpritePath = "Assets/_Project/UI/Generated/panel-rounded-sm-filled.png";
    }
}
