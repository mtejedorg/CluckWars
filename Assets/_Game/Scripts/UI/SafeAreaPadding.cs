using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// Pads UI Toolkit elements so their content stays out of a notch, cutout or gesture bar
    /// while their background still bleeds to the screen edges. Same rule as
    /// <c>MenuUiController.ApplySafeArea</c> (that copy pads the menu's page host); this shared
    /// static is what the in-match overlays use. TODO(Phase 4 dedupe): point the menu at it too.
    /// </summary>
    /// <remarks>
    /// <c>Screen.safeArea</c> is in screen pixels with a bottom-left origin; a panel works in its
    /// own scaled units with a top-left origin, so each corner goes through
    /// <see cref="RuntimePanelUtils.ScreenToPanel"/> after flipping Y. A safe area covering the
    /// whole screen (desktop, Editor) pads nothing.
    /// </remarks>
    public sealed class SafeAreaPadding
    {
        private readonly VisualElement[] _targets;
        private Rect _applied;
        private Vector2Int _appliedScreen;
        private bool _hasApplied;

        public SafeAreaPadding(params VisualElement[] targets)
        {
            _targets = targets ?? Array.Empty<VisualElement>();
        }

        /// <summary>Re-applies when the safe area or screen size changed (or <paramref name="force"/>). Cheap to poll each frame.</summary>
        public void Apply(bool force = false)
        {
            var sa = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && _hasApplied && sa == _applied && screen == _appliedScreen) return;

            IPanel panel = null;
            foreach (var t in _targets) if (t?.panel != null) { panel = t.panel; break; }
            if (panel == null) return; // not attached yet: retry on the next poll

            _applied = sa;
            _appliedScreen = screen;
            _hasApplied = true;

            var insets = Insets(sa, screen, p => RuntimePanelUtils.ScreenToPanel(panel, p));
            foreach (var t in _targets)
            {
                if (t == null) continue;
                t.style.paddingLeft = insets.x;
                t.style.paddingTop = insets.y;
                t.style.paddingRight = insets.z;
                t.style.paddingBottom = insets.w;
            }
        }

        /// <summary>
        /// Left / top / right / bottom insets in panel units for <paramref name="safeArea"/> on a
        /// <paramref name="screen"/>-sized display. <paramref name="screenToPanel"/> maps a
        /// top-left-origin screen point into the panel.
        /// </summary>
        public static Vector4 Insets(Rect safeArea, Vector2Int screen, Func<Vector2, Vector2> screenToPanel)
        {
            bool fullScreen = safeArea.xMin <= 0f && safeArea.yMin <= 0f
                && safeArea.xMax >= screen.x && safeArea.yMax >= screen.y;
            if (fullScreen || screen.x <= 0 || screen.y <= 0) return Vector4.zero;

            Vector2 min = screenToPanel(new Vector2(safeArea.xMin, screen.y - safeArea.yMax));
            Vector2 max = screenToPanel(new Vector2(safeArea.xMax, screen.y - safeArea.yMin));
            Vector2 full = screenToPanel(new Vector2(screen.x, screen.y));
            return new Vector4(
                Mathf.Max(0f, min.x),
                Mathf.Max(0f, min.y),
                Mathf.Max(0f, full.x - max.x),
                Mathf.Max(0f, full.y - max.y));
        }
    }
}
