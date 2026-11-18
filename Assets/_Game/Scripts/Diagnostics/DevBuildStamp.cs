using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Dev-tools builds only: the full version (<c>0.1+26462977</c>, <see cref="Application.version"/> + the
    /// <see cref="BuildInfo"/> build number) at the bottom-right of every screen, menus and match, PC and phone. One
    /// UI Toolkit document (<c>Resources/Dev/DevBuildStamp.uxml</c>) on a cross-scene object, above every game document.
    /// </summary>
    /// <remarks>
    /// Bound by <c>ProjectInstaller.InstallDiagnostics</c> (which runs only when <see cref="DevTools.Available"/>), and the
    /// component refuses to live without dev tools on its own. Bottom-right because the touch hexes' rings end 75 px above
    /// the edge, the desktop hex strip is bottom-centre, the joystick is bottom-left and the menu's CTA is bottom-centre. Never takes a tap. Read-only.
    /// </remarks>
    public sealed class DevBuildStamp : MonoBehaviour
    {
        private const string Source = "Diagnostics";
        public const string UxmlResource = "Dev/DevBuildStamp";
        public const string PanelSettingsResource = "PanelSettings";
        /// <summary>Above the match overlays (100), the touch HUD (95) and the top bar (90).</summary>
        public const int SortingOrder = 200;

        private ILogService _log;
        private SafeAreaPadding _safeArea;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        /// <summary>The stamp text: <paramref name="version"/> + "+" + the build-number label (<c>unknown</c> / <c>editor</c> pass through).</summary>
        public static string Compose(string version, string buildLabel) =>
            UiText.Format(UiKeys.DevBuildStamp, ("version", version), ("build", buildLabel));

        private void Awake()
        {
            if (!DevTools.Available)
            {
                Destroy(gameObject);
                return;
            }
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
        }

        private void Start()
        {
            if (!DevTools.Available) return;

            var tree = Resources.Load<VisualTreeAsset>(UxmlResource);
            var panel = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (tree == null || panel == null)
            {
                _log?.Warn(Source, $"DevBuildStamp not shown: Resources/{(tree == null ? UxmlResource : PanelSettingsResource)} is missing.");
                enabled = false;
                return;
            }

            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.sortingOrder = SortingOrder;
            doc.visualTreeAsset = tree;

            var root = doc.rootVisualElement;
            var stamp = root?.Q<Label>("BuildStamp");
            var stampRoot = root?.Q<VisualElement>("StampRoot");
            if (stamp == null || stampRoot == null)
            {
                _log?.Warn(Source, "DevBuildStamp not shown: DevBuildStamp.uxml has no StampRoot / BuildStamp.");
                enabled = false;
                return;
            }

            root.pickingMode = PickingMode.Ignore;
            // A failed load is already warned by SessionStamp; the label still carries version+unknown (or +editor).
            BuildInfo.TryLoad(out _, out _, out var buildLabel, out _);
            stamp.text = Compose(Application.version, buildLabel);

            _safeArea = new SafeAreaPadding(stampRoot);
        }

        // The safe area can change (rotation, the -cw_safearea dev override); Apply is cheap when nothing moved.
        private void Update() => _safeArea?.Apply();
    }
}
