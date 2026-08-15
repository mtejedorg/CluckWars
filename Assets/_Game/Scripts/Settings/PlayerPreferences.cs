using UnityEngine;

namespace CluckWars.Settings
{
    /// <summary>
    /// Persisted, player-facing display options. Deliberately tiny: this is the first
    /// settings surface the project has ever had, so it is a flat static class with one
    /// property rather than a service, an installer binding and an interface for a single
    /// boolean nobody injects.
    /// </summary>
    /// <remarks>
    /// <b>There is no settings screen yet.</b> This is the persistence + runtime-read half
    /// only; <c>ui-designer</c> places the control in a later pass. The agreed user-facing
    /// copy for that pass, recorded here so the eventual UI and this code cannot drift:
    /// <list type="bullet">
    ///   <item>Label: <b>"Ability Range Guides"</b></item>
    ///   <item>Helper: <i>"Show a faint outline of what each equipped ability can reach."</i></item>
    /// </list>
    ///
    /// <b>Cached, because the consumer reads it every frame.</b>
    /// <c>AbilitySlotOverlay.LateUpdate</c> polls the getter once per frame so a mid-match
    /// toggle takes effect live, and <c>PlayerPrefs</c> is a native call backed by a registry
    /// / plist read — fine once, wasteful 60 times a second. The setter writes through, so
    /// the cache can never disagree with what is on disk.
    ///
    /// This is the PLAYER's choice. It is a separate gate from
    /// <c>FeedbackTuning.AbilitySlotOverlayEnabled</c>, which is the developer kill switch
    /// checked once in <c>Awake</c>; both are honoured and neither implies the other.
    /// </remarks>
    public static class PlayerPreferences
    {
        /// <summary>
        /// <c>PlayerPrefs</c> key for <see cref="AbilityRangeGuidesEnabled"/>. Namespaced
        /// because <c>PlayerPrefs</c> is a single flat per-application store — an unprefixed
        /// "AbilityRangeGuides" would sit in the same namespace as anything a future plugin
        /// writes. Public so a test can assert against the real key instead of a copy.
        /// </summary>
        public const string AbilityRangeGuidesKey = "CluckWars.AbilityRangeGuides";

        private static bool _abilityRangeGuides;
        private static bool _abilityRangeGuidesLoaded;

        /// <summary>
        /// Draw the always-on ability reach overlay (<c>AbilitySlotOverlay</c>)?
        /// <b>Defaults to true when the key has never been written</b> — the feature exists
        /// because new players cannot tell what an ability reaches, so it has to be on before
        /// anyone has been anywhere near a settings screen.
        /// </summary>
        public static bool AbilityRangeGuidesEnabled
        {
            get
            {
                if (!_abilityRangeGuidesLoaded)
                {
                    _abilityRangeGuides = PlayerPrefs.GetInt(AbilityRangeGuidesKey, 1) != 0;
                    _abilityRangeGuidesLoaded = true;
                }
                return _abilityRangeGuides;
            }
            set
            {
                if (_abilityRangeGuidesLoaded && _abilityRangeGuides == value) return;

                _abilityRangeGuides = value;
                _abilityRangeGuidesLoaded = true;
                PlayerPrefs.SetInt(AbilityRangeGuidesKey, value ? 1 : 0);
                // Written immediately rather than left to Unity's own flush on quit: a mobile
                // player who changes a setting and is then killed by the OS should not lose it.
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Drops the cache so the next read goes back to <c>PlayerPrefs</c>. Needed because
        /// "Enter Play Mode Options" can suppress the domain reload that would otherwise reset
        /// the statics, which would carry one play session's value into the next — the same
        /// hazard <c>AbilityTelegraph.ResetStatics</c> guards against.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetCache() => _abilityRangeGuidesLoaded = false;
    }
}
