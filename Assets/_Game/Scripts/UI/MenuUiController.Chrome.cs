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
    /// <see cref="MenuUiController"/>: the menu's chrome: the music and click bed, and the animated backdrop behind every page (A21, architecture audit 2026-11-12).
    /// </summary>
    /// <remarks>
    /// One component, several files. The Bootstrap scene references this type by name and every serialized
    /// field and bound element reference has to stay on it, so the split is a partial class, never a second
    /// component. Navigation stays in the main file: it owns the page list, ShowPage, the back stack and the
    /// build order, and calls into each page's Build / Refresh here.
    /// </remarks>
    public sealed partial class MenuUiController
    {
        // ---- Menu audio ---------------------------------------------------------
        // Buttons get the generic tap from OnRootClick, except those registered below: BACK-type
        // buttons (softer cue) and buttons whose handler plays a more specific cue itself (so the
        // tap would double up).
        private readonly HashSet<VisualElement> _backCueButtons = new();
        private readonly HashSet<VisualElement> _ownCueButtons = new();

        private void BackCue(Button b) => _backCueButtons.Add(b);
        private void OwnCue(Button b) => _ownCueButtons.Add(b);

        private void OnRootClick(ClickEvent evt)
        {
            for (var ve = evt.target as VisualElement; ve != null; ve = ve.parent)
            {
                if (ve is not Button) continue;
                if (!ve.enabledInHierarchy || _ownCueButtons.Contains(ve)) return;
                if (_backCueButtons.Contains(ve)) _audio.Back(); else _audio.Tap();
                return;
            }
        }

        private void LateUpdate() { if (_stage != null) RunOnStage(_stage.Tick); }

        private void OnDisable() { DisposeStage(); DisposeJuice(); }

        private void OnDestroy() { DisposeStage(); DisposeJuice(); }

        /// <summary>Ends every running effect (landing / stamp cues still fire) and the countdown; the next BuildAll makes new ones.</summary>
        private void DisposeJuice()
        {
            _juice?.Dispose();
            _juice = null;
            _stampPending = false;
        }

        private VisualElement ClonePage(VisualTreeAsset vta)
        {
            if (vta == null) return new VisualElement();
            var ve = vta.Instantiate();
            UiText.ResolveTree(ve);
            ve.style.flexGrow = 1;
            UiElements.SetShown(ve, false);
            ve.AddToClassList(PageWrapClass);
            _pageHost.Add(ve);
            return ve;
        }

        private static void StretchToParent(VisualElement ve)
        {
            ve.style.position = Position.Absolute;
            ve.style.left = 0; ve.style.top = 0; ve.style.right = 0; ve.style.bottom = 0;
        }


        // ---- Backdrop -----------------------------------------------------------
        /// <summary>How a page wants the shared backdrop: which painted scene (a USS modifier class
        /// on #Backdrop) and the colour of the tint layer over it.</summary>
        private readonly struct BackdropSpec
        {
            public readonly string SceneClass;
            public readonly Color Tint;
            public BackdropSpec(string sceneClass, Color tint) { SceneClass = sceneClass; Tint = tint; }
        }

        // Phase 2: one painted golden-hour scene per screen (Art/UI/Backgrounds/Bg_*.png), chosen by
        // a USS modifier class rather than Resources.Load, so the textures stay in Art/UI (imported
        // by UiSpriteImportSettings) and the stylesheet is the one place that names them. The tint
        // layer also carries the vignette (.cw-backdrop-tint); its colour is a light ink scrim that
        // knocks the bright scenes back behind the cards, or the class wash on PICK YOUR BIRD.
        private const string SceneMain = "cw-backdrop--main", SceneClass = "cw-backdrop--class",
                             SceneLoadout = "cw-backdrop--loadout", SceneLobby = "cw-backdrop--lobby";
        private static readonly Color ScrimMenu = new Color(0.10f, 0.05f, 0.02f, 0.10f);
        private static readonly Color ScrimBusy = new Color(0.10f, 0.05f, 0.02f, 0.22f);
        private string _appliedScene;

        private BackdropSpec BackdropFor(VisualElement page)
        {
            if (page == _classSelect)
            {
                // PICK YOUR BIRD: washed with the selected class (lightened so it stays sunny).
                var t = Lighten(TintOf(Cls), 0.25f);
                return new BackdropSpec(SceneClass, new Color(t.r, t.g, t.b, 0.30f));
            }
            if (page == _loadout) return new BackdropSpec(SceneLoadout, ScrimBusy);
            if (page == _lobby)   return new BackdropSpec(SceneLobby, ScrimBusy);
            return new BackdropSpec(SceneMain, ScrimMenu);
        }

        private VisualElement _currentPage;

        /// <summary>Applies <see cref="BackdropFor"/> for the current page to the shared backdrop.</summary>
        private void ApplyBackdrop()
        {
            if (_backdrop == null || _backdropTint == null) return;
            var spec = BackdropFor(_currentPage);
            if (_appliedScene != spec.SceneClass)
            {
                if (_appliedScene != null) _backdrop.RemoveFromClassList(_appliedScene);
                _backdrop.AddToClassList(spec.SceneClass);
                _appliedScene = spec.SceneClass;
            }
            _backdropTint.style.backgroundColor = spec.Tint;
        }


    }
}
