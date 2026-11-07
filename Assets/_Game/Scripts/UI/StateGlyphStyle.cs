using System.Collections.Generic;

namespace CluckWars.UI
{
    /// <summary>
    /// Single source of truth for the status-state glyph sprites (feedback batch C): every
    /// <see cref="StateGlyph"/> maps to one sprite file and one USS class
    /// (<c>cw-status-glyph--&lt;kebab&gt;</c>). The HUD status strip and the world-space badges
    /// read these strings; this class only holds data and never loads assets.
    /// </summary>
    /// <remarks>
    /// <c>Marked</c> reuses the shipped <c>Icons/Glyph_Skull.png</c>; every other state lives in
    /// <see cref="GlyphFolder"/>. <c>StateGlyphArtTests</c> asserts file, import profile, unique
    /// classes and that the folder holds no file the map does not reference.
    /// </remarks>
    public static class StateGlyphStyle
    {
        public enum StateGlyph : byte
        {
            Stunned, Slowed, KnockedBack, ShieldWalk, ShieldEgg, Hidden,
            Speeding, Hunting, Safe, PeckChain, Marked,
        }

        /// <summary>Folder holding the state glyph sprites (everything except Marked).</summary>
        public const string GlyphFolder = "Assets/_Game/Art/UI/Icons/States";

        private const string MarkedSprite = "Assets/_Game/Art/UI/Icons/Glyph_Skull.png";

        private static readonly StateGlyph[] AllStates =
        {
            StateGlyph.Stunned, StateGlyph.Slowed, StateGlyph.KnockedBack, StateGlyph.ShieldWalk,
            StateGlyph.ShieldEgg, StateGlyph.Hidden, StateGlyph.Speeding, StateGlyph.Hunting,
            StateGlyph.Safe, StateGlyph.PeckChain, StateGlyph.Marked,
        };

        private static readonly Dictionary<StateGlyph, (string Path, string UssClass)> Map = new()
        {
            { StateGlyph.Stunned,     ($"{GlyphFolder}/Glyph_Stunned.png",     "cw-status-glyph--stunned") },
            { StateGlyph.Slowed,      ($"{GlyphFolder}/Glyph_Slowed.png",      "cw-status-glyph--slowed") },
            { StateGlyph.KnockedBack, ($"{GlyphFolder}/Glyph_KnockedBack.png", "cw-status-glyph--knocked-back") },
            { StateGlyph.ShieldWalk,  ($"{GlyphFolder}/Glyph_ShieldWalk.png",  "cw-status-glyph--shield-walk") },
            { StateGlyph.ShieldEgg,   ($"{GlyphFolder}/Glyph_ShieldEgg.png",   "cw-status-glyph--shield-egg") },
            { StateGlyph.Hidden,      ($"{GlyphFolder}/Glyph_Hidden.png",      "cw-status-glyph--hidden") },
            { StateGlyph.Speeding,    ($"{GlyphFolder}/Glyph_Speeding.png",    "cw-status-glyph--speeding") },
            { StateGlyph.Hunting,     ($"{GlyphFolder}/Glyph_Hunting.png",     "cw-status-glyph--hunting") },
            { StateGlyph.Safe,        ($"{GlyphFolder}/Glyph_Safe.png",        "cw-status-glyph--safe") },
            { StateGlyph.PeckChain,   ($"{GlyphFolder}/Glyph_PeckChain.png",   "cw-status-glyph--peck-chain") },
            { StateGlyph.Marked,      (MarkedSprite,                           "cw-status-glyph--marked") },
        };

        /// <summary>Every state, in a stable order (tests walk this).</summary>
        public static IReadOnlyList<StateGlyph> All => AllStates;

        /// <summary>Project path of the sprite for <paramref name="state"/>.</summary>
        public static string SpritePathFor(StateGlyph state) => Map[state].Path;

        /// <summary>USS class painting the sprite for <paramref name="state"/>.</summary>
        public static string UssClassFor(StateGlyph state) => Map[state].UssClass;
    }
}
