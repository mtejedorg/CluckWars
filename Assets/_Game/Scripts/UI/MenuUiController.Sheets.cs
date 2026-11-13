using CluckWars.Injection;
using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;


namespace CluckWars.UI
{
    /// <summary>
    /// <see cref="MenuUiController"/>: the two modal sheets the main menu can raise: the disconnect notice (CW-218) and SETTINGS (A21, architecture audit 2026-11-12).
    /// </summary>
    /// <remarks>
    /// One component, several files. The Bootstrap scene references this type by name and every serialized
    /// field and bound element reference has to stay on it, so the split is a partial class, never a second
    /// component. Navigation stays in the main file: it owns the page list, ShowPage, the back stack and the
    /// build order, and calls into each page's Build / Refresh here.
    /// </remarks>
    public sealed partial class MenuUiController
    {
        // ======================================================================
        //  DISCONNECT NOTICE (CW-218: lost connection -> "You were disconnected" + REJOIN)
        // ======================================================================
        /// <summary>Binds MainMenu.uxml #DisconnectSheet (static markup, hidden until <see cref="ConsumeDisconnectNotice"/>).</summary>
        private void BuildDisconnectSheet()
        {
            _disconnectSheet     = _mainMenu.Q<VisualElement>("DisconnectSheet");
            _disconnectBody      = _mainMenu.Q<Label>("DisconnectBody");
            _disconnectStatus    = _mainMenu.Q<Label>("DisconnectStatus");
            _disconnectRejoinBtn = _mainMenu.Q<Button>("DisconnectRejoinBtn");
            _disconnectBackBtn   = _mainMenu.Q<Button>("DisconnectBackBtn");
            if (_disconnectSheet == null || _disconnectBody == null || _disconnectStatus == null || _disconnectRejoinBtn == null || _disconnectBackBtn == null)
            {
                _log?.Error(Source, "MainMenu.uxml is missing #DisconnectSheet / #DisconnectBody / #DisconnectStatus / #DisconnectRejoinBtn / #DisconnectBackBtn; " +
                    "a player who lost the connection lands on the main menu with no message and no REJOIN.");
                _disconnectSheet = null;
                return;
            }
            _disconnectRejoinBtn.clicked += OnRejoin;
            OwnCue(_disconnectRejoinBtn);
            _disconnectBackBtn.clicked += CloseDisconnectSheet;
            BackCue(_disconnectBackBtn);
        }

        private bool IsDisconnectSheetOpen =>
            _disconnectSheet != null && _disconnectSheet.style.display == DisplayStyle.Flex;

        /// <summary>
        /// The match scene set <see cref="ISessionSelectionService.DisconnectNoticeOnMenuLoad"/> after a lost connection
        /// (<see cref="CluckWars.Networking.DisconnectRules.ShowsMenuNotice"/>). Read once, cleared at once: opens the sheet
        /// over the main menu, with REJOIN when there is a session to go back to.
        /// </summary>
        private void ConsumeDisconnectNotice()
        {
            if (_selection == null || !_selection.DisconnectNoticeOnMenuLoad) return;
            _selection.DisconnectNoticeOnMenuLoad = false;
            if (_disconnectSheet == null) return;   // BuildDisconnectSheet already reported the wiring gap

            _rejoinCode = _ugs?.LastJoinCode;
            bool offered = CluckWars.Networking.DisconnectRules.OffersRejoin(_selection.Mode, _rejoinCode);
            _log?.Info(Source, $"Disconnect notice: mode {_selection.Mode}, last code '{_rejoinCode ?? "-"}', REJOIN {(offered ? "offered" : "not offered")}.");

            // BuildAll has just shown the main menu (the post-match landing flags are never set alongside this one).
            SetDisconnectStatus(null);
            UiElements.SetShown(_disconnectBody, true);
            _disconnectRejoinBtn.SetEnabled(true);
            _disconnectBackBtn.SetEnabled(true);
            UiElements.SetShown(_disconnectRejoinBtn, offered);
            UiElements.SetShown(_disconnectSheet, true);
            if (offered) CheckRejoinLobby(_rejoinCode);
        }

