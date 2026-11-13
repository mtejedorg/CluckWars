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
    /// <see cref="MenuUiController"/>: the live 3D chicken stage behind the pages (Phase 3, decision 7; only while Performance Mode is off) (A21, architecture audit 2026-11-12).
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
        //  LIVE 3D CHICKEN STAGE (Phase 3, Decision 7)
        // ======================================================================
        // Performance Mode OFF: the hero and the lineup seats show the real models, rendered by
        // MenuChickenStage into RenderTextures painted over the elements' USS background. ON (the
        // mobile default), or if the stage cannot come up: the static Phase 2 renders, untouched.
        // GEAR UP (Phase 4): the chosen bird stands in the barn doorway (#GearChicken) and hops when
        // a move lands in a slot.

        /// <summary>
        /// Brings the stage in line with <see cref="PlayerPreferences.PerformanceModeEnabled"/>:
        /// creates and binds it (hero + seats) when live, disposes it (static renders) when not.
        /// Called at build time and whenever the Performance Mode toggle changes.
        /// </summary>
        private void RefreshStage()
        {
            if (!MenuStagePolicy.WantsLive(PlayerPreferences.PerformanceModeEnabled, _stageFailed))
            {
                DisposeStage();
                return;
            }
            if (_stage == null && !TryCreateStage()) return;

            ShowHeroOnStage(hop: false);
            for (int i = 0; i < _seats.Length; i++) ShowSeatOnStage(i);
            ShowGearOnStage(hop: false);
        }

        private bool TryCreateStage()
        {
            if (_classRegistry == null)
            {
                FailStage("ChickenClassRegistrySO not injected", null);
                return false;
            }
            try
            {
                _stage = new MenuChickenStage(_classRegistry, _log, GearStageSlot + 1, PanelPixelsPerPoint);
                _log?.Info(Source, "Live chicken stage on (Performance Mode off).");
                return true;
            }
            catch (Exception e)
            {
                FailStage("the stage could not be created", e);
                return false;
            }
        }

        /// <summary>Stage failure: log once, fall back to the static renders for this session.</summary>
        private void FailStage(string reason, Exception e)
        {
            _stageFailed = true;
            _log?.Warn(Source, $"Live chicken stage unavailable ({reason}{(e != null ? ": " + e.Message : "")}); showing the static renders.");
            DisposeStage();
        }

        private void DisposeStage()
        {
            if (_stage == null) return;
            _stage.Dispose();
            _stage = null;
            _log?.Info(Source, "Live chicken stage off.");
        }

        private void ShowHeroOnStage(bool hop)
        {
            if (_stage == null || _previewChicken == null) return;
            // Edges fade into the soft light behind the bird (same colour RefreshClassSelect paints the glow).
            var clear = Lighten(TintOf(Cls), 0.8f);
            // Decision 4 (Phase 4): the hero sways like the Coop seats instead of a full turntable.
            RunOnStage(() => _stage.Show(HeroStageSlot, _previewChicken, Cls, hop, sway: true, clear));
        }

        /// <summary>The GEAR UP doorway bird on the live stage; <paramref name="hop"/> = a move was just equipped.</summary>
        private void ShowGearOnStage(bool hop)
        {
            if (_stage == null || _gearChicken == null) return;
            // Edges fade into the sunlit doorway behind it.
            RunOnStage(() => _stage.Show(GearStageSlot, _gearChicken, Cls, hop, sway: true, UiGfx.Hex32("e0a050")));
        }

        private void ShowSeatOnStage(int idx)
        {
            if (_stage == null || _seats[idx] == null) return;
            int slot = 1 + idx;
            var cls = _seatClasses[idx];
            if (cls == null) { _stage.Clear(slot); return; }
            // Edges fade into the warm barn tone behind the lineup.
            RunOnStage(() => _stage.Show(slot, _seats[idx].Chicken, cls.Value, false, sway: true, UiGfx.Hex32("8a5a2e")));
        }

        private void RunOnStage(Action act)
        {
            try { act(); }
            catch (Exception e) { FailStage("a chicken could not be staged", e); }
        }

        /// <summary>Screen pixels per panel point: the panel's target width (its RenderTexture when
        /// one is set, else the screen) over the root's laid-out width.</summary>
        private float PanelPixelsPerPoint()
        {
            float rootW = _root?.layout.width ?? 0f;
            if (float.IsNaN(rootW) || rootW <= 0f) return 1f;
            var ps = GetComponent<UIDocument>().panelSettings;
            float targetW = ps != null && ps.targetTexture != null ? ps.targetTexture.width : Screen.width;
            return targetW / rootW;
        }


    }
}
