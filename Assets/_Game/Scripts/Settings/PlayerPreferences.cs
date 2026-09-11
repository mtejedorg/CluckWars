using UnityEngine;

namespace CluckWars.Settings
{
    /// <summary>
    /// Persisted, player-facing display options plus the developer-tools gate.
    /// Deliberately tiny: this is the only settings surface the project has, so it is a
    /// flat static class rather than a service, an installer binding and an interface for
    /// three booleans nobody injects.
    /// </summary>
    /// <remarks>
    /// <b>There is no dedicated settings screen.</b> The controls live on <c>#OptionsRow</c>
    /// in the Character Select column, which is as much of one as the project has. The agreed
    /// user-facing copy, recorded here so the UI and this code cannot drift:
    /// <list type="bullet">
    ///   <item>Label: <b>"Ability Range Guides"</b><br/>
    ///   Helper: <i>"Show a faint outline of what each equipped ability can reach."</i></item>
    ///   <item>Label: <b>"Reduced Motion"</b><br/>
    ///   Helper: <i>"Turn off camera shake and other screen movement effects."</i></item>
    ///   <item>Label: <b>"Developer Mode"</b><br/>
    ///   Helper: <i>"Show the Ability Lab and other developer tools on the main menu."</i></item>
    /// </list>
    /// <b>Status: partially placed.</b> Ability Range Guides and Developer Mode are both
    /// wired to <c>#OptionsRow</c> on the Character Select screen; Reduced Motion still has
    /// no control anywhere, so it can only be changed by editing PlayerPrefs by hand.
    ///
    /// <b>Cached, because the consumers read these per frame or per hit.</b>
    /// <c>AbilitySlotOverlay.LateUpdate</c> polls its getter once per frame and
    /// <c>MatchCamera.ApplyShake</c> reads its own on every cast, hit and death, so a
    /// mid-match toggle takes effect live. <c>PlayerPrefs</c> is a native call backed by a
    /// registry / plist read — fine once, wasteful at that rate, and the Android target is
    /// already close to frame budget. Each setter writes through, so a cache can never
    /// disagree with what is on disk, and no consumer needs a cache of its own.
    ///
    /// These are the PLAYER's choices. <see cref="AbilityRangeGuidesEnabled"/> in particular
    /// is a separate gate from <c>FeedbackTuning.AbilitySlotOverlayEnabled</c>, which is the
    /// developer kill switch checked once in <c>Awake</c>; both are honoured and neither
    /// implies the other.
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
        /// <c>PlayerPrefs</c> key for <see cref="ReducedMotionEnabled"/>. Namespaced for the
        /// same reason as <see cref="AbilityRangeGuidesKey"/>, and public so a test can assert
        /// against the real key instead of a copy.
        /// </summary>
        public const string ReducedMotionKey = "CluckWars.ReducedMotion";

        private static bool _reducedMotion;
        private static bool _reducedMotionLoaded;