        /// <summary>Asks the lobby service whether the lobby is still there; only a lobby known to be gone hides REJOIN.</summary>
        private async void CheckRejoinLobby(string code)
        {
            try
            {
                var availability = await _ugs.CheckLobbyAsync(code);
                if (!IsDisconnectSheetOpen || _isBusy || code != _rejoinCode) return;   // closed, or a REJOIN already started
                if (CluckWars.Networking.DisconnectRules.ShowsRejoin(true, availability)) return;
                // A lobby that is gone means the match ended (possibly the host-left case a transport reason of Unknown
                // was read as a lost connection, see DisconnectRules.Classify): no "Lost the signal" claim, only the outcome.
                _log?.Info(Source, $"Disconnect notice: lobby '{code}' is gone; REJOIN hidden.");
                UiElements.SetShown(_disconnectBody, false);
                UiElements.SetShown(_disconnectRejoinBtn, false);
                SetDisconnectStatus(UiText.Get(UiKeys.DisconnectRejoinFailed));
            }
            catch (Exception e)
            {
                // CheckLobbyAsync never throws by contract; anything here is a bug in this method. REJOIN stays up.
                _log?.Error(Source, $"Disconnect notice: the lobby check faulted: {e}");
            }
        }

        /// <summary>
        /// REJOIN: the same join-by-code flow as THE COOP's JOIN MATCH (UGS lobby, then the match scene joins the Fusion
        /// session of that name) - a fresh join under the late-join rules, nothing restored. A failure stays on the sheet.
        /// </summary>
        private async void OnRejoin()
        {
            if (_isBusy || _selection == null || string.IsNullOrWhiteSpace(_rejoinCode)) return;
            _audio.Tap();
            _isBusy = true;
            _disconnectRejoinBtn.SetEnabled(false);
            _disconnectBackBtn.SetEnabled(false);
            SetDisconnectStatus(UiText.Get(UiKeys.DisconnectRejoining));
            _log?.Info(Source, $"REJOIN: joining '{_rejoinCode}' again.");
            try
            {
                var info = await _ugs.JoinLobbyByCodeAsync(_rejoinCode);
                _selection.Mode = SessionMode.Join;
                _selection.SessionName = info.JoinCode;
                _log?.Info(Source, $"REJOIN: lobby '{info.JoinCode}' joined ({info.PlayerCount}/{info.MaxPlayers}); loading the match to join its session.");
                _isBusy = false;
                CloseDisconnectSheet();
                LoadMatchScene(); // re-claims _isBusy until the scene swaps
            }
            catch (Exception e)
            {
                // The match ending (lobby gone) or filling up is a legal outcome, not a wiring bug: Warn, and say so on the sheet.
                _autoTestRejoinFailures++;
                // The lobby service logged its Reason / ErrorCode just before (UGS line); the type tells the rest apart.
                _log?.Warn(Source, $"REJOIN '{_rejoinCode}' failed: {e.GetType().Name}: {e.Message}" +
                    (e.InnerException != null ? $" (inner {e.InnerException.GetType().Name}: {e.InnerException.Message})" : string.Empty));
                UiElements.SetShown(_disconnectBody, false);
                UiElements.SetShown(_disconnectRejoinBtn, false);
                _disconnectBackBtn.SetEnabled(true);
                SetDisconnectStatus(UiText.Get(UiKeys.DisconnectRejoinFailed));
                _isBusy = false;
            }
        }

        private void SetDisconnectStatus(string text)
        {
            if (_disconnectStatus == null) return;
            _disconnectStatus.text = text ?? string.Empty;
            UiElements.SetShown(_disconnectStatus, !(string.IsNullOrEmpty(text)));
        }

        private void CloseDisconnectSheet()
        {
            if (!IsDisconnectSheetOpen) return;
            UiElements.SetShown(_disconnectSheet, false);
        }

