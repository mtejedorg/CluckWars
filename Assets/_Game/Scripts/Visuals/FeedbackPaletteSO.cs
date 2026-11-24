using System;
using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The feedback colours in one config asset (spec 2026-11-06 §12 D3a): the control-state colours
    /// <c>FeedbackTuning</c> exposes, plus one accent per ability job. The shipped values live in
    /// <c>Assets/_Game/Data/FeedbackPalette.asset</c>, bound by <c>ProjectInstaller</c> into
    /// <see cref="FeedbackPalette.Current"/>. The field initialisers are the pre-palette values, so an
    /// unbound game (EditMode tests, editor tools) looks exactly as it did before the palette existed.
    /// </summary>
    [CreateAssetMenu(menuName = "Cluck Wars/Feedback Palette", fileName = "FeedbackPalette")]
    public sealed class FeedbackPaletteSO : ScriptableObject
    {
        [Header("State colours")]
        public Color StunColor = new Color(1.00f, 0.85f, 0.20f, 1f);
        public Color SlowColor = new Color(0.30f, 0.75f, 1.00f, 1f);
        [Tooltip("E6a: a darker slow blue for TEXT (the SLOW row word and pop) so it reads on pale ground; the ring and state colour stay SlowColor.")]
        public Color SlowTextColor = new Color(0.15f, 0.55f, 0.95f, 1f);
        public Color KnockColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("\"Nothing will happen to THIS target\" grey (alpha is part of the colour).")]
        public Color NeutralNoEffectColor = new Color(0.58f, 0.58f, 0.62f, 0.85f);
        [Tooltip("#2a1a0c, the menu's Ink: ground-guide under-stroke and HUD hex rim.")]
        public Color InkColor = new Color(0.165f, 0.102f, 0.047f, 1f);
        [Tooltip("#FEF5E0 cream: held-move fill, no-target stalk, Death Mark shelled marker.")]
        public Color CreamColor = new Color(0.996f, 0.961f, 0.878f, 1f);
        [Tooltip("#F5C842 yolk gold: the Egg Shell hatch puff.")]
        public Color YolkColor = new Color(0.961f, 0.784f, 0.259f, 1f);

        [Header("Ability accents (AccentKey)")]
        public Color StealAccent = new Color(1f, 0.3f, 0.1f, 1f);
        [Tooltip("Spec §6: equal to StunColor (an ability wears the colour of the state it inflicts).")]
        public Color StunAccent = new Color(0.9f, 0.4f, 0.1f, 1f);
        public Color ShoveAccent = new Color(1f, 0.9f, 0.1f, 1f);
        [Tooltip("Spec §6: equal to SlowColor (an ability wears the colour of the state it inflicts).")]
        public Color SlowAccent = new Color(0.6f, 0.3f, 0.9f, 1f);
        public Color SpeedAccent = new Color(0.3f, 0.85f, 0.95f, 1f);
        public Color JumpAccent = new Color(0.4f, 0.2f, 0.9f, 1f);
        public Color EggShellAccent = new Color(0.55f, 0.42f, 0.25f, 1f);
        public Color HideAccent = new Color(0.55f, 0.85f, 0.85f, 1f);
        /// <summary>Lighter Hide purple for TEXT (the HIDDEN row and the POOF! pop, H7 contrast); the accent stays for rings/tints.</summary>
        public Color HideTextColor = new Color(0.75f, 0.65f, 1.00f, 1f);
        public Color PeckAccent = new Color(0.96f, 0.8f, 0.3f, 1f);
        public Color HuntAccent = new Color(0.9f, 0.1f, 0.2f, 1f);
        [Tooltip("The instant Slow's accent; Slow Area keeps SlowAccent.")]
        public Color SlowBurstAccent = new Color(0.35f, 0.82f, 0.45f, 1f);

        [Header("Death Mark")]
        [Tooltip("The hunt marker, brackets and Death Mark hex when the pounce is legal (#FFEB1A).")]
        public Color MarkPounceColor = new Color(1f, 0.92f, 0.1f, 1f);
        [Tooltip("The live / blocked red: the standings skull once the kill is live, and the hunt's \"not legal yet\" marker and brackets.")]
        public Color MarkLiveColor = new Color(0.9f, 0.04f, 0.04f, 1f);
        [Tooltip("The MISSED word the hunter reads when a mark ends (K11: cream, equal to CreamColor).")]
        public Color MarkMissedColor = new Color(0.996f, 0.961f, 0.878f, 1f);
        [Tooltip("The hunter's screen edge (the hunt mood's dark colour).")]
        public Color MarkHunterEdgeColor = new Color(0.03f, 0.015f, 0.05f, 1f);
        [Tooltip("The marked player's dim: the darkness colour with the Assassin far away.")]
        public Color MarkVictimDimColor = new Color(0.03f, 0.015f, 0.05f, 1f);
        [Tooltip("The marked player's cold violet: the darkness turns this as the Assassin closes in. Keep it dark.")]
        public Color MarkVictimColdColor = new Color(0.13f, 0.05f, 0.28f, 1f);
        [Tooltip("The marked player's red tint at the screen edge.")]
        public Color MarkVictimRedColor = new Color(0.9f, 0.04f, 0.04f, 1f);

        [Header("Thief loot mark")]
        [Tooltip("The loot bag: the Thief's marker, brackets and Steal-hex dot while a mark is live, and the victim's badge (a warm gold, apart from the yolk and the stun yellow).")]
        public Color LootGoldColor = new Color(0.91f, 0.62f, 0.10f, 1f);
        [Tooltip("The Steal-hex dot while the next mark is still coming: a pale, empty bag (cream, dimmed).")]
        public Color LootEmptyColor = new Color(0.80f, 0.74f, 0.62f, 0.85f);

        /// <summary>The accent for <paramref name="key"/>. <see cref="AccentKey.Custom"/> has no palette colour
        /// (the ability's own literal applies), so asking for it is a caller bug and throws.</summary>
        public Color Accent(AccentKey key) => key switch
        {
            AccentKey.Steal => StealAccent,
            AccentKey.Stun => StunAccent,
            AccentKey.Shove => ShoveAccent,
            AccentKey.Slow => SlowAccent,
            AccentKey.Speed => SpeedAccent,
            AccentKey.Jump => JumpAccent,
            AccentKey.EggShell => EggShellAccent,
            AccentKey.Hide => HideAccent,
            AccentKey.Peck => PeckAccent,
            AccentKey.Hunt => HuntAccent,
            AccentKey.SlowBurst => SlowBurstAccent,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "AccentKey.Custom has no palette colour"),
        };

        private static FeedbackPaletteSO _builtInDefaults;

        /// <summary>A cached runtime instance carrying the pre-palette values (the field initialisers).</summary>
        public static FeedbackPaletteSO BuiltInDefaults()
        {
            if (_builtInDefaults == null)
            {
                _builtInDefaults = CreateInstance<FeedbackPaletteSO>();
                _builtInDefaults.name = "FeedbackPalette (built-in defaults)";
                _builtInDefaults.hideFlags = HideFlags.HideAndDontSave;
            }
            return _builtInDefaults;
        }
    }

    /// <summary>
    /// The live palette for code with no injection context (<c>FeedbackTuning</c>'s static colours,
    /// <c>AbilityAccent.AccentColor</c>). Pushed once by <c>ProjectInstaller</c>, like
    /// <c>AbilityTargeting.Mode</c>; unbound, it is <see cref="FeedbackPaletteSO.BuiltInDefaults"/>.
    /// </summary>
    public static class FeedbackPalette
    {
        private static FeedbackPaletteSO _bound;

        public static FeedbackPaletteSO Current
        {
            get => _bound != null ? _bound : FeedbackPaletteSO.BuiltInDefaults();
            set => _bound = value;
        }
    }
}
