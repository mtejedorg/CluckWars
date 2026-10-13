using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The cream focus ring on buttons is a pad / keyboard cue. A mouse click or a touch also focuses a
    /// Button, so without this the ring lingered on whatever a finger had just tapped (round-4 finding 19).
    /// Attach once to a panel root: any pointer press adds <see cref="NoRingClass"/> (the USS drops the ring),
    /// and the next key press or navigation event (D-pad, stick, Submit) removes it again.
    /// </summary>
    public static class NavFocusRing
    {
        /// <summary>Set on the panel root while the last input was a pointer; the theme hides the focus ring under it.</summary>
        public const string NoRingClass = "cw-no-focus-ring";

        /// <summary>
        /// Wires <paramref name="root"/>. A phone starts in pointer mode (a touch is the only input there);
        /// desktop starts with the ring allowed, so a pad / keyboard user sees where they are from the first frame.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            if (root == null) return;
            // Trickle-down so it runs before any child handler stops propagation.
            root.RegisterCallback<PointerDownEvent>(_ => SetPointerMode(root, true), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(_ => SetPointerMode(root, false), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(_ => SetPointerMode(root, false), TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(_ => SetPointerMode(root, false), TrickleDown.TrickleDown);
            SetPointerMode(root, Application.isMobilePlatform);
        }

        /// <summary>Pure state flip, exposed so a test can drive it without a live panel.</summary>
        public static void SetPointerMode(VisualElement root, bool pointer) =>
            root.EnableInClassList(NoRingClass, pointer);
    }
}
