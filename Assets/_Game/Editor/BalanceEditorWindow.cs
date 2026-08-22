using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Editor
{
    /// <summary>
    /// One-screen balance spreadsheet for all classes and abilities.
    /// Open via <b>Cluck Wars ▶ Balance ▶ Balance Editor</b>.
    /// All fields are live-editable; click <b>Save All</b> to flush changes to disk.
    /// </summary>
    /// <remarks>
    /// Uses <c>AssetDatabase.FindAssets</c> to discover every
    /// <see cref="ChickenStatsSO"/> and <see cref="AbilityBaseSO"/> asset in the
    /// project automatically — no manual list to maintain.
    /// </remarks>
    public sealed class BalanceEditorWindow : EditorWindow
    {
        // ---- Menu entry ----------------------------------------------------------

        [MenuItem("Cluck Wars/Balance/Balance Editor", false, 200)]
        private static void Open() => GetWindow<BalanceEditorWindow>("Balance Editor");

        // ---- State ---------------------------------------------------------------

        private List<ChickenStatsSO> _classes   = new List<ChickenStatsSO>();
        private List<AbilityBaseSO>  _abilities = new List<AbilityBaseSO>();
        private Vector2              _scroll;
        private bool                 _dirty;

        /// <summary>Which ability rows have their tuned-value block expanded.</summary>
        private readonly HashSet<Object> _expanded = new HashSet<Object>();

        /// <summary>Roster filter. <c>None</c> means "show everything".</summary>
        private ChickenClassFlags _filter = ChickenClassFlags.None;

        // ---- Lifecycle -----------------------------------------------------------

        private void OnEnable() => Reload();

        private void Reload()
        {
            _classes   = LoadAssets<ChickenStatsSO>("t:ChickenStatsSO");
            _abilities = LoadAssets<AbilityBaseSO>("t:AbilityBaseSO");
            _dirty = false;
            Repaint();
        }

        private static List<T> LoadAssets<T>(string filter) where T : ScriptableObject
        {
            var list  = new List<T>();
            var guids = AssetDatabase.FindAssets(filter);
            foreach (var guid in guids)
            {
                var path  = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) list.Add(asset);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name,
                System.StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // ---- GUI -----------------------------------------------------------------

        private void OnGUI()
        {
            // ── Toolbar ─────────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Reload();
            GUILayout.FlexibleSpace();
            var savedColor = GUI.color;
            if (_dirty) GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.enabled = _dirty;
            if (GUILayout.Button("Save All ★", EditorStyles.toolbarButton, GUILayout.Width(80)))
                SaveAll();
            GUI.enabled = true;
            GUI.color = savedColor;
            EditorGUILayout.EndHorizontal();

            // ── Scrollable body ──────────────────────────────────────────────────
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawClassTable();
            GUILayout.Space(16);
            DrawRosterIntegrity();
            GUILayout.Space(16);
            DrawAbilityTable();

            EditorGUILayout.EndScrollView();
        }

        // ---- Class table ---------------------------------------------------------

        private void DrawClassTable()
        {
            EditorGUILayout.LabelField("Classes", EditorStyles.boldLabel);
            DrawRule();

            // Column widths
            const float wName = 90, wF = 52, wI = 42;

            // Header row
            EditorGUILayout.BeginHorizontal();
            ColHeader("Class",   wName);
            // The Passive column is gone: the class specialization now lives on the equipped
            // PassiveAbilitySO asset, not as an enum on the stats, so there is nothing to edit here.
            ColHeader("Peck", wF);
            ColHeader("PkCD", wF);
            ColHeader("MoveSpd", wF);
            ColHeader("Turn°",   wF);
            ColHeader("Cap",     wI);
            ColHeader("Rate",    wF);
            ColHeader("Scale",   wF);
            EditorGUILayout.EndHorizontal();

            DrawRule();

            foreach (var s in _classes)
            {
                if (s == null) continue;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(s.DisplayName, GUILayout.Width(wName));
                s.MoveSpeed      = FloatField(s.MoveSpeed,      wF, minVal: 0f);
                s.TurnSpeed      = FloatField(s.TurnSpeed,      wF, minVal: 0f);
                s.CargoCapacity  = IntField(s.CargoCapacity,    wI, minVal: 1);
                s.PeckAmount     = FloatField(s.PeckAmount,     wF, minVal: 0.1f);
                s.PeckCooldown   = FloatField(s.PeckCooldown,   wF, minVal: 0.05f);
                s.Scale          = FloatField(s.Scale,          wF, minVal: 0.1f);

                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(s);
                    _dirty = true;
                }
            }
        }

        // ---- Roster integrity ----------------------------------------------------

        /// <summary>
        /// Live check of the roster rule, plus a per-class census.
        /// </summary>
        /// <remarks>
        /// <b>The rule (Maestro, 2026-08-21):</b> classes keep their identity, so a
        /// <see cref="AbilitySlotKind.Character"/> ability belongs to EXACTLY ONE class.
        /// Sharing is expressed only by <see cref="AbilitySlotKind.Common"/> plus
        /// <see cref="ChickenClassFlags.All"/>. The field stays a bitmask on purpose: one class
        /// is a DESIGN limit, not a code limit, so nothing here forbids a deliberate two-class
        /// ability later — it simply will not pass unnoticed.
        ///
        /// Surfaced as a panel and not only as a unit test because the reassignment is done by
        /// hand in this window: you want the violation list shrinking as you fix it, and a class
        /// pool going thin the moment it happens rather than at the next test run.
        /// </remarks>
        private void DrawRosterIntegrity()
        {
            EditorGUILayout.LabelField("Roster integrity", EditorStyles.boldLabel);
            DrawRule();

            var multi = new List<AbilityBaseSO>();
            var commonNotAll = new List<AbilityBaseSO>();
            var orphaned = new List<AbilityBaseSO>();

            foreach (var a in _abilities)
            {
                if (a == null) continue;
                int bits = CountClassBits(a.AllowedClasses);

                if (a.SlotKind == AbilitySlotKind.Common)
                {
                    if (a.AllowedClasses != ChickenClassFlags.All) commonNotAll.Add(a);
                }
                else
                {
                    if (bits == 0) orphaned.Add(a);
                    else if (bits > 1) multi.Add(a);
                }
            }

            if (multi.Count == 0 && commonNotAll.Count == 0 && orphaned.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Every Character ability belongs to exactly one class, and every Common " +
                    "ability is available to All. Class identity is intact.",
                    MessageType.Info);
            }
            else
            {
                if (multi.Count > 0)
                    EditorGUILayout.HelpBox(
                        multi.Count + " Character ability(s) are shared across classes, which " +
                        "erodes class identity. Give each one a single class, and author a new " +
                        "ability for whichever class loses it:" + NewLineList(multi),
                        MessageType.Warning);

                if (commonNotAll.Count > 0)
                    EditorGUILayout.HelpBox(
                        commonNotAll.Count + " Common ability(s) are not set to All. A Common " +
                        "ability is by definition the shared pool:" + NewLineList(commonNotAll),
                        MessageType.Warning);

                if (orphaned.Count > 0)
                    EditorGUILayout.HelpBox(
                        orphaned.Count + " Character ability(s) have NO class and can never be " +
                        "equipped:" + NewLineList(orphaned),
                        MessageType.Error);
            }

            // Per-class census. Four slots get equipped, so a class needs more than four
            // available before the loadout screen is offering an actual decision.
            EditorGUILayout.BeginHorizontal();
            foreach (var cls in RealClasses)
            {
                int character = 0, common = 0;
                foreach (var a in _abilities)
                {
                    if (a == null || (a.AllowedClasses & cls) == 0) continue;
                    if (a.SlotKind == AbilitySlotKind.Common) common++; else character++;
                }

                int total = character + common;
                var prev = GUI.color;
                if (total < 5) GUI.color = new Color(1f, 0.6f, 0.4f);
                EditorGUILayout.LabelField(
                    cls + ": " + total + "  (" + character + " own + " + common + " common)",
                    EditorStyles.miniLabel, GUILayout.Width(190));
                GUI.color = prev;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "Four slots are equipped, so fewer than 5 available means no real choice.",
                EditorStyles.miniLabel);
        }

        private static string NewLineList(List<AbilityBaseSO> items)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var a in items)
                sb.Append(System.Environment.NewLine).Append("  ")
                  .Append(a.DisplayName).Append(" -> ").Append(a.AllowedClasses);
            return sb.ToString();
        }

        // ---- Ability table -------------------------------------------------------

        private void DrawAbilityTable()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Abilities", EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Show class:", EditorStyles.miniLabel, GUILayout.Width(70));
            _filter = (ChickenClassFlags)EditorGUILayout.EnumPopup(_filter, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
            DrawRule();

            const float wName = 150, wF = 60, wSlot = 78, wClasses = 150, wColor = 54, wFold = 18;

            EditorGUILayout.BeginHorizontal();
            ColHeader("", wFold);
            ColHeader("Ability",  wName);
            ColHeader("Slot",     wSlot);
            ColHeader("Classes",  wClasses);
            ColHeader("Duration", wF);
            ColHeader("Cooldown", wF);
            ColHeader("Accent",   wColor);
            EditorGUILayout.EndHorizontal();

            DrawRule();

            foreach (var a in _abilities)
            {
                if (a == null) continue;
                if (_filter != ChickenClassFlags.None && (a.AllowedClasses & _filter) == 0) continue;

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();

                bool open = _expanded.Contains(a);
                bool nowOpen = EditorGUILayout.Toggle(open, EditorStyles.foldout, GUILayout.Width(wFold));
                if (nowOpen != open)
                {
                    if (nowOpen) _expanded.Add(a); else _expanded.Remove(a);
                }

                EditorGUILayout.LabelField(a.DisplayName, GUILayout.Width(wName));
                a.SlotKind = (AbilitySlotKind)EditorGUILayout.EnumPopup(
                    a.SlotKind, GUILayout.Width(wSlot));

                // A flags field, so a deliberate two-class ability stays POSSIBLE. The integrity
                // panel above tints and reports it rather than the type system forbidding it.
                var prevColor = GUI.color;
                if (a.SlotKind == AbilitySlotKind.Character && CountClassBits(a.AllowedClasses) > 1)
                    GUI.color = new Color(1f, 0.75f, 0.4f);
                a.AllowedClasses = (ChickenClassFlags)EditorGUILayout.EnumFlagsField(
                    a.AllowedClasses, GUILayout.Width(wClasses));
                GUI.color = prevColor;

                a.Duration   = FloatField(a.Duration,  wF, minVal: 0.05f);
                a.Cooldown   = FloatField(a.Cooldown,  wF, minVal: 0f);
                a.AccentColor = EditorGUILayout.ColorField(
                    GUIContent.none, a.AccentColor,
                    showEyedropper: false, showAlpha: false, hdr: false,
                    GUILayout.Width(wColor));

                EditorGUILayout.EndHorizontal();

                if (nowOpen) DrawAbilityEffectValues(a);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(a);
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// Every tuned number an ability actually exposes, discovered through
        /// <see cref="SerializedObject"/> rather than listed per type.
        /// </summary>
        /// <remarks>
        /// Reflection instead of a hand-written column set, because the tuned fields differ per
        /// ability (StunRadius, StealAmount, SnatchRange, PushStrength, SweepRadius...) and a
        /// hardcoded list would silently omit anything added later. That is the same staleness
        /// class that let a wrong TerrainTraversal and a shared Shadowstep ship unnoticed.
        /// Fields already shown as columns are skipped so no value is editable in two places.
        /// </remarks>
        private void DrawAbilityEffectValues(AbilityBaseSO ability)
        {
            var so = new SerializedObject(ability);
            var prop = so.GetIterator();
            bool enterChildren = true;

            EditorGUI.indentLevel += 2;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (SkipInEffectBlock(prop.name)) continue;
                EditorGUILayout.PropertyField(prop, true);
            }
            EditorGUI.indentLevel -= 2;

            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(ability);
                _dirty = true;
            }
        }

        /// <summary>Already shown as a column, or not a balance value at all.</summary>
        private static bool SkipInEffectBlock(string propertyName)
        {
            switch (propertyName)
            {
                case "m_Script":
                case "DisplayName":
                case "ShortLabel":
                case "Description":
                case "Icon":
                case "AccentColor":
                case "Duration":
                case "Cooldown":
                case "SlotKind":
                case "AllowedClasses":
                case "AbilityAnimationClip":
                    return true;
                default:
                    return false;
            }
        }

        private static readonly ChickenClassFlags[] RealClasses =
        {
            ChickenClassFlags.Warrior, ChickenClassFlags.Speedy,
            ChickenClassFlags.Fatty,   ChickenClassFlags.Assassin,
        };

        private static int CountClassBits(ChickenClassFlags flags)
        {
            int n = 0;
            foreach (var c in RealClasses) if ((flags & c) != 0) n++;
            return n;
        }

        // ---- Helpers -------------------------------------------------------------

        private void SaveAll()
        {
            AssetDatabase.SaveAssets();
            _dirty = false;
        }

        private static void ColHeader(string label, float width) =>
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel,
                GUILayout.Width(width));

        private static void DrawRule()
        {
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }

        private static float FloatField(float value, float width, float minVal)
        {
            float v = EditorGUILayout.FloatField(value, GUILayout.Width(width));
            return Mathf.Max(minVal, v);
        }

        private static int IntField(int value, float width, int minVal)
        {
            int v = EditorGUILayout.IntField(value, GUILayout.Width(width));
            return Mathf.Max(minVal, v);
        }
    }
}
