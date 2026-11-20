using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CluckWars.Balance;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Dev-tools builds only: the local balance editor. Lists every overridable field of the class stats, abilities,
    /// specializations and MatchConfig with its shipped and current value, takes edits, CHECKs them (the one validation in
    /// <see cref="BalanceBook.Evaluate"/> plus the Balance Oracle's before -> after numbers on copies, <see cref="BalanceSctCheck"/>, which reports and never blocks), APPLYes them on this
    /// device through <see cref="BalanceLocalOverride"/> (only while the apply window is open; otherwise it stores them for
    /// next launch), and shows and copies the delta in force so the owner can publish it with <c>/balance publish dev</c>
    /// on the PC. No network, no credential, no publish (ARCH-DATA-14, owner ruling 2026-11-18).
    /// </summary>
    /// <remarks>
    /// Bound by <c>ProjectInstaller.InstallDiagnostics</c> (which runs only when <see cref="DevTools.Available"/>); the
    /// component also refuses to live, and <see cref="Open"/> refuses to show, without dev tools. The menu reaches it through
    /// <see cref="IDevBalanceEditor"/> (Presentation cannot see this assembly). Kits are not editable here: a kit change in
    /// the delta in force is carried over unchanged. One UI Toolkit document (<c>Resources/Dev/DevBalanceEditor.uxml</c>) with
    /// a row template (<c>DevBalanceRow.uxml</c>), above every other document.
    /// </remarks>
    public sealed class DevBalanceEditor : MonoBehaviour, IDevBalanceEditor
    {
        private const string Source = "Diagnostics";
        public const string UxmlResource = "Dev/DevBalanceEditor";
        public const string RowResource = "Dev/DevBalanceRow";
        public const string PanelSettingsResource = "PanelSettings";
        /// <summary>Above the dev build stamp (200), the match overlays (100) and everything else.</summary>
        public const int SortingOrder = 210;

        private ILogService _log;
        private BalanceBook _book;
        private BalanceLocalOverride _local;

        private UIDocument _doc;
        private VisualTreeAsset _rowTemplate;
        private VisualElement _root;
        private SafeAreaPadding _safeArea;
        private ScrollView _list;
        private Label _state;
        private Label _report;
        private TextField _filter;
        private TextField _deltaText;
        private Button _checkBtn, _applyBtn, _resetBtn, _copyBtn;
        private bool _ready;

        private List<AssetGroup> _groups = new List<AssetGroup>();
        private readonly Dictionary<string, BalanceEdit> _edits = new Dictionary<string, BalanceEdit>(StringComparer.Ordinal);
        /// <summary>The delta that passed the last CHECK with no edit since; null otherwise (APPLY needs it).</summary>
        private string _checkedJson;
        /// <summary>The book's hash when that CHECK passed: if it moved (a remote delta landed), the check is stale.</summary>
        private string _checkedHash;
        /// <summary>The delta stored for next launch by the last APPLY (window closed); shown and copied until reset.</summary>
        private string _pendingJson;
        private string _pendingHash;

        private sealed class AssetGroup
        {
            public string Name;
            public string Group;
            public readonly List<BalanceFieldInfo> Fields = new List<BalanceFieldInfo>();
        }

        [Inject]
        public void Construct(ILogService log, BalanceBook book, BalanceLocalOverride local)
        {
            _log = log;
            _book = book;
            _local = local;
        }

        private void Awake()
        {
            if (!DevTools.Available)
            {
                Destroy(gameObject);
                return;
            }
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
        }

        // ---- Open / close --------------------------------------------------------------------------------------

        public void Open()
        {
            if (!DevTools.Available) { _log?.Warn(Source, "Balance editor requested without dev tools. Ignoring."); return; }
            if (!EnsureUi()) return;

            _edits.Clear();
            _checkedJson = null;
            _checkedHash = null;
            _filter.SetValueWithoutNotify(string.Empty);
            LoadFields();
            RebuildList();
            Show(UiText.Get(UiKeys.DevBalanceStatusIdle));
            RefreshState();
            UiElements.SetShown(_root, true);
        }

        private void Close() => UiElements.SetShown(_root, false);

        private void Update()
        {
            if (_root != null && UiElements.IsShown(_root)) _safeArea?.Apply();
        }

        private bool EnsureUi()
        {
            if (_ready) return true;
            if (_book == null || _local == null) { _log?.Warn(Source, "Balance editor not shown: the balance services are not injected."); return false; }

            var tree = Resources.Load<VisualTreeAsset>(UxmlResource);
            _rowTemplate = Resources.Load<VisualTreeAsset>(RowResource);
            var panel = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (tree == null || _rowTemplate == null || panel == null)
            {
                string missing = tree == null ? UxmlResource : _rowTemplate == null ? RowResource : PanelSettingsResource;
                _log?.Warn(Source, UiText.Format(UiKeys.DevBalanceUnavailable, ("reason", $"Resources/{missing} is missing")));
                return false;
            }

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = panel;
            _doc.sortingOrder = SortingOrder;
            _doc.visualTreeAsset = tree;

            var docRoot = _doc.rootVisualElement;
            if (docRoot != null) docRoot.pickingMode = PickingMode.Ignore; // BalanceRoot (the scrim) and its panel take the input
            _root = docRoot?.Q<VisualElement>("BalanceRoot");
            _list = docRoot?.Q<ScrollView>("List");
            _state = docRoot?.Q<Label>("StateLine");
            _report = docRoot?.Q<Label>("Report");
            _filter = docRoot?.Q<TextField>("Filter");
            _deltaText = docRoot?.Q<TextField>("DeltaText");
            _checkBtn = docRoot?.Q<Button>("CheckBtn");
            _applyBtn = docRoot?.Q<Button>("ApplyBtn");
            _resetBtn = docRoot?.Q<Button>("ResetBtn");
            _copyBtn = docRoot?.Q<Button>("CopyBtn");
            var closeBtn = docRoot?.Q<Button>("CloseBtn");
            if (_root == null || _list == null || _state == null || _report == null || _filter == null || _deltaText == null
                || _checkBtn == null || _applyBtn == null || _resetBtn == null || _copyBtn == null || closeBtn == null)
            {
                _log?.Warn(Source, UiText.Format(UiKeys.DevBalanceUnavailable, ("reason", "DevBalanceEditor.uxml is missing an element")));
                Destroy(_doc);
                _doc = null;
                return false;
            }

            UiText.ResolveTree(docRoot);
            _deltaText.multiline = true;
            _deltaText.isReadOnly = true;
            _filter.RegisterValueChangedCallback(_ => RebuildList());
            closeBtn.clicked += Close;
            _checkBtn.clicked += RunCheck;
            _applyBtn.clicked += ApplyChecked;
            _resetBtn.clicked += ResetLocal;
            _copyBtn.clicked += CopyDelta;
            _safeArea = new SafeAreaPadding(_root);
            UiElements.SetShown(_root, false);
            _ready = true;
            return true;
        }

        // ---- The field list ------------------------------------------------------------------------------------

        private void LoadFields()
        {
            _groups = new List<AssetGroup>();
            AssetGroup current = null;
            foreach (var f in _book.DescribeFields())
            {
                if (current == null || current.Name != f.Asset)
                {
                    current = new AssetGroup { Name = f.Asset, Group = f.Group };
                    _groups.Add(current);
                }
                current.Fields.Add(f);
            }
        }

        /// <summary>True when a field passes the filter text (asset or field name contains it, ignoring case; blank passes all).</summary>
        public static bool Matches(string asset, string field, string filter) =>
            string.IsNullOrWhiteSpace(filter)
            || asset.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
            || field.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        private void RebuildList()
        {
            _list.Clear();
            string filter = _filter.value;
            bool filtering = !string.IsNullOrWhiteSpace(filter);
            foreach (var group in _groups)
            {
                var shown = new List<BalanceFieldInfo>();
                foreach (var f in group.Fields) if (Matches(f.Asset, f.Field, filter)) shown.Add(f);
                if (shown.Count == 0) continue;

                var foldout = new Foldout
                {
                    text = UiText.Format(UiKeys.DevBalanceGroup, ("asset", group.Name), ("group", GroupLabel(group.Group))),
                    value = false,
                };
                bool built = false;
                void BuildRows()
                {
                    if (built) return;
                    built = true;
                    foreach (var f in shown) foldout.Add(MakeRow(f));
                }
                foldout.RegisterValueChangedCallback(e => { if (e.newValue) BuildRows(); });
                _list.Add(foldout);
                if (filtering) { foldout.SetValueWithoutNotify(true); BuildRows(); }
            }
        }

        private static string GroupLabel(string group)
        {
            switch (group)
            {
                case "Class": return UiText.Get(UiKeys.DevBalanceGroupClass);
                case "Specialization": return UiText.Get(UiKeys.DevBalanceGroupSpecialization);
                case "Match": return UiText.Get(UiKeys.DevBalanceGroupMatch);
                default: return UiText.Get(UiKeys.DevBalanceGroupAbility);
            }
        }

        private static string EditKey(string asset, string field) => asset + "." + field;

        private VisualElement MakeRow(BalanceFieldInfo f)
        {
            var row = _rowTemplate.Instantiate();
            var rowBox = row.Q<VisualElement>("Row") ?? row;
            row.Q<Label>("FieldName").text = f.Field;
            row.Q<Label>("Shipped").text = f.Shipped;
            row.Q<Label>("Current").text = f.Current;
            rowBox.EnableInClassList("cw-bal-row--changed", f.Current != f.Shipped);

            string key = EditKey(f.Asset, f.Field);
            _edits.TryGetValue(key, out var pending);
            string start = pending.Field != null ? pending.Text : f.Current;
            rowBox.EnableInClassList("cw-bal-row--edited", pending.Field != null);

            var number = row.Q<TextField>("NumberInput");
            var toggle = row.Q<Toggle>("BoolInput");
            var dropdown = row.Q<DropdownField>("EnumInput");
            UiElements.SetShown(number, f.Kind == BalanceFieldKind.Number || f.Kind == BalanceFieldKind.Whole);
            UiElements.SetShown(toggle, f.Kind == BalanceFieldKind.Bool);
            UiElements.SetShown(dropdown, f.Kind == BalanceFieldKind.Enum);

            switch (f.Kind)
            {
                case BalanceFieldKind.Bool:
                    toggle.SetValueWithoutNotify(start == "true");
                    toggle.RegisterValueChangedCallback(e => OnEdit(rowBox, f, e.newValue ? "true" : "false"));
                    break;
                case BalanceFieldKind.Enum:
                    dropdown.choices = new List<string>(f.EnumNames);
                    dropdown.SetValueWithoutNotify(start);
                    dropdown.RegisterValueChangedCallback(e => OnEdit(rowBox, f, e.newValue));
                    break;
                default:
                    number.SetValueWithoutNotify(start);
                    number.RegisterValueChangedCallback(e => OnEdit(rowBox, f, e.newValue));
                    break;
            }
            return row;
        }

        private void OnEdit(VisualElement rowBox, BalanceFieldInfo f, string text)
        {
            string key = EditKey(f.Asset, f.Field);
            bool changed = !SameText(text, f.Current);
            if (changed) _edits[key] = new BalanceEdit(f.Asset, f.Field, text);
            else _edits.Remove(key);
            rowBox.EnableInClassList("cw-bal-row--edited", changed);
            _checkedJson = null;
            RefreshButtons();
        }

        /// <summary>Input text equals the current value text (numbers compared as numbers, so "3.50" equals "3.5").</summary>
        private static bool SameText(string a, string b)
        {
            if (string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal)) return true;
            return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                   && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && x == y;
        }

        // ---- Check / apply / reset / copy ----------------------------------------------------------------------

        private void RunCheck()
        {
            _checkedJson = null;
            if (!_book.TryBuildDeltaJson(_edits.Values, out string json, out var problems))
            {
                Show(UiText.Format(UiKeys.DevBalanceStatusProblems, ("list", string.Join("\n", problems))));
                RefreshButtons();
                return;
            }

            var verdict = _book.Evaluate(json, "Local check");
            if (!verdict.IsAccepted)
            {
                Show(UiText.Format(UiKeys.DevBalanceStatusRefused, ("reason", verdict.Reason)));
                RefreshButtons();
                return;
            }
            if (verdict.IsEmpty)
            {
                _checkedJson = json;
                _checkedHash = _book.Hash;
                Show(UiText.Get(UiKeys.DevBalanceStatusNoChange));
                RefreshButtons();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(UiText.Format(UiKeys.DevBalanceStatusChanges, ("count", verdict.Changes.Count), ("list", string.Join("\n", verdict.Changes))));
            if (verdict.Ignored.Count > 0)
                sb.AppendLine(UiText.Format(UiKeys.DevBalanceStatusIgnored, ("list", string.Join("\n", verdict.Ignored))));

            // The oracle reports; it never gates the apply.
            sb.Append(OracleText(_book.CheckOracle(verdict)));
            _checkedJson = json;
            _checkedHash = _book.Hash;
            Show(sb.ToString());
            RefreshButtons();
        }

        /// <summary>The report lines for one oracle check: why it did not run, or each kit's best food before -> after and what the oracle does not model.</summary>
        public static string OracleText(OracleCheck check)
        {
            if (!check.Relevant) return UiText.Format(UiKeys.DevBalanceOracleSkipped, ("reason", check.Note ?? string.Empty));
            if (!check.Ran) return UiText.Format(UiKeys.DevBalanceOracleUnavailable, ("reason", check.Note ?? string.Empty));

            var sb = new StringBuilder(UiText.Get(UiKeys.DevBalanceOracleHeader));
            foreach (var row in check.Rows())
                sb.Append('\n').Append(UiText.Format(UiKeys.DevBalanceOracleLine,
                    ("label", row.Label),
                    ("before", Number(row.Before)),
                    ("after", Number(row.After)),
                    ("change", Signed(row.Change))));
            if (check.After.NotModelled.Count > 0)
            {
                sb.Append('\n').Append(UiText.Get(UiKeys.DevBalanceOracleNotModelled));
                foreach (var item in check.After.NotModelled) sb.Append('\n').Append(UiText.Format(UiKeys.DevBalanceOracleNotModelledItem, ("item", item)));
            }
            return sb.ToString();
        }

        private static string Number(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
        private static string Signed(float value) => (value >= 0f ? "+" : string.Empty) + Number(value);

        private void ApplyChecked()
        {
            if (_checkedJson == null) return;
            if (_checkedHash != _book.Hash)
            {
                // The balance in force changed since CHECK (a remote delta landed): what was checked is not what would apply.
                _checkedJson = null;
                Show(UiText.Get(UiKeys.DevBalanceStatusStale));
                RefreshState();
                return;
            }
            var result = _local.Apply(_checkedJson);
            if (result.Outcome == LocalOverrideOutcome.StoredForNextLaunch)
            {
                _pendingJson = _book.PublishJsonOf(result.Verdict);
                _pendingHash = result.Verdict.Hash;
            }
            if (result.Outcome == LocalOverrideOutcome.Applied || result.Outcome == LocalOverrideOutcome.Cleared) { _pendingJson = null; _pendingHash = null; }
            Show(ResultText(result));
            AfterChange(result.Outcome == LocalOverrideOutcome.Applied || result.Outcome == LocalOverrideOutcome.Cleared);
        }

        private void ResetLocal()
        {
            var result = _local.Reset();
            _pendingJson = null;
            _pendingHash = null;
            Show(ResultText(result));
            AfterChange(true);
        }

        private string ResultText(LocalOverrideResult result)
        {
            switch (result.Outcome)
            {
                case LocalOverrideOutcome.Applied:
                    return UiText.Format(UiKeys.DevBalanceResultApplied, ("hash", result.Verdict.Hash));
                case LocalOverrideOutcome.StoredForNextLaunch:
                    return UiText.Format(UiKeys.DevBalanceResultStored, ("hash", result.Verdict.Hash));
                case LocalOverrideOutcome.Cleared:
                    return UiText.Format(UiKeys.DevBalanceResultCleared, ("hash", _book.Label), ("origin", _book.Origin));
                case LocalOverrideOutcome.ClearedNextLaunch:
                    return UiText.Format(UiKeys.DevBalanceResultClearedLater, ("hash", _book.Label));
                case LocalOverrideOutcome.NotAllowed:
                    return UiText.Get(UiKeys.DevBalanceResultNotAllowed);
                default:
                    return UiText.Format(UiKeys.DevBalanceResultRefused, ("reason", result.Reason ?? string.Empty));
            }
        }

        /// <summary>After a change of the delta in force: forget the edits, re-read the fields so NOW is right, refresh the header.</summary>
        private void AfterChange(bool reloadFields)
        {
            _checkedJson = null;
            _checkedHash = null;
            if (reloadFields)
            {
                _edits.Clear();
                LoadFields();
                RebuildList();
            }
            RefreshState();
        }

        private void CopyDelta()
        {
            string json = _pendingJson ?? _book.PublishJson;
            _deltaText.SetValueWithoutNotify(json);
            // The log line is the adb logcat fallback; the read-only field is the on-screen one.
            _log.Info(Source, $"Balance delta in force (bal={_book.Hash}, {_book.Origin}) for /balance publish dev: {json}");
            try
            {
                GUIUtility.systemCopyBuffer = json;
                if (GUIUtility.systemCopyBuffer != json)
                {
                    _log.Warn(Source, "Balance editor: the clipboard did not keep the delta (read back differently).");
                    Show(UiText.Get(UiKeys.DevBalanceCopyFailed));
                    return;
                }
                Show(UiText.Format(UiKeys.DevBalanceCopied, ("hash", _pendingHash ?? _book.Hash)));
            }
            catch (Exception e)
            {
                _log.Warn(Source, $"Balance editor: the clipboard is not available ({e.GetType().Name}: {e.Message}).");
                Show(UiText.Get(UiKeys.DevBalanceCopyFailed));
            }
        }

        // ---- Header, buttons, report ---------------------------------------------------------------------------

        private void RefreshState()
        {
            string window = UiText.Get(_book.ApplyWindowOpen ? UiKeys.DevBalanceWindowOpen : UiKeys.DevBalanceWindowClosed);
            string state = UiText.Format(UiKeys.DevBalanceState, ("hash", _book.Hash), ("origin", _book.Origin), ("window", window));
            if (_pendingJson != null) state += "\n" + UiText.Format(UiKeys.DevBalancePending, ("hash", _pendingHash));
            _state.text = state;
            _deltaText.SetValueWithoutNotify(_pendingJson ?? _book.PublishJson);
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            _applyBtn.text = UiText.Get(_book.ApplyWindowOpen ? UiKeys.DevBalanceApply : UiKeys.DevBalanceApplyLater);
            _applyBtn.SetEnabled(_checkedJson != null);
        }

        private void Show(string text) => _report.text = text;
    }
}
