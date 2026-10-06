using System.Collections.Generic;
using CluckWars.Abilities;

namespace CluckWars.UI
{
    /// <summary>
    /// Single source of truth mapping an ability's concrete ScriptableObject type
    /// to the USS class that paints its exported <c>Icon_*.png</c> sprite (Stage-0
    /// design export). Both the in-game touch HUD (<see cref="Input.TouchControlsController"/>)
    /// and the menu front-end (<see cref="MenuUiController"/> character-select +
    /// lobby) paint ability icons from these classes, so the mapping lives here
    /// once instead of being duplicated per screen.
    /// </summary>
    /// <remarks>
    /// Keyed by concrete type name — stable and authoring-independent, so it is
    /// robust vs. ShortLabel / DisplayName drift ("Dive Bomb" is still the
    /// <c>RollTrampleAbilitySO</c> type, renamed twice now with the same asset GUID).
    /// The matching <c>.cw-hex-icon--*</c> rules are defined in every stylesheet
    /// that shows ability icons (Assets/UI/Styles/TouchControls.uss and
    /// CluckWarsTheme.uss) — <c>AbilitySystemTests</c> asserts both that every type
    /// has an entry here AND that every entry resolves to a rule that actually
    /// exists in both stylesheets, because a key with no matching rule renders a
    /// blank hex and logs nothing.
    /// <para>
    /// The <c>Icon_*.png</c> filenames still carry the pre-rename names (Icon_Peck,
    /// Icon_FlyingPeck). The art is unchanged, so the sprites were deliberately NOT
    /// renamed — that would churn texture GUIDs for no visual gain.
    /// </para>
    /// </remarks>
    public static class AbilityIconStyle
    {
        private static readonly Dictionary<string, string> ByType = new()
        {
            { "RollTrampleAbilitySO", "cw-hex-icon--dive-bomb" },
            { "CluckShockAbilitySO",  "cw-hex-icon--cluck" },
            { "SnatchAbilitySO",      "cw-hex-icon--snatch" },
            { "PeckAbilitySO",        "cw-hex-icon--peck" },   // foraging; art not yet exported
            { "RollPushAbilitySO",    "cw-hex-icon--roll" },
            { "FeatherTrapAbilitySO", "cw-hex-icon--trap" },
            { "FeatherAuraAbilitySO", "cw-hex-icon--aura" },
            { "RootEggAbilitySO",     "cw-hex-icon--root" },
            { "EggShellAbilitySO",    "cw-hex-icon--shell" },
            { "TurtleModeAbilitySO",  "cw-hex-icon--turtle" },
            { "SpineCoatAbilitySO",   "cw-hex-icon--spine" },
            { "SpeedBurstAbilitySO",  "cw-hex-icon--burst" },
            { "InvisibilityAbilitySO","cw-hex-icon--invis" },
            { "DoppelgangerAbilitySO","cw-hex-icon--doppel" },
            { "SneakyStealAbilitySO", "cw-hex-icon--steal" },
            { "AmbushAbilitySO",      "cw-hex-icon--ambush" },
            { "WingSlamAbilitySO",    "cw-hex-icon--wing-slam" },
            { "ShadowstepAbilitySO",  "cw-hex-icon--shadowstep" },
            { "MarkKillAbilitySO",    "cw-hex-icon--mark-kill" },
            // Roster expansion of 2026-08-23 (docs/design/class-essence-and-signatures.md
            // section 4). No sprites exported yet - all seven are listed in
            // AbilitySystemTests.IconsNotYetAuthored and carry their emoji fallback.
            { "HeadbuttAbilitySO",       "cw-hex-icon--headbutt" },
            { "ScrapAbilitySO",          "cw-hex-icon--scrap" },
            { "RuffleAbilitySO",         "cw-hex-icon--ruffle" },
            { "DustKickAbilitySO",       "cw-hex-icon--dust-kick" },
            { "FeintAbilitySO",          "cw-hex-icon--feint" },
            { "GroundQuakeAbilitySO",    "cw-hex-icon--ground-quake" },
            { "BellyFlopAbilitySO",      "cw-hex-icon--belly-flop" },
            // Class specializations. None have exported sprites yet - all are listed in
            // AbilitySystemTests.IconsNotYetAuthored and carry their emoji fallback.
            { "SlipperyPassiveSO",    "cw-hex-icon--slippery" },
            { "FeatherfootPassiveSO", "cw-hex-icon--featherfoot" },
            { "QuickDropAbilitySO",   "cw-hex-icon--quick-drop" },
            { "ImmovableAbilitySO",      "cw-hex-icon--immovable" },
            { "SmokeRoostAbilitySO",     "cw-hex-icon--smoke-roost" },
            { "HoarderPassiveSO",     "cw-hex-icon--hoarder" },
            { "BulwarkPassiveSO",     "cw-hex-icon--bulwark" },
            { "RelentlessPassiveSO",  "cw-hex-icon--relentless" },
            { "BullyPassiveSO",       "cw-hex-icon--bully" },
            { "SpoilerPassiveSO",     "cw-hex-icon--spoiler" },
            { "ThiefPassiveSO",       "cw-hex-icon--thief" },
        };

