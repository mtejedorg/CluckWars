using System.Collections.Generic;
using CluckWars.Abilities;

namespace CluckWars.UI
{
    /// <summary>
    /// Single source of truth mapping an ability's concrete ScriptableObject type to the USS
    /// class that paints its icon sprite. Both the in-game touch HUD
    /// (<see cref="Input.TouchControlsController"/>) and the menu front-end
    /// (<see cref="MenuUiController"/>) paint ability icons from these classes.
    /// </summary>
    /// <remarks>
    /// Keyed by concrete type name — stable and authoring-independent, so it is robust vs.
    /// ShortLabel / DisplayName drift ("Dive Bomb" is still the <c>RollTrampleAbilitySO</c> type).
    /// <para>
    /// Every entry is authored (menu overhaul Phase 2, 2026-10-07): its sprite is
    /// <c>Assets/_Game/Art/UI/Icons/Abilities/Icon_&lt;TypeName&gt;.png</c> (see
    /// <see cref="SpriteFileFor"/>) and its <c>.cw-hex-icon--*</c> rule lives in BOTH
    /// <c>Assets/UI/Styles/CluckWarsTheme.uss</c> and <c>TouchControls.uss</c>.
    /// <c>AbilityIconArtTests</c> asserts the type -&gt; entry -&gt; file -&gt; rule chain end to
    /// end, so a new ability without art fails a test instead of drawing a blank hex.
    /// <see cref="Monogram"/> remains only as a runtime safety net for a type with no entry.
    /// </para>
    /// </remarks>
    public static class AbilityIconStyle
    {
        private static readonly Dictionary<string, string> ByType = new()
        {
            { "RollTrampleAbilitySO", "cw-hex-icon--dive-bomb" },
            { "CluckShockAbilitySO",  "cw-hex-icon--cluck" },
            { "SnatchAbilitySO",      "cw-hex-icon--snatch" },
            { "PeckAbilitySO",        "cw-hex-icon--peck" },
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
            // Roster expansion of 2026-08-23 (docs/design/class-essence-and-signatures.md section 4).
            { "HeadbuttAbilitySO",       "cw-hex-icon--headbutt" },
            { "ScrapAbilitySO",          "cw-hex-icon--scrap" },
            { "RuffleAbilitySO",         "cw-hex-icon--ruffle" },
            { "DustKickAbilitySO",       "cw-hex-icon--dust-kick" },
            { "FeintAbilitySO",          "cw-hex-icon--feint" },
            { "GroundQuakeAbilitySO",    "cw-hex-icon--ground-quake" },
            { "BellyFlopAbilitySO",      "cw-hex-icon--belly-flop" },
            // Class specializations (perks) and their signature moves.
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

        /// <summary>Folder holding every ability/perk icon sprite (one per <see cref="ByType"/> key).</summary>
        public const string IconFolder = "Assets/_Game/Art/UI/Icons/Abilities";

        /// <summary>Every mapped concrete type name and its USS class (tests walk this).</summary>
        public static IReadOnlyDictionary<string, string> Entries => ByType;

        /// <summary>Project path of the sprite for <paramref name="typeName"/>: <c>Icon_&lt;TypeName&gt;.png</c>.</summary>
        public static string SpriteFileFor(string typeName) => $"{IconFolder}/Icon_{typeName}.png";

        /// <summary>
        /// Safety net only (every mapped type has a sprite): text that identifies an ability whose
        /// type has no <see cref="ByType"/> entry. Its
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
        /// USS icon class painting the sprite for <paramref name="ability"/>, or <c>null</c> when
        /// the ability is null or its type has no entry (the caller logs once and falls back to
        /// <see cref="Monogram"/>).
        /// </summary>
        public static string ClassFor(AbilityBaseSO ability) =>
            ability != null && ByType.TryGetValue(ability.GetType().Name, out var cls) ? cls : null;
    }
}
