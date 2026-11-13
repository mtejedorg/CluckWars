using System;
using System.Collections.Generic;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The three pieces of UI Toolkit plumbing every screen needed and each one had written out for
    /// itself (A22, architecture audit 2026-11-12): show / hide an element, make sure a runtime panel
    /// has an EventSystem to click with, and find a named element in a UXML tree.
    /// </summary>
    /// <remarks>
    /// Before this, <c>SetShown</c> existed three times (<c>MarkHudController</c>,
    /// <c>MatchHudController</c>, <c>MatchOverlaysController</c>) with the same body, around 45 more
    /// <c>style.display</c> assignments were written inline in <c>MenuUiController</c>, and
    /// <c>EnsureEventSystem</c> existed four times (the menu, the match HUD, the overlays and the
    /// Ability Lab). There is nothing clever here; the point is that there is one of each.
    /// </remarks>
    public static class UiElements
    {
        /// <summary>
        /// Shows or hides <paramref name="element"/>. Null is a no-op: an element that never bound is
        /// already reported by <see cref="Bind{T}"/> / <see cref="Query{T}"/>, so failing again here
        /// would only cost the rest of the screen its refresh. Writes the style only when it changes,
        /// so UI Toolkit does not mark the panel dirty every frame.
        /// </summary>
        public static void SetShown(VisualElement element, bool shown)
        {
            if (element == null) return;
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) element.style.display = display;
        }

        /// <summary>True when <paramref name="element"/> exists and is currently displayed.</summary>
        public static bool IsShown(VisualElement element) =>
            element != null && element.style.display.value == DisplayStyle.Flex;

        /// <summary>
        /// UI Toolkit runtime panels need an <c>EventSystem</c> (+ the Input System UI module) to receive
        /// pointer clicks. Scenes do not all carry one, and the one <c>MenuUiController</c> creates in
        /// Bootstrap is a plain <c>GameObject</c> that dies with the scene load — so the menu, the match
        /// HUD, the overlays and the Ability Lab each call this in <c>Awake</c>. It is a one-time
        /// bootstrap <c>Find</c>, listed in <c>SceneObjectAccessTests.AllowedFinds</c>; the guard on
        /// "none exists" means only ever one is created.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        // ---- Named lookups -------------------------------------------------------------------

        /// <summary>One warning per missing name, so a renamed UXML element says so once instead of every frame.</summary>
        private static readonly HashSet<string> ReportedMisses = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ReportedMisses.Clear();

        /// <summary>
        /// The element called <paramref name="name"/> under <paramref name="root"/>, or null. A miss is a
        /// wiring bug — the UXML was renamed and this screen now silently does nothing — so it is logged
        /// once per name rather than swallowed. A null <paramref name="root"/> is not reported: the caller
        /// that failed to bind the root already did.
        /// </summary>
        public static T Query<T>(VisualElement root, string name, ILogService log, string source) where T : VisualElement
        {
            if (root == null) return null;
            var element = root.Q<T>(name);
            if (element == null) ReportMiss(name, typeof(T), log, source);
            return element;
        }

        /// <summary>
        /// Runs <paramref name="act"/> on the element called <paramref name="name"/> when it is there, and
        /// reports it once when it is not. Returns whether it ran.
        /// </summary>
        public static bool Bind<T>(VisualElement root, string name, Action<T> act, ILogService log, string source)
            where T : VisualElement
        {
            var element = Query<T>(root, name, log, source);
            if (element == null) return false;
            act(element);
            return true;
        }

        private static void ReportMiss(string name, Type type, ILogService log, string source)
        {
            if (!ReportedMisses.Add(source + "/" + name)) return;
            log?.Warn(source, $"No {type.Name} called '{name}' in this UXML tree; the screen's binding for it does nothing. " +
                              "The element was renamed or removed in Assets/UI/*.uxml.");
        }
    }
}