        /// <summary>
        /// The icon classes that actually have an exported sprite AND a matching
        /// <c>.cw-hex-icon--*</c> rule in <c>CluckWarsTheme.uss</c>. Every other entry in
        /// <see cref="ByType"/> is a reserved name for art that does not exist yet, and an
        /// element given one of those classes paints NOTHING — that was the menu's
        /// "blank hex" bug (Mark/Kill, Peck, the 2026-08-23 roster, every perk).
        /// <c>AbilitySystemTests</c> keeps this set equal to the rules the stylesheet defines.
        /// </summary>
        private static readonly HashSet<string> SpriteAuthored = new()
        {
            "cw-hex-icon--dive-bomb", "cw-hex-icon--cluck", "cw-hex-icon--snatch",
            "cw-hex-icon--roll", "cw-hex-icon--trap", "cw-hex-icon--aura",
            "cw-hex-icon--root", "cw-hex-icon--shell", "cw-hex-icon--turtle",
            "cw-hex-icon--spine", "cw-hex-icon--burst", "cw-hex-icon--invis",
            "cw-hex-icon--doppel", "cw-hex-icon--steal",
        };

        /// <summary>The icon classes with real sprites (see <see cref="SpriteAuthored"/>).</summary>
        public static IReadOnlyCollection<string> AuthoredSpriteClasses => SpriteAuthored;

        /// <summary>
        /// Like <see cref="ClassFor"/>, but only when that class really paints a sprite.
        /// Menu code uses this so an ability without art falls through to
        /// <see cref="Monogram"/> instead of rendering an empty hex.
        /// </summary>
        public static string SpriteClassFor(AbilityBaseSO ability)
        {
            string cls = ClassFor(ability);
            return cls != null && SpriteAuthored.Contains(cls) ? cls : null;
        }

        /// <summary>
        /// Text that always identifies <paramref name="ability"/> when it has no sprite: its
        /// authored <see cref="AbilityBaseSO.ShortLabel"/> (the ≤4-character HUD abbreviation),
        /// else the initials of its display name, else its asset name's first two letters.
        /// Never empty for a non-null ability.
        /// </summary>
        public static string Monogram(AbilityBaseSO ability)
        {
            if (ability == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(ability.ShortLabel)) return ability.ShortLabel.Trim().ToUpperInvariant();

            string src = !string.IsNullOrWhiteSpace(ability.DisplayName) ? ability.DisplayName : ability.name;
            var sb = new System.Text.StringBuilder(3);
            foreach (var word in src.Split(new[] { ' ', '/', '-', '_' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (sb.Length == 3) break;
            }
            if (sb.Length == 1 && src.Length > 1) sb.Append(char.ToUpperInvariant(src[1]));
            return sb.Length > 0 ? sb.ToString() : "?";
        }

        /// <summary>
        /// USS icon class painting the exported sprite for <paramref name="ability"/>,
        /// or <c>null</c> when the ability is null or has no exported sprite (the
        /// caller then hides its icon element and lets a text label carry it).
        /// </summary>
        public static string ClassFor(AbilityBaseSO ability) =>
            ability != null && ByType.TryGetValue(ability.GetType().Name, out var cls) ? cls : null;
    }
}
