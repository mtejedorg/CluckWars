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
            { "MightyPassiveSO",      "cw-hex-icon--mighty" },
            { "BracerPassiveSO",      "cw-hex-icon--bracer" },
            { "SlipperyPassiveSO",    "cw-hex-icon--slippery" },
            { "SecondWindPassiveSO",  "cw-hex-icon--wind" },
            { "ImmovablePassiveSO",   "cw-hex-icon--immovable" },
            { "JuggernautPassiveSO",  "cw-hex-icon--juggernaut" },
            { "ComboPassiveSO",       "cw-hex-icon--combo" },
            { "OpportunistPassiveSO", "cw-hex-icon--opportunist" },
        };

        /// <summary>
        /// USS icon class painting the exported sprite for <paramref name="ability"/>,
        /// or <c>null</c> when the ability is null or has no exported sprite (the
        /// caller then hides its icon element and lets a text label carry it).
        /// </summary>
        public static string ClassFor(AbilityBaseSO ability) =>
            ability != null && ByType.TryGetValue(ability.GetType().Name, out var cls) ? cls : null;
    }
}