        /// <summary>
        /// Suppress camera shake and other vestibular-triggering screen movement?
        /// <b>Defaults to false when the key has never been written</b> — the opposite
        /// default to <see cref="AbilityRangeGuidesEnabled"/>, and deliberately so: range
        /// guides add information a new player lacks, whereas this one removes feedback the
        /// game is designed around. Motion sensitivity is the minority case, so it is opt-in;
        /// defaulting it on would silently strip the juice from every player who never asked.
        /// </summary>
        /// <remarks>
        /// Honoured by <c>MatchCamera.ApplyShake</c>, which is the single chokepoint every
        /// shake in the game routes through — gating there covers <c>ChickenCombat</c>'s death
        /// shake, <c>ChickenVFX</c>'s caster micro-shake and all of <c>HitFeedback</c>'s
        /// hit-confirm shakes without any of them knowing this preference exists.
        ///
        /// <b>Not yet honoured by the execute hit-stop.</b> The <c>Time.timeScale</c> dip is
        /// owned by <c>HitStopDriver</c>, which lives inside <c>HitFeedback.cs</c>; gating it
        /// belongs at <c>HitStopDriver.Request</c> and is an outstanding follow-up. Anyone
        /// adding new screen movement — travel arcs, landing impacts, punch-in zooms — is
        /// expected to check this the way <c>ApplyShake</c> does.
        /// </remarks>
        public static bool ReducedMotionEnabled
        {
            get
            {
                if (!_reducedMotionLoaded)
                {
                    _reducedMotion = PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0;
                    _reducedMotionLoaded = true;
                }
                return _reducedMotion;
            }
            set
            {
                if (_reducedMotionLoaded && _reducedMotion == value) return;

                _reducedMotion = value;
                _reducedMotionLoaded = true;
                PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0);
                // Written immediately rather than left to Unity's own flush on quit: a mobile
                // player who changes a setting and is then killed by the OS should not lose it.
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// <c>PlayerPrefs</c> key for <see cref="DeveloperModeEnabled"/>. Namespaced for the
        /// same reason as <see cref="AbilityRangeGuidesKey"/>, and public so a test can assert
        /// against the real key instead of a copy.
        /// </summary>
        public const string DeveloperModeKey = "CluckWars.DeveloperMode";

        private static bool _developerMode;
        private static bool _developerModeLoaded;

        /// <summary>
        /// Reveal developer-only tools — currently the Ability Lab entry on the main menu.
        /// <b>Defaults to false when the key has never been written</b>, and unlike the two
        /// preferences above this one is not an accessibility or comfort choice: it is the
        /// only thing standing between a player and a scene that spawns practice dummies and
        /// holds the match clock open forever.
        /// </summary>
        /// <remarks>
        /// <b>This gate exists because the lab now ships.</b> <c>AbilityLab.unity</c> is
        /// enabled in Build Settings so it can be launched on a phone, where there is no
        /// Editor menu to launch it from — which also means a player build contains it and
        /// something has to keep players out.
        ///
        /// <b><c>Debug.isDebugBuild</c> cannot do this job.</b> <c>CluckWarsBuildMenu</c>
        /// builds with <c>BuildOptions.None</c>, so the flag is false on every artefact we
        /// ship, including the ones Maestro installs on the Pixel 9 to test with. A gate on
        /// it would hide the lab from precisely the build it exists to be used in. Likewise
        /// <c>#if UNITY_EDITOR</c>, which is what this whole change is undoing.
        ///
        /// Not cached-per-frame like its neighbours — nothing reads it in a loop; it is read
        /// when the main menu is built and when the toggle is drawn. It is cached anyway
        /// because <see cref="ResetCache"/> resets ALL preferences and a member that opted out
        /// of the pattern would be the odd one out for no gain.
        /// </remarks>
        public static bool DeveloperModeEnabled
        {
            get
            {
                if (!_developerModeLoaded)
                {
                    _developerMode = PlayerPrefs.GetInt(DeveloperModeKey, 0) != 0;
                    _developerModeLoaded = true;
                }
                return _developerMode;
            }
            set
            {
                if (_developerModeLoaded && _developerMode == value) return;

                _developerMode = value;
                _developerModeLoaded = true;
                PlayerPrefs.SetInt(DeveloperModeKey, value ? 1 : 0);
                // Written immediately rather than left to Unity's own flush on quit: a mobile
                // player who changes a setting and is then killed by the OS should not lose it.
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Drops every cache so the next read goes back to <c>PlayerPrefs</c>. Needed because
        /// "Enter Play Mode Options" can suppress the domain reload that would otherwise reset
        /// the statics, which would carry one play session's value into the next — the same
        /// hazard <c>AbilityTelegraph.ResetStatics</c> guards against.
        /// </summary>
        /// <remarks>
        /// Resets ALL preferences, not one. A future preference that forgets to clear its own
        /// flag here would leak across play sessions in the Editor and, worse, would make a
        /// test that calls this to force a re-read silently assert against a stale cache.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetCache()
        {
            _abilityRangeGuidesLoaded = false;
            _reducedMotionLoaded      = false;
            _developerModeLoaded      = false;
        }
    }
}