        // ======================================================================
        //  SETTINGS SHEET (main menu gear -> modal)
        // ======================================================================
        /// <summary>
        /// Wires the gear, the modal sheet and its preference rows and volume sliders. The sheet is static
        /// markup in MainMenu.uxml (#SettingsSheet); this only binds it.
        /// </summary>
        private void BuildSettingsSheet()
        {
            _settingsSheet = _mainMenu.Q<VisualElement>("SettingsSheet");
            if (_settingsSheet == null)
            {
                _log?.Error(Source, "MainMenu.uxml has no #SettingsSheet; the settings gear will do nothing.");
                return;
            }

            Bind<Button>(_mainMenu, "SettingsBtn", b => b.clicked += OpenSettings);
            Bind<Button>(_settingsSheet, "SettingsCloseBtn", b => { b.clicked += CloseSettings; BackCue(b); });
            // A tap on the dimmed scrim (outside the panel) closes too; taps inside the panel
            // bubble up here with a different target and are ignored.
            _settingsSheet.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target != _settingsSheet) return;
                _audio.Back();
                CloseSettings();
            });

            BindSettingRow("RangeGuidesRow", "RangeGuidesToggle",
                () => PlayerPreferences.AbilityRangeGuidesEnabled,
                v => PlayerPreferences.AbilityRangeGuidesEnabled = v, "Ability range guides");
            BindSettingRow("ReducedMotionRow", "ReducedMotionToggle",
                () => PlayerPreferences.ReducedMotionEnabled,
                v => { PlayerPreferences.ReducedMotionEnabled = v; ApplyReducedMotion(); }, "Reduced motion");
            BindSettingRow("PerformanceModeRow", "PerformanceModeToggle",
                () => PlayerPreferences.PerformanceModeEnabled,
                v => { PlayerPreferences.PerformanceModeEnabled = v; RefreshStage(); }, "Performance mode");
            // Phase 6 chunk 6: the input and feedback options, then the two volume sliders.
            BindSettingRow("QuickMovesRow", "QuickMovesToggle",
                () => PlayerPreferences.QuickMovesEnabled,
                v => PlayerPreferences.QuickMovesEnabled = v, "Quick moves");
            BindSettingRow("AutoPeckRow", "AutoPeckToggle",
                () => PlayerPreferences.AutoPeckEnabled,
                v => PlayerPreferences.AutoPeckEnabled = v, "Auto-peck");
            BindSettingRow("BuzzWhenHitRow", "BuzzWhenHitToggle",
                () => PlayerPreferences.BuzzWhenHitEnabled,
                v => PlayerPreferences.BuzzWhenHitEnabled = v, "Buzz when hit");
            BindVolumeSlider("MusicVolumeSlider", () => PlayerPreferences.MusicVolume,
                v => { PlayerPreferences.MusicVolume = v; _audioService?.SetMusicVolume(v); });
            BindVolumeSlider("SfxVolumeSlider", () => PlayerPreferences.SfxVolume,
                v => { PlayerPreferences.SfxVolume = v; _audioService?.SetSfxVolume(v); });

            // Dev Mode refreshes #DevRow the moment it changes — the Ability Lab button sits
            // right behind the sheet, so waiting for the next ShowMainMenu would look broken.
            BindSettingRow("DeveloperModeRow", "DeveloperModeToggle",
                () => PlayerPreferences.DeveloperModeEnabled,
                v => { PlayerPreferences.DeveloperModeEnabled = v; RefreshDevRow(); }, "Developer mode");

            // Buzz When Hit only means something on a phone: desktop and the Editor have no vibrator, so the row
            // is hidden there rather than offering a switch that can never do anything.
            if (!Application.isMobilePlatform)
                Bind<VisualElement>(_settingsSheet, "BuzzWhenHitRow", r => UiElements.SetShown(r, false));

            // Release builds have no Dev Mode at all (re-audit item 17): the row goes, and the
            // Performance row becomes the last one (no divider under it).
            if (!DevToolsAvailable)
            {
                Bind<VisualElement>(_settingsSheet, "DeveloperModeRow", r => UiElements.SetShown(r, false));
                Bind<VisualElement>(_settingsSheet, "PerformanceModeRow", r => r.AddToClassList("cw-setting-toggle-row--last"));
            }
        }

        /// <summary>
        /// Dev Mode (and so the Ability Lab) exists in development builds and the Editor only, or in
        /// a build compiled with <c>CLUCKWARS_DEV_TOOLS</c>. A release build hides the toggle and
        /// ignores a Dev Mode preference left over from a development build. The rule itself is
        /// <see cref="CluckWars.Diagnostics.DevTools.Available"/>, shared with the test-session tools.
        /// </summary>
        private static bool DevToolsAvailable => CluckWars.Diagnostics.DevTools.Available;

        /// <summary>
        /// Binds one settings row: seeds its Toggle from the preference and writes changes
        /// back, and makes the WHOLE row the touch target (a tap on the name or description
        /// flips the toggle too).
        /// </summary>
        /// <remarks>
        /// Seeded with <c>SetValueWithoutNotify</c>: the seed is not a player choice, and a
        /// ChangeEvent would echo the stored value straight back to <c>PlayerPrefs</c> on every
        /// build, turning "never chosen, using the default" into "explicitly chosen" (for Dev
        /// Mode it would write the key on every player's machine; for Performance Mode it would
        /// freeze the platform default). Re-seeded on every <see cref="OpenSettings"/> so the
        /// sheet always shows the stored value, whatever changed it.
        /// </remarks>
        private void BindSettingRow(string rowName, string toggleName, Func<bool> get, Action<bool> set, string logName)
        {
            var row = _settingsSheet.Q<VisualElement>(rowName);
            var toggle = _settingsSheet.Q<Toggle>(toggleName);
            if (row == null || toggle == null)
            {
                _log?.Error(Source, $"Settings sheet is missing #{rowName} or #{toggleName}; '{logName}' cannot be changed from the menu.");
                return;
            }

            toggle.SetValueWithoutNotify(get());
            toggle.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                _audio.Tap();
                _log?.Info(Source, $"{logName} {(evt.newValue ? "enabled" : "disabled")}.");
            });
            // The Toggle handles taps on itself; anywhere else in the row flips it here.
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement t && (t == toggle || toggle.Contains(t))) return;
                toggle.value = !toggle.value;
            });
            _settingToggles.Add((toggle, get));
        }

        private readonly List<(Toggle Toggle, Func<bool> Get)> _settingToggles = new();
        private readonly List<(Slider Slider, Func<float> Get, Label Percent)> _settingSliders = new();

        /// <summary>
        /// Binds one volume slider (0..1): seeded silently from the stored level, applied live while dragging, flushed
        /// to disk when the drag ends. A tap plays on release so the new level is heard.
        /// </summary>
        private void BindVolumeSlider(string sliderName, Func<float> get, Action<float> set)
        {
            var slider = _settingsSheet.Q<Slider>(sliderName);
            if (slider == null)
            {
                _log?.Error(Source, $"Settings sheet is missing #{sliderName}; that volume cannot be changed from the menu.");
                return;
            }

            // "73%" next to the name (Phase 7 B20). #MusicVolumePercent for #MusicVolumeSlider.
            var percent = _settingsSheet.Q<Label>(sliderName.Replace("Slider", "Percent"));
            if (percent == null)
                _log?.Warn(Source, $"Settings sheet has no percent readout for #{sliderName}; the volume shows no number.");

            slider.SetValueWithoutNotify(get());
            if (percent != null) percent.text = SliderRowRules.Percent(get());
            slider.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                if (percent != null) percent.text = SliderRowRules.Percent(evt.newValue);
            });
            slider.RegisterCallback<PointerUpEvent>(_ =>
            {
                PlayerPreferences.FlushVolumes();
                _audio.Tap();
            }, TrickleDown.TrickleDown);
            _settingSliders.Add((slider, get, percent));

            BindFullRowDrag(slider);
        }

        /// <summary>
        /// A press that lands on the slider's row but not on the slider itself (the name / percent header) drags the volume
        /// too: the pointer is captured by the row and mapped onto the track by x (<see cref="SliderRowRules"/>). Presses on
        /// the slider keep its own dragging.
        /// </summary>
        private void BindFullRowDrag(Slider slider)
        {
            var row = slider.parent;
            if (row == null) return;

            int pointer = -1;
            void Apply(float x)
            {
                var track = slider.worldBound;
                var dragger = slider.Q(className: "unity-base-slider__dragger");
                float thumb = Mathf.Max(SliderRowRules.MinThumbPx, dragger != null ? dragger.resolvedStyle.width : 0f);
                float f = SliderRowRules.FractionFromPointer(x, track.xMin, track.width, thumb);
                slider.value = SliderRowRules.ValueFromFraction(f, slider.lowValue, slider.highValue);
            }

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (pointer >= 0 || slider.worldBound.Contains(evt.position)) return;
                pointer = evt.pointerId;
                row.CapturePointer(pointer);
                Apply(evt.position.x);
                evt.StopPropagation();
            });
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != pointer || !row.HasPointerCapture(pointer)) return;
                Apply(evt.position.x);
            });
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != pointer) return;
                row.ReleasePointer(pointer);
                pointer = -1;
                PlayerPreferences.FlushVolumes();
                _audio.Tap();
            });
            row.RegisterCallback<PointerCaptureOutEvent>(_ => pointer = -1);
        }

        private void OpenSettings()
        {
            if (_settingsSheet == null) return;
            foreach (var (toggle, get) in _settingToggles) toggle.SetValueWithoutNotify(get());
            foreach (var (slider, get, percent) in _settingSliders)
            {
                slider.SetValueWithoutNotify(get());
                if (percent != null) percent.text = SliderRowRules.Percent(get());
            }
            UiElements.SetShown(_settingsSheet, true);
        }

        private void CloseSettings()
        {
            if (_settingsSheet == null || _settingsSheet.style.display == DisplayStyle.None) return;
            UiElements.SetShown(_settingsSheet, false);
            PlayerPreferences.FlushVolumes();
            RefreshDevRow();
        }

        /// <summary>
        /// Mirrors <see cref="PlayerPreferences.ReducedMotionEnabled"/> onto the root as
        /// <c>.cw-reduced-motion</c>, which zeroes every menu transition and hover/press pop
        /// (CluckWarsTheme.uss). Any animation added to these menus must be gated the same way.
        /// </summary>
        private void ApplyReducedMotion() =>
            _root?.EnableInClassList("cw-reduced-motion", PlayerPreferences.ReducedMotionEnabled);

        /// <summary>
        /// Shows or hides <c>#DevRow</c> to match
        /// <see cref="PlayerPreferences.DeveloperModeEnabled"/>.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="ShowMainMenu"/>, when the Dev Mode toggle changes and when the
        /// settings sheet closes — not once from <see cref="BuildMainMenu"/>. Binding once would
        /// mean the button only appeared on the next launch, which on a phone is a
        /// reinstall-and-relaunch cycle to discover a feature that is already there.
        /// </remarks>
        private void RefreshDevRow()
        {
            Bind<VisualElement>(_mainMenu, "DevRow", r =>
                UiElements.SetShown(r, DevToolsAvailable && PlayerPreferences.DeveloperModeEnabled));
        }

        /// <summary>
        /// Build-Settings name of the lab scene. Enabled in Build Settings since 2026-09-04 so
        /// it exists on a device; reaching it is gated on Developer Mode, not on the build.
        /// </summary>
        private const string AbilityLabSceneName = "AbilityLab";

        /// <summary>
        /// Loads <c>AbilityLab.unity</c>, the developer ability-feel scene.
        /// </summary>
        /// <remarks>
        /// <c>SceneManager</c> directly rather than <c>SceneLoader</c>: that component exists
        /// to run the Bootstrap → Game handoff and carries a serialized "next scene", so
        /// pointing it at the lab would mean mutating the menu's own flow to take a detour.
        /// The lab is a leaf — it loads, and its MENU button loads Bootstrap back.
        ///
        /// No <c>ISessionSelectionService</c> write here. <c>AbilityLabBootstrapper.Start</c>
        /// pins the mode to Solo itself, deliberately, rather than inheriting whatever the
        /// menu last left behind — its dummy patrol and held match timer are only sound on a
        /// single peer.
        /// </remarks>
        private void OpenAbilityLab()
        {
            if (!DevToolsAvailable || !PlayerPreferences.DeveloperModeEnabled)
            {
                // Not reachable through the UI — the row is hidden. Refuse anyway rather than
                // trust that, so the gate holds even if a later change shows the row wrongly.
                _log?.Warn(Source, "Ability Lab requested with Developer Mode off. Ignoring.");
                return;
            }

            _log?.Info(Source, "Opening the Ability Lab.");
            UnityEngine.SceneManagement.SceneManager.LoadScene(AbilityLabSceneName);
        }


    }
}
