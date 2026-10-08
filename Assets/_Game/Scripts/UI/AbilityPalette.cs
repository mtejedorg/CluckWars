using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// The UI colour of an ability: the warm painted colour of its <see cref="AbilityCategory"/>
    /// (menu overhaul Phase 4, re-audit item 3). Every ability hex, deck-card frame, category tag
    /// and slot badge in the menus takes its colour from here, so a move's colour says its job.
    /// </summary>
    /// <remarks>
    /// <b>UI only.</b> <see cref="AbilityBaseSO.AccentColor"/> stays the in-match VFX colour
    /// (telegraphs, cast bursts, range rings); the menus and the in-match hexes read the category colour.
    /// <para>
    /// The four colours were solved, not picked (<c>AbilityPaletteTests</c> pins every rule):
    /// the label drawn on each (cream or ink, whichever <see cref="InkOn"/> picks) clears 4.5:1;
    /// each sits at CIEDE2000 &gt;= <see cref="MinPlayerDeltaE"/> from every player colour
    /// (<see cref="PlayerPalette"/>), so a move never reads as a player; they are &gt;= 25 apart from
    /// each other; and after a deuteranopia or protanopia simulation (Machado, Oliveira &amp; Fernandes
    /// 2009, severity 1) every pair keeps &gt;= <see cref="MinColourBlindDeltaE"/>. The stock Material
    /// colours they replace (#FF5722, #9C27B0, #4CAF50, #00BCD4) failed the first two.
    /// </para>
    /// <para>
    /// Colour is never the only cue: every hex also carries its category's shape
    /// (<see cref="CategoryMark"/>), and the deck cards name the category.
    /// </para>
    /// </remarks>
    public static class AbilityPalette
    {
        /// <summary>Brick red. Cream label 5.2:1; nearest player (orange) dE00 21.</summary>
        public static readonly Color Steal = UiGfx.Hex32("b2442a");
        /// <summary>Plum. Cream label 6.6:1; nearest player (pink) dE00 19.</summary>
        public static readonly Color Control = UiGfx.Hex32("7a3f8f");
        /// <summary>Dark moss. Cream label 9.6:1; nearest player dE00 33. Was #5f8a2c (ink label 4.65:1), which
        /// sat 4.7 dE00 from Steal under deuteranopia (round-2 finding 9); darkened to L* 27 so it parts from
        /// Steal (L* 44) by lightness, which colour blindness keeps.</summary>
        public static readonly Color Defense = UiGfx.Hex32("30460c");
        /// <summary>Slate teal. Cream label 5.6:1; nearest player (blue) dE00 20.</summary>
        public static readonly Color Utility = UiGfx.Hex32("2e6b72");

        /// <summary>Smallest CIEDE2000 distance any category colour keeps from a player colour.</summary>
        public const float MinPlayerDeltaE = 15f;

        /// <summary>WCAG AA for normal text.</summary>
        public const float MinLabelContrast = 4.5f;

        /// <summary>
        /// Smallest CIEDE2000 distance between any two category colours after a full-severity deuteranopia or
        /// protanopia simulation. 8 dE00 is well past "noticeably different side by side" (~2-5) for the hex
        /// sizes the game draws; the shape mark (<see cref="CategoryMark"/>) carries the category where colour
        /// alone cannot. Today's weakest pairs: Control / Utility 10.1 (deuteranopia), Steal / Defense 8.5
        /// (protanopia; it was 4.7 under deuteranopia before Defense was darkened).
        /// </summary>
        public const float MinColourBlindDeltaE = 8f;

        /// <summary>An empty hex / slot (no ability): warm wood, nobody's colour.</summary>
        public static readonly Color EmptyHex = new Color(0.45f, 0.38f, 0.28f, 0.7f);

        public static Color CategoryColor(AbilityCategory category) => category switch
        {
            AbilityCategory.Steal   => Steal,
            AbilityCategory.Control => Control,
            AbilityCategory.Defense => Defense,
            _                       => Utility,
        };

        /// <summary>The hex / disc colour of <paramref name="ability"/>: its category colour.</summary>
        public static Color HexColor(AbilityBaseSO ability) => CategoryColor(ability.Category);

        /// <summary>WCAG 2.1 relative luminance of an sRGB colour (alpha ignored).</summary>
        public static float Luminance(Color c)
        {
            static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        /// <summary>WCAG contrast ratio between two colours (1..21).</summary>
        public static float ContrastRatio(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            if (la < lb) (la, lb) = (lb, la);
            return (la + 0.05f) / (lb + 0.05f);
        }

        /// <summary>
        /// Text colour for a label on <paramref name="bg"/>: whichever of the cream
        /// (<see cref="UiGfx.TextPrimary"/>) and dark ink (<see cref="UiGfx.TextDark"/>) text
        /// colours contrasts more with it.
        /// </summary>
        public static Color InkOn(Color bg) =>
            ContrastRatio(UiGfx.TextPrimary, bg) >= ContrastRatio(UiGfx.TextDark, bg) ? UiGfx.TextPrimary : UiGfx.TextDark;
    }
}
