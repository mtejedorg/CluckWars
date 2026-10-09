using UnityEngine;

namespace CluckWars.Settings
{
    /// <summary>
    /// Persisted, player-facing display options plus the developer-tools gate.
    /// Deliberately tiny: this is the only settings surface the project has, so it is a
    /// flat static class rather than a service, an installer binding and an interface for
    /// a handful of booleans nobody injects.
    /// </summary>
    /// <remarks>
    /// <b>Every preference here is a row on the Settings sheet</b>: the gear button at the
    /// top-right of the main menu opens a modal (<c>#SettingsSheet</c> in
    /// <c>Assets/UI/MainMenu.uxml</c>, bound by <c>MenuUiController.BuildSettingsSheet</c>) with one
    /// whole-row toggle per preference, each seeded from the getter with
    /// <c>SetValueWithoutNotify</c> so opening the sheet never writes a default to disk. The
    /// sheet is the only settings surface; the Loadout screen no longer carries toggles. The
    /// user-facing copy lives in the wording dictionary (<c>settings.*</c> keys in
    /// <c>Resources/Text/UiText.csv</c>), not here:
    /// <list type="bullet">
    ///   <item><see cref="AbilityRangeGuidesEnabled"/>: Range Guides</item>
    ///   <item><see cref="ReducedMotionEnabled"/>: Reduced Motion (also mirrored onto the menu
    ///   root as <c>.cw-reduced-motion</c>, which switches off the menus' own transitions)</item>
    ///   <item><see cref="PerformanceModeEnabled"/>: Performance Mode</item>
    ///   <item><see cref="DeveloperModeEnabled"/>: Dev Mode (shows/hides the main menu's
    ///   Ability Lab row as soon as it changes)</item>
    /// </list>
    ///
    /// <b>The one non-setting here is <see cref="LastSetup"/></b>: the class / perk / loadout / mode
    /// committed at READY, stored as stable ids (class value, <c>ChickenSubclass</c> byte, ability asset
    /// names) so the main-menu PLAY AGAIN and the post-match BACK TO LOBBY can reopen THE COOP. It is raw
    /// and unvalidated; <see cref="LastSetupResolver"/> turns it back into assets or rejects it.
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
        /// <c>PlayerPrefs</c> key for <see cref="PerformanceModeEnabled"/>. Namespaced for the
        /// same reason as <see cref="AbilityRangeGuidesKey"/>, and public so a test can assert
        /// against the real key instead of a copy.
        /// </summary>
        public const string PerformanceModeKey = "CluckWars.PerformanceMode";

        private static bool _performanceMode;
        private static bool _performanceModeLoaded;

        /// <summary>
        /// Show static chicken renders in the menus and skip the live 3D stage?
        /// <b>Defaults to true on phones and tablets</b> (<c>Application.isMobilePlatform</c>)
        /// and false on desktop when the key has never been written: a mid-range Android device
        /// should not pay for a second 3D scene behind a menu, while a desktop can afford it.
        /// Once the player chooses, their choice wins on every platform.
        /// </summary>
        public static bool PerformanceModeEnabled
        {
            get
            {
                if (!_performanceModeLoaded)
                {
                    _performanceMode = PlayerPrefs.GetInt(PerformanceModeKey, DefaultPerformanceMode ? 1 : 0) != 0;
                    _performanceModeLoaded = true;
                }
                return _performanceMode;
            }
            set
            {
                if (_performanceModeLoaded && _performanceMode == value) return;

                _performanceMode = value;
                _performanceModeLoaded = true;
                PlayerPrefs.SetInt(PerformanceModeKey, value ? 1 : 0);
                // Written immediately rather than left to Unity's own flush on quit: a mobile
                // player who changes a setting and is then killed by the OS should not lose it.
                PlayerPrefs.Save();
            }
        }

        /// <summary>The value <see cref="PerformanceModeEnabled"/> takes until the player chooses.</summary>
        public static bool DefaultPerformanceMode => Application.isMobilePlatform;

        /// <summary><c>PlayerPrefs</c> key for <see cref="AutoPeckEnabled"/>. Namespaced like every key here.</summary>
        public const string AutoPeckKey = "CluckWars.AutoPeck";

        private static bool _autoPeck;
        private static bool _autoPeckLoaded;

