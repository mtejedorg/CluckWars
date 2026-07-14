using UnityEngine;
using UnityEngine.UI;

namespace CluckWars.UI
{
    /// <summary>
    /// Shared UI tokens for the remaining UGUI surfaces: the ART.md §6.1 colour
    /// palette, the two brand fonts, and the baseline text drop-shadow.
    /// </summary>
    /// <remarks>
    /// This used to also bake rounded-rect / hex / gloss / chicken sprites procedurally
    /// at runtime, so the glossy art direction could be applied from code without
    /// importing any art. That was exactly the drift that made three UI attempts miss
    /// the design — every surface was a hand-rolled approximation of the design's CSS.
    /// The UI rebuild replaced all of it with sprites exported from the design itself
    /// (<c>Assets/_Game/Art/UI/</c>, see <c>tools/export-design-assets.ps1</c>), and the
    /// generators were deleted once the last caller went away.
    ///
    /// Do not add sprite generation back here. New UI art comes from the design export;
    /// screen-space layout is UI Toolkit (UXML/USS), and on-character indicators are
    /// world-space sprites (<c>ChickenWorldBars</c> / <c>ChickenStateOverlays</c>).
    /// </remarks>
    public static class UiGfx
    {
        private static Font _heading;
        private static Font _emoji;

        // ---- Theme tokens (ART.md §6.1, cluckwars-tokens-v2) -------------------
        // The USS side of these lives in Assets/UI/Styles/CluckWarsTokens.uss,
        // generated from the same design source by tools/generate-uss-tokens.ps1.
        public static readonly Color PanelOuter    = Hex32("5a3a1a");
        public static readonly Color PanelInner    = Hex32("3a2210");
        public static readonly Color PanelBorder   = Hex32("8a6a3a");
        public static readonly Color CardTop       = Hex32("3a2816");
        public static readonly Color CardBottom    = Hex32("2a1c0e");
        public static readonly Color CardBorder    = Hex32("6a4a28");
        public static readonly Color Gold          = Hex32("f5c842");
        public static readonly Color GoldDark      = Hex32("b88a14");
        public static readonly Color GreenTop      = Hex32("5ac54f");
        public static readonly Color GreenBottom   = Hex32("228b22");
        public static readonly Color GreenBorder   = Hex32("1a6a1a");
        public static readonly Color TextPrimary   = Hex32("fef5e0");
        public static readonly Color TextSecondary = Hex32("c4a060");
        public static readonly Color ScreenBg      = Hex32("0e0804");

        /// <summary>Parse a 6-hex-digit color (no '#').</summary>
        public static Color Hex32(string hex)
        {
            byte r = (byte)System.Convert.ToInt32(hex.Substring(0, 2), 16);
            byte g = (byte)System.Convert.ToInt32(hex.Substring(2, 2), 16);
            byte b = (byte)System.Convert.ToInt32(hex.Substring(4, 2), 16);
            return new Color32(r, g, b, 255);
        }

        // ---- Fonts -------------------------------------------------------------

        /// <summary>Heading / label font — Lilita One (ART.md §6.2), shipped in Resources/Fonts.</summary>
        public static Font ChunkyFont()
        {
            if (_heading == null)
                _heading = LoadFont("Fonts/LilitaOne-Regular",
                    new[] { "Lilita One", "Bauhaus 93", "Arial Black", "Impact" });
            return _heading;
        }

        /// <summary>Monochrome emoji font (Noto Emoji) for the few remaining glyph labels — tintable.</summary>
        public static Font EmojiFont()
        {
            if (_emoji == null)
                _emoji = LoadFont("Fonts/NotoEmoji-Regular",
                    new[] { "Segoe UI Emoji", "Segoe UI Symbol", "Apple Color Emoji" });
            return _emoji;
        }

        private static Font LoadFont(string resourcePath, string[] osFallback)
        {
            var f = Resources.Load<Font>(resourcePath);
            if (f != null) return f;
            f = Font.CreateDynamicFontFromOSFont(osFallback, 32);
            return f ?? (Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf"));
        }

        // ---- Text effects ------------------------------------------------------

        /// <summary>Adds the spec's baseline text drop-shadow (0,−2, dark, 60%).</summary>
        public static void AddShadow(Graphic g, float alpha = 0.6f, Vector2? offset = null)
        {
            if (g == null) return;
            var sh = g.gameObject.GetComponent<Shadow>();
            if (sh == null) sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor    = new Color(0f, 0f, 0f, alpha);
            sh.effectDistance = offset ?? new Vector2(0f, -2f);
        }
    }
}
