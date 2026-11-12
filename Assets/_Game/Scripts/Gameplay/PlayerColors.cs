using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The four player colours, keyed by spawn corner: the one definition. A corner is a gameplay fact (it is where
    /// a player spawns and banks), and the base tints itself with its owner's colour, so the values live here and
    /// presentation reads them through <c>CluckWars.UI.PlayerPalette</c>, which forwards every member. The reasoning
    /// behind the hues (colour-blind separation, the YOU mark) is documented there. A source-scan test
    /// (<c>PlayerPaletteTests</c>) fails if any other file spells one of these colours.
    /// </summary>
    public static class PlayerColors
    {
        /// <summary>Corner 0, "Splash Aqua" (#3fe8e8), L* 84: the lightest of the four.</summary>
        public static readonly Color P1 = new Color32(0x3f, 0xe8, 0xe8, 255);
        /// <summary>Corner 1, bright blue (#0994e8).</summary>
        public static readonly Color P2 = new Color32(0x09, 0x94, 0xe8, 255);
        /// <summary>Corner 2, hot pink (#f7367b), L* 56.</summary>
        public static readonly Color P3 = new Color32(0xf7, 0x36, 0x7b, 255);
        /// <summary>Corner 3, dark teal (#17785a), L* 45: the darkest of the four.</summary>
        public static readonly Color P4 = new Color32(0x17, 0x78, 0x5a, 255);

        /// <summary>A seat whose corner is not known yet (a guest before joining, a corner not stamped yet):
        /// a warm grey (#a3967f) that reads as "nobody's colour".</summary>
        public static readonly Color Neutral = new Color32(0xa3, 0x96, 0x7f, 255);

        private static readonly Color[] ByCorner = { P1, P2, P3, P4 };

        /// <summary>Number of player colours (= spawn corners).</summary>
        public static int Count => ByCorner.Length;

        /// <summary>The player colour of spawn <paramref name="corner"/> (0..3, wrapping like the spawn points);
        /// <see cref="Neutral"/> when the corner is unknown (negative).</summary>
        public static Color ForCorner(int corner) => corner >= 0 ? ByCorner[corner % ByCorner.Length] : Neutral;
    }
}