        /// <summary>
        /// Peck by yourself when you stand still by a pile (Phase 6, A6)? <b>Defaults to false</b>: it adds
        /// behaviour a player did not ask for. Read every input tick by <c>FusionNetworkService.OnInput</c> and
        /// carried to the state authority as <c>InputButton.AutoPeck</c>. The settings-page toggle is chunk 6.
        /// </summary>
        public static bool AutoPeckEnabled
        {
            get
            {
                if (!_autoPeckLoaded)
                {
                    _autoPeck = PlayerPrefs.GetInt(AutoPeckKey, 0) != 0;
                    _autoPeckLoaded = true;
                }
                return _autoPeck;
            }
            set
            {
                if (_autoPeckLoaded && _autoPeck == value) return;

                _autoPeck = value;
                _autoPeckLoaded = true;
                PlayerPrefs.SetInt(AutoPeckKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary><c>PlayerPrefs</c> key for <see cref="QuickMovesEnabled"/>. Namespaced like every key here.</summary>
        public const string QuickMovesKey = "CluckWars.QuickMoves";

        private static bool _quickMoves;
        private static bool _quickMovesLoaded;

        /// <summary>
        /// "Quick Moves" (Phase 6, A8): a tap fires the move the moment it lands, with no hold, aim or preview.
        /// <b>Defaults to false.</b> Carried to the state authority as <c>InputButton.QuickMoves</c>; the settings-page
        /// toggle is chunk 6.
        /// </summary>
        public static bool QuickMovesEnabled
        {
            get
            {
                if (!_quickMovesLoaded)
                {
                    _quickMoves = PlayerPrefs.GetInt(QuickMovesKey, 0) != 0;
                    _quickMovesLoaded = true;
                }
                return _quickMoves;
            }
            set
            {
                if (_quickMovesLoaded && _quickMoves == value) return;

                _quickMoves = value;
                _quickMovesLoaded = true;
                PlayerPrefs.SetInt(QuickMovesKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary><c>PlayerPrefs</c> keys for the last committed setup (see <see cref="LastSetup"/>).
        /// Namespaced like every key here; public so a test asserts the real ones.</summary>
        public const string LastSetupClassKey     = "CluckWars.LastSetup.Class";
        public const string LastSetupSubclassKey  = "CluckWars.LastSetup.Subclass";
        public const string LastSetupAbilitiesKey = "CluckWars.LastSetup.Abilities";
        public const string LastSetupModeKey      = "CluckWars.LastSetup.Mode";

        private const char AbilitySeparator = '|';

        /// <summary>Inverse of the <c>string.Join</c> in the setter. "" is an empty loadout, not one
        /// empty name (<c>"".Split</c> would return <c>[""]</c>). Public so a test pins it.</summary>
        public static string[] DecodeAbilities(string stored) =>
            string.IsNullOrEmpty(stored) ? System.Array.Empty<string>() : stored.Split(AbilitySeparator);

        private static LastSetupRecord _lastSetup;
        private static bool _lastSetupLoaded;

        /// <summary>
        /// The class / perk / loadout / mode last committed from the menu (READY), or null if none
        /// was ever stored. <b>Raw ids, not validated</b>: the stored ability names may no longer
        /// exist. Run it through <see cref="LastSetupResolver.TryResolve"/> before trusting it.
        /// Written through on set, like every other preference; null clears it.
        /// </summary>
        public static LastSetupRecord LastSetup
        {
            get
            {
                if (!_lastSetupLoaded)
                {
                    _lastSetup = PlayerPrefs.HasKey(LastSetupClassKey)
                        ? new LastSetupRecord
                        {
                            Class     = PlayerPrefs.GetInt(LastSetupClassKey, 0),
                            Subclass  = PlayerPrefs.GetInt(LastSetupSubclassKey, 0),
                            Abilities = DecodeAbilities(PlayerPrefs.GetString(LastSetupAbilitiesKey, string.Empty)),
                            Mode      = PlayerPrefs.GetInt(LastSetupModeKey, 0),
                        }
                        : null;
                    _lastSetupLoaded = true;
                }
                return _lastSetup;
            }
            set
            {
                _lastSetup = value;
                _lastSetupLoaded = true;
                if (value == null)
                {
                    PlayerPrefs.DeleteKey(LastSetupClassKey);
                    PlayerPrefs.DeleteKey(LastSetupSubclassKey);
                    PlayerPrefs.DeleteKey(LastSetupAbilitiesKey);
                    PlayerPrefs.DeleteKey(LastSetupModeKey);
                }
                else
                {
                    PlayerPrefs.SetInt(LastSetupSubclassKey, value.Subclass);
                    PlayerPrefs.SetString(LastSetupAbilitiesKey, string.Join(AbilitySeparator.ToString(), value.Abilities ?? System.Array.Empty<string>()));
                    PlayerPrefs.SetInt(LastSetupModeKey, value.Mode);
                    // The class key doubles as the "a setup exists" marker, so it is written last.
                    PlayerPrefs.SetInt(LastSetupClassKey, value.Class);
                }
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
            _performanceModeLoaded    = false;
            _autoPeckLoaded           = false;
            _quickMovesLoaded         = false;
            _lastSetupLoaded          = false;
        }
    }
}
