using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Every specialization (subclass) passive, ordered by class then Subclass id.</summary>
        private List<PassiveAbilitySO> _passives = new List<PassiveAbilitySO>();
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
            _passives  = _abilities.OfType<PassiveAbilitySO>()
                .OrderBy(p => PreEquippedLoadout.TryGetSingleClass(p.AllowedClasses, out var c) ? (int)c : 99)
                .ThenBy(p => (byte)p.Subclass)
                .ThenBy(p => p.name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            DrawPreEquippedLoadouts();
            GUILayout.Space(16);
            DrawPreEquippedCompleteness();
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
            ColHeader("MoveSpd", wF);
            ColHeader("Turn°",   wF);
            ColHeader("Cap",     wI);
            ColHeader("Peck", wF);
            ColHeader("PkCD", wF);
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
        /// <b>The rule (Maestro, 2026-10-06):</b> <see cref="AbilityBaseSO.AllowedClasses"/> alone
        /// decides who can pick an ability (the Common/Character slot kind is retired).
        /// <c>All</c> = shared; one class = that class's own; several-but-not-all is a design smell
        /// (warned); <c>None</c> is LEGAL and means pre-equip-only — never in the picker, bot presets
        /// or backfill, obtainable only as a subclass Peck-slot/Signature-slot pre-equip (Peck itself is
        /// None-class: forced via the Peck slot column, never hand-picked). A None
        /// ability claimed in neither slot column is unreachable dead data (warned).
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
            var preEquipOnly = new List<AbilityBaseSO>();

            foreach (var a in _abilities)
            {
                if (a == null || a is PassiveAbilitySO) continue;
                int bits = CountClassBits(a.AllowedClasses);
                if (bits == 0) preEquipOnly.Add(a);
                else if (bits > 1 && a.AllowedClasses != ChickenClassFlags.All) multi.Add(a);
            }

            var unreachable = PreEquippedLoadout.FindUnreachablePreEquipOnly(_abilities);

            if (multi.Count == 0 && unreachable.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Every ability is either shared (All), single-class, or pre-equip-only and referenced by a " +
                    "subclass. Class identity is intact.",
                    MessageType.Info);
            }
            else
            {
                if (multi.Count > 0)
                    EditorGUILayout.HelpBox(
                        multi.Count + " ability(s) are shared by SOME classes but not all, which " +
                        "erodes class identity. Give each one a single class (or All):" + NewLineList(multi),
                        MessageType.Warning);

                if (unreachable.Count > 0)
                    EditorGUILayout.HelpBox(
                        unreachable.Count + " pre-equip-only (None) ability(s) are not pre-equipped by any " +
                        "subclass (both slot columns empty) — unreachable dead data:" + NewLineList(unreachable.ToList()),
                        MessageType.Warning);
            }

            if (preEquipOnly.Count > 0)
                EditorGUILayout.HelpBox(
                    "Pre-equip-only (AllowedClasses = None; never pickable): " +
                    string.Join(", ", preEquipOnly.Select(a => a.DisplayName)),
                    MessageType.None);

            // Per-class census of what the class can actually PICK (its flag is set; None is
            // excluded). Four slots get equipped, so a class needs more than four available
            // before the loadout screen is offering an actual decision.
            EditorGUILayout.BeginHorizontal();
            foreach (var cls in RealClasses)
            {
                int own = 0, shared = 0;
                foreach (var a in _abilities)
                {
                    if (a == null || a is PassiveAbilitySO || (a.AllowedClasses & cls) == 0) continue;
                    if (a.AllowedClasses == ChickenClassFlags.All) shared++; else own++;
                }

                int total = own + shared;
                var prev = GUI.color;
                if (total < 5) GUI.color = new Color(1f, 0.6f, 0.4f);
                EditorGUILayout.LabelField(
                    cls + ": " + total + "  (" + own + " own + " + shared + " shared)",
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

        // ---- Pre-equipped loadouts (read-only) -----------------------------------

        /// <summary>
        /// Read-only per-subclass view of what each specialization starts with. The ABILITY owns
        /// the assignment (the "Peck slot" / "Signature slot" columns of the Abilities table), so this is a
        /// derived view — a second edit path would have to silently resolve conflicts. On a
        /// conflict every claimant is listed.
        /// </summary>
        private void DrawPreEquippedLoadouts()
        {
            EditorGUILayout.LabelField("Pre-equipped loadouts (edit in the Abilities table, 'Peck slot' / 'Signature slot' columns)", EditorStyles.boldLabel);
            DrawRule();

            const float wSub = 110, wCls = 70, wSlot = 220;

            EditorGUILayout.BeginHorizontal();
            ColHeader("Subclass", wSub);
            ColHeader("Class", wCls);
            ColHeader("Peck slot", wSlot);
            ColHeader("Signature slot", wSlot);
            EditorGUILayout.EndHorizontal();
            DrawRule();

            foreach (var p in _passives)
            {
                if (p == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(p.Subclass.ToString(), GUILayout.Width(wSub));
                EditorGUILayout.LabelField(
                    PreEquippedLoadout.TryGetSingleClass(p.AllowedClasses, out var cls) ? cls.ToString() : "?",
                    GUILayout.Width(wCls));
                EditorGUILayout.LabelField(ClaimantText(PreEquippedLoadout.PeckSlotClaimants(_abilities, p.Subclass)), GUILayout.Width(wSlot));
                EditorGUILayout.LabelField(ClaimantText(PreEquippedLoadout.SignatureClaimants(_abilities, p.Subclass)), GUILayout.Width(wSlot));
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string ClaimantText(List<AbilityBaseSO> claims)
        {
            if (claims.Count == 0) return "—";
            string names = string.Join(" / ", claims.Select(a => a.DisplayName));
            return claims.Count > 1 ? "⚠ conflict: " + names : names;
        }

        // ---- Pre-equipped completeness -------------------------------------------

        /// <summary>
        /// Reports, per subclass, whether both pre-equipped slots are authored and legal
        /// (<see cref="PreEquippedLoadout.ValidateRoster"/>), then an at-a-glance table.
        /// Gaps are reported, not blocked — filling them is a design decision.
        /// </summary>
        private void DrawPreEquippedCompleteness()
        {
            EditorGUILayout.LabelField("Pre-equipped completeness", EditorStyles.boldLabel);
            DrawRule();

            var issues = PreEquippedLoadout.ValidateRoster(_passives, _abilities, _abilities.OfType<PeckAbilitySO>().FirstOrDefault());

            // Per-subclass status lines.
            foreach (var p in _passives)
            {
                if (p == null) continue;
                var mine = issues.Where(i => i.Passive == p).ToList();
                var prev = GUI.color;
                var worst = mine.Count == 0 ? PreEquipSeverity.Info : mine.Max(i => i.Severity);
                bool complete = !mine.Any(i => i.Severity != PreEquipSeverity.Info);
                GUI.color = worst == PreEquipSeverity.Error ? new Color(1f, 0.5f, 0.45f)
                    : worst == PreEquipSeverity.Warning ? new Color(1f, 0.85f, 0.4f)
                    : new Color(0.55f, 0.9f, 0.55f);
                EditorGUILayout.LabelField(
                    p.DisplayName + " (" + p.Subclass + "): " +
                    (mine.Count == 0 ? "OK" : (complete ? "OK (notes) — " : "") + string.Join("; ", mine.Select(DescribeIssue))),
                    EditorStyles.miniLabel);
                GUI.color = prev;
            }

            // Roster-level issues with no passive to hang them on.
            var orphans = issues.Where(i => i.Passive == null).ToList();
            if (orphans.Count > 0)
                EditorGUILayout.HelpBox(string.Join(System.Environment.NewLine, orphans.Select(i => i.Message)),
                    MessageType.Error);

            // Overall summary. Info notes are listed but never count against "complete".
            var blocking = issues.Where(i => i.Severity != PreEquipSeverity.Info).ToList();
            var notes = issues.Where(i => i.Severity == PreEquipSeverity.Info).ToList();
            if (blocking.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "All " + _passives.Count + " subclasses have a legal Peck slot and Signature slot." +
                    (notes.Count > 0 ? System.Environment.NewLine + "Notes:" + System.Environment.NewLine +
                        string.Join(System.Environment.NewLine, notes.Select(i => "  " + i.Message)) : ""),
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    blocking.Count + " pre-equip issue(s):" + System.Environment.NewLine +
                    string.Join(System.Environment.NewLine, blocking.Select(i => "  " + i.Message)),
                    blocking.Any(i => i.Severity == PreEquipSeverity.Error) ? MessageType.Error : MessageType.Warning);
                if (notes.Count > 0)
                    EditorGUILayout.HelpBox("Notes:" + System.Environment.NewLine +
                        string.Join(System.Environment.NewLine, notes.Select(i => "  " + i.Message)), MessageType.Info);
            }
        }

        private static string DescribeIssue(PreEquipIssue i)
        {
            switch (i.Status)
            {
                case PreEquipStatus.MissingPeckSlot:       return "missing Peck slot";
                case PreEquipStatus.MissingSignature:      return "missing Signature";
                case PreEquipStatus.IllegalPeckSlot:       return "illegal Peck slot (Assassin cannot forage)";
                case PreEquipStatus.IllegalSignature:      return "illegal Signature (Peck belongs in the Peck slot)";
                case PreEquipStatus.MissingSubclassId:     return "Subclass id is None";
                case PreEquipStatus.SubclassClassMismatch: return "Subclass/class mismatch";
                case PreEquipStatus.DuplicateSubclass:     return "duplicate Subclass id";
                case PreEquipStatus.SlotConflict:          return "slot conflict (several abilities claim it)";
                case PreEquipStatus.SameAbilityInBothSlots: return "same ability in both slots";
                case PreEquipStatus.PeckIsPickable:        return "Peck is hand-pickable (Classes must be None)";
                case PreEquipStatus.CannotForage:          return "note: no pre-equipped Peck (this subclass cannot forage)";
                case PreEquipStatus.OffClassAbility:       return "note: off-class ability";
                default:                                   return i.Status.ToString();
            }
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

            const float wName = 150, wF = 60, wClasses = 150, wColor = 54, wFold = 18, wSlotCol = 130, wMark = 18;

            EditorGUILayout.BeginHorizontal();
            ColHeader("", wFold);
            ColHeader("Ability",  wName);
            ColHeader("Classes",  wClasses);
            ColHeader("Peck slot", wSlotCol + wMark + 4);
            ColHeader("Signature slot", wSlotCol + wMark + 4);
            ColHeader("Duration", wF);
            ColHeader("Cooldown", wF);
            ColHeader("Accent",   wColor);
            EditorGUILayout.EndHorizontal();

            DrawRule();

            foreach (var a in _abilities)
            {
                if (a == null) continue;
                if (!VisibleUnderFilter(a)) continue;

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();

                bool open = _expanded.Contains(a);
                bool nowOpen = EditorGUILayout.Toggle(open, EditorStyles.foldout, GUILayout.Width(wFold));
                if (nowOpen != open)
                {
                    if (nowOpen) _expanded.Add(a); else _expanded.Remove(a);
                }

                EditorGUILayout.LabelField(a.DisplayName, GUILayout.Width(wName));
                DrawClassesCell(a, wClasses);

                // Pre-equip ownership lives on the ability, as two flag columns: which subclasses
                // hold it in their Peck slot and which in their Signature slot. Same SetDirty/_dirty
                // path as every other column (EndChangeCheck below).
                a.PeckSlotPreEquippedBy = (ChickenSubclassFlags)EditorGUILayout.EnumFlagsField(
                    a.PeckSlotPreEquippedBy, GUILayout.Width(wSlotCol));
                DrawMark(PeckSlotProblem(a), wMark);
                a.SignaturePreEquippedBy = (ChickenSubclassFlags)EditorGUILayout.EnumFlagsField(
                    a.SignaturePreEquippedBy, GUILayout.Width(wSlotCol));
                DrawMark(SignatureSlotProblem(a), wMark);

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
                case "AccentColor":
                case "Duration":
                case "Cooldown":
                case "AllowedClasses":
                case "AbilityAnimationClip":
                // Edited in the "Peck slot" / "Signature slot" columns, so not editable in two places.
                case "PeckSlotPreEquippedBy":
                case "SignaturePreEquippedBy":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The Classes cell. AllowedClasses means "hand-selectable in the picker", so a Peck is shown
        /// read-only as None (it is forced via the Peck slot column). If a Peck's data says otherwise
        /// the field stays editable, tinted red, so the designer can fix it.
        /// </summary>
        private static void DrawClassesCell(AbilityBaseSO a, float width)
        {
            var prevColor = GUI.color;
            if (a is PeckAbilitySO)
            {
                if (a.AllowedClasses == ChickenClassFlags.None)
                {
                    EditorGUILayout.LabelField(new GUIContent("None",
                        "Peck is never hand-picked; it is forced via the Peck slot column"), GUILayout.Width(width));
                    return;
                }
                GUI.color = new Color(1f, 0.5f, 0.45f);
                a.AllowedClasses = (ChickenClassFlags)EditorGUILayout.EnumFlagsField(
                    new GUIContent("", "ERROR: Peck must be None (never hand-picked; it is forced via the Peck slot column)."),
                    a.AllowedClasses, GUILayout.Width(width));
                GUI.color = prevColor;
                return;
            }

            // A flags field, so a deliberate two-class ability stays POSSIBLE. The integrity
            // panel above tints and reports it rather than the type system forbidding it.
            if (CountClassBits(a.AllowedClasses) > 1 && a.AllowedClasses != ChickenClassFlags.All)
                GUI.color = new Color(1f, 0.75f, 0.4f);
            a.AllowedClasses = (ChickenClassFlags)EditorGUILayout.EnumFlagsField(a.AllowedClasses, GUILayout.Width(width));
            GUI.color = prevColor;
        }

        /// <summary>Why this ability's Peck-slot column is wrong, or null.</summary>
        private static string PeckSlotProblem(AbilityBaseSO a)
        {
            if (a is PeckAbilitySO && a.PeckSlotPreEquippedBy.Subclasses().Any(sc => sc.ClassOf() == ChickenClass.Assassin))
                return "A Peck cannot be pre-equipped by an Assassin subclass: the Assassin cannot forage.";
            return SameAbilityBothSlotsProblem(a);
        }

        /// <summary>Why this ability's Signature column is wrong, or null.</summary>
        private static string SignatureSlotProblem(AbilityBaseSO a)
        {
            if (a is PeckAbilitySO && a.SignaturePreEquippedBy != ChickenSubclassFlags.None)
                return "Peck belongs in the Peck slot, never the Signature slot.";
            return SameAbilityBothSlotsProblem(a);
        }

        private static string SameAbilityBothSlotsProblem(AbilityBaseSO a)
        {
            var both = a.PeckSlotPreEquippedBy & a.SignaturePreEquippedBy;
            return both == ChickenSubclassFlags.None
                ? null
                : "Same ability in BOTH slots of: " + string.Join(", ", both.Subclasses()) + ".";
        }

        private static void DrawMark(string problem, float width)
        {
            if (problem == null) { GUILayout.Space(width + 4); return; }
            var prev = GUI.contentColor;
            GUI.contentColor = new Color(1f, 0.4f, 0.35f);
            GUILayout.Label(new GUIContent("\u26A0", problem), GUILayout.Width(width));
            GUI.contentColor = prev;
        }

        /// <summary>
        /// Class filter for the Abilities table. An ability is shown for a class if that class can PICK
        /// it, or a subclass of that class holds it in either slot column. Without the second clause a pre-equip-only
        /// (AllowedClasses = None) ability such as Mark/Kill vanished from every class filter and could
        /// not be edited there.
        /// </summary>
        private bool VisibleUnderFilter(AbilityBaseSO a)
        {
            if (_filter == ChickenClassFlags.None) return true;
            if ((a.AllowedClasses & _filter) != 0) return true;
            foreach (var sc in (a.PeckSlotPreEquippedBy | a.SignaturePreEquippedBy).Subclasses())
                if (AbilityRegistrySO.FlagOf(sc.ClassOf()) != ChickenClassFlags.None
                    && (AbilityRegistrySO.FlagOf(sc.ClassOf()) & _filter) != 0) return true;
            return false;
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
