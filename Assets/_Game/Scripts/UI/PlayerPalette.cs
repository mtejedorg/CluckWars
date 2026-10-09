using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// The player colours: ONE palette, keyed by spawn corner (round-2 finding 1). Every surface that
    /// means "this player" takes its colour from <see cref="ForCorner"/>: the menu Coop seats, the in-match
    /// waiting room, the HUD leaderboard dots and bars, the in-world nameplate and feet ring, the base
    /// tint, and the post-match podium and rows. A source-scan test (<c>PlayerPaletteTests</c>) fails if any
    /// other file defines its own copy of these four colours.
    /// </summary>
    /// <remarks>
    /// Colour-blind safe (docs/ART.md "Player Identity Colors"): still orange / blue / pink / teal, but
    /// separated by <b>lightness</b> as well as hue, because a dichromat keeps lightness. Every one of the six
    /// pairs stays &gt;= <see cref="AbilityPalette.MinPlayerColourBlindDeltaE"/> CIEDE2000 apart under
    /// protanopia, deuteranopia and tritanopia (<c>AbilityPaletteTests</c>); round 3 found P3 / P4 at 6.9
    /// (deuteranopia) and P2 / P4 at 9.0 (tritanopia) with the previous hues-only set. The corner is the identity, so a player is the same colour in the lobby (which
    /// shows the corner each seat will spawn on, see <c>CornerAssignment</c>), the match and the results.
    /// <para>
    /// The local player also carries the <b>YOU mark</b> everywhere: their name drawn as a gold pill
    /// with ink text (<see cref="YouMarkFill"/> / <see cref="YouMarkInk"/>, 10:1). UI Toolkit draws it
    /// with <c>.cw-you-mark</c> (Assets/UI/Styles/YouMark.uss); the in-world nameplate draws the same
    /// pill as a sprite.
    /// </para>
    /// </remarks>
    public static class PlayerPalette
    {
        /// <summary>Corner 0, light orange (was #e8751a, L* 62; now L* 71).</summary>
        public static readonly Color P1 = UiGfx.Hex32("f59a3a");
        /// <summary>Corner 1, bright blue (was #1a7fc4).</summary>
        public static readonly Color P2 = UiGfx.Hex32("0994e8");
        /// <summary>Corner 2, hot pink (was #c4286f, L* 45; now L* 56).</summary>
        public static readonly Color P3 = UiGfx.Hex32("f7367b");
        /// <summary>Corner 3, dark teal (was #0d9e7a, L* 58; now L* 45): the darkest of the four.</summary>
        public static readonly Color P4 = UiGfx.Hex32("17785a");

        /// <summary>A seat whose corner is not known yet (a guest before joining, a corner not stamped yet):
        /// a warm grey that reads as "nobody's colour".</summary>
        public static readonly Color Neutral = UiGfx.Hex32("a3967f");

        /// <summary>YOU mark fill (the brand gold).</summary>
        public static readonly Color YouMarkFill = UiGfx.Gold;
        /// <summary>YOU mark text (the brand dark ink).</summary>
        public static readonly Color YouMarkInk = UiGfx.TextDark;
        /// <summary>USS class that draws a name label as the YOU mark (Assets/UI/Styles/YouMark.uss).</summary>
        public const string YouMarkClass = "cw-you-mark";

        private static readonly Color[] ByCorner = { P1, P2, P3, P4 };

        /// <summary>Number of player colours (= spawn corners).</summary>
        public static int Count => ByCorner.Length;

        /// <summary>The player colour of spawn <paramref name="corner"/> (0..3); <see cref="Neutral"/> when the
        /// corner is unknown (negative).</summary>
        public static Color ForCorner(int corner) => corner >= 0 ? ByCorner[corner % ByCorner.Length] : Neutral;
    }
}
