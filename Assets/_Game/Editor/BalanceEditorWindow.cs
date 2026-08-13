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
            GUILayout.Space(20);
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
                s.CollectionRate = FloatField(s.CollectionRate, wF, minVal: 0f);
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

        // ---- Ability table -------------------------------------------------------

        private void DrawAbilityTable()
        {
            EditorGUILayout.LabelField("Abilities", EditorStyles.boldLabel);
            DrawRule();

            const float wName = 140, wF = 64, wColor = 64;

            EditorGUILayout.BeginHorizontal();
            ColHeader("Ability",  wName);
            ColHeader("Duration", wF);
            ColHeader("Cooldown", wF);
            ColHeader("Accent",   wColor);
            EditorGUILayout.EndHorizontal();

            DrawRule();

            foreach (var a in _abilities)
            {
                if (a == null) continue;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(a.DisplayName, GUILayout.Width(wName));
                a.Duration   = FloatField(a.Duration,  wF, minVal: 0.05f);
                a.Cooldown   = FloatField(a.Cooldown,  wF, minVal: 0f);
                a.AccentColor = EditorGUILayout.ColorField(
                    GUIContent.none, a.AccentColor,
                    showEyedropper: false, showAlpha: false, hdr: false,
                    GUILayout.Width(wColor));

                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(a);
                    _dirty = true;
                }
            }
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
