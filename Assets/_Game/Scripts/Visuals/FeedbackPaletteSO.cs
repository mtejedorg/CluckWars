using System;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>Which palette accent an ability wears (spec D3a). <see cref="Custom"/> = the ability's own
    /// serialized literal (retired assets keep theirs); every other key reads <see cref="FeedbackPaletteSO.Accent"/>.</summary>
    public enum AccentKey
    {
        Custom = 0,
        Steal,
        Stun,
        Shove,
        Slow,
        Speed,
        Jump,
        EggShell,
        Hide,
        Peck,
        Hunt,
    }

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
        public Color PeckAccent = new Color(0.96f, 0.8f, 0.3f, 1f);
        public Color HuntAccent = new Color(0.9f, 0.1f, 0.2f, 1f);

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
    /// <c>AbilityBaseSO.AccentColor</c>). Pushed once by <c>ProjectInstaller</c>, like
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
