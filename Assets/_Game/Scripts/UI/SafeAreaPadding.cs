using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// Pads UI Toolkit elements so their content stays out of a notch, cutout or gesture bar
    /// while their background still bleeds to the screen edges. The one safe-area rule in the game:
    /// the menu's page host, the in-match overlays, the HUD top bar and the touch controls all use it.
    /// </summary>
    /// <remarks>
    /// <c>Screen.safeArea</c> is in screen pixels with a bottom-left origin; a panel works in its
    /// own scaled units with a top-left origin, so each corner goes through
    /// <see cref="RuntimePanelUtils.ScreenToPanel"/> after flipping Y. A safe area covering the
    /// whole screen (desktop, Editor) pads nothing.
    /// </remarks>
    public sealed class SafeAreaPadding
    {
        /// <summary>Which edges of a target take the insets.</summary>
        public enum Edge
        {
            /// <summary>Padding: the target's background bleeds to the screen edge, its flow content is inset.</summary>
            Padding,
            /// <summary>left / top / right / bottom: for a full-screen absolute zone whose children are absolutely
            /// positioned (the touch controls), which padding would not move.</summary>
            Offsets,
        }

        private readonly VisualElement[] _targets;
        private readonly Edge _edge;
        private Rect _applied;
        private Vector2Int _appliedScreen;
        private bool _hasApplied;

        public SafeAreaPadding(params VisualElement[] targets) : this(Edge.Padding, targets) { }

        public SafeAreaPadding(Edge edge, params VisualElement[] targets)
        {
            _edge = edge;
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
                if (_edge == Edge.Offsets)
                {
                    t.style.left = insets.x;
                    t.style.top = insets.y;
                    t.style.right = insets.z;
                    t.style.bottom = insets.w;
                }
                else
                {
                    t.style.paddingLeft = insets.x;
                    t.style.paddingTop = insets.y;
                    t.style.paddingRight = insets.z;
                    t.style.paddingBottom = insets.w;
                }
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
