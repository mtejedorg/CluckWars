using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>How the balance editor edits one field.</summary>
    public enum BalanceFieldKind : byte
    {
        /// <summary>A float or double.</summary>
        Number = 0,
        /// <summary>An integer type: whole numbers only.</summary>
        Whole = 1,
        Bool = 2,
        /// <summary>An enum: one of <see cref="BalanceFieldInfo.EnumNames"/>.</summary>
        Enum = 3,
    }

    /// <summary>One overridable field of one asset, as the balance editor lists it.</summary>
    public sealed class BalanceFieldInfo
    {
        public string Asset;
        /// <summary>"Class", "Specialization", "Ability" or "Match": the editor's grouping.</summary>
        public string Group;
        public string Field;
        public BalanceFieldKind Kind;
        /// <summary>The shipped value as text (an enum as its member name).</summary>
        public string Shipped;
        /// <summary>The value in force now as text (what a delta applied this launch made it).</summary>
        public string Current;
        public string[] EnumNames;
        /// <summary>The field's [Range] / [Min] bounds, NaN when it has none.</summary>
        public double Min = double.NaN;
        public double Max = double.NaN;
    }

    /// <summary>One typed value the balance editor wants a field to hold.</summary>
    public readonly struct BalanceEdit
    {
        public readonly string Asset;
        public readonly string Field;
        public readonly string Text;
        public BalanceEdit(string asset, string field, string text) { Asset = asset; Field = field; Text = text; }
    }

    /// <summary>
    /// The local half of the balance layer: the dev-tools balance editor's entry points into <see cref="BalanceBook"/>.
    /// Everything that applies, lists or builds a local delta checks <see cref="LocalAllowed"/> first, so a production
    /// build cannot reach it whatever is bound (ARCH-DATA-14, owner ruling 2026-11-18). Nothing here writes to a network
    /// service, holds a credential or publishes: the owner copies the delta and publishes it on the PC.
    /// </summary>
    public sealed partial class BalanceBook
    {
        /// <summary>The one decision of the local path, a pure function so a test can feed it both answers.</summary>
        public static bool LocalAllowed(bool devToolsAvailable) => devToolsAvailable;

        private bool CheckLocalAllowed(string what)
        {
            if (LocalAllowed(DevTools.Available)) return true;
            _log.Error(Source, $"Balance: {what} refused: the local balance path exists only in dev-tools builds.");
            return false;
        }

        // ---- Apply ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Applies an accepted verdict with origin <see cref="BalanceOrigin.Local"/>. False (nothing changed, an Error logged)
        /// without dev tools; otherwise exactly <see cref="Apply"/>: false for a verdict that was not accepted and while the
        /// apply window is closed (a match or lobby started this launch).
        /// </summary>
        public bool ApplyLocal(BalanceVerdict verdict) =>
            CheckLocalAllowed("local apply") && Apply(verdict, BalanceOrigin.Local);

        /// <summary>
        /// Evaluates (the one validation home) and applies <paramref name="json"/> locally. Without dev tools it is refused
        /// before anything is read, with a refused <paramref name="verdict"/> saying so.
        /// </summary>
        public bool TryApplyLocal(string json, out BalanceVerdict verdict)
        {
            if (!CheckLocalAllowed("local apply"))
            {
                verdict = new BalanceVerdict { Kind = BalanceVerdictKind.Refused, Reason = "the local balance path exists only in dev-tools builds" };
                return false;
            }
            verdict = Evaluate(json, "Local");
            return Apply(verdict, BalanceOrigin.Local);
        }

        // ---- What the editor lists -----------------------------------------------------------------------------

        /// <summary>
        /// Every field a delta may override, by asset (the fields <see cref="Evaluate"/> accepts: number, bool and enum, not
        /// deny-listed), with its shipped and current value. Empty without dev tools.
        /// </summary>
        public IReadOnlyList<BalanceFieldInfo> DescribeFields()
        {
            var list = new List<BalanceFieldInfo>();
            if (!CheckLocalAllowed("field listing")) return list;
            if (!TryBuildIndex(out var index, out var error))
            {
                _log.Error(Source, $"Balance fields unavailable: {error}");
                return list;
            }

            var names = new List<string>(index.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var so = index[name];
                var shipped = BaselineOf(so);
                BalanceJson.TryParse(JsonUtility.ToJson(so), out var live, out _);
                if (shipped.Members == null) continue;
                foreach (var member in shipped.Members)
                {
                    if (member.Value.Kind != JsonKind.Number && member.Value.Kind != JsonKind.Bool) continue;
                    var field = FindField(so.GetType(), member.Key);
                    if (field == null || IsDenied(field)) continue;

                    var info = new BalanceFieldInfo { Asset = name, Group = GroupOf(so), Field = member.Key, Kind = KindOf(field) };
                    info.Shipped = DisplayOf(field, member.Value);
                    info.Current = live != null && live.TryGet(member.Key, out var now) ? DisplayOf(field, now) : info.Shipped;
                    if (info.Kind == BalanceFieldKind.Enum) info.EnumNames = Enum.GetNames(field.FieldType);
                    var range = field.GetCustomAttribute<RangeAttribute>();
                    if (range != null) { info.Min = range.min; info.Max = range.max; }
                    var minAttr = field.GetCustomAttribute<MinAttribute>();
                    if (minAttr != null && (double.IsNaN(info.Min) || minAttr.min > info.Min)) info.Min = minAttr.min;
                    list.Add(info);
                }
            }
            return list;
        }

        /// <summary>
        /// The shipped value of <paramref name="field"/> on <paramref name="assetName"/> as a canonical JSON token (an enum as
        /// its number): the asset's snapshot when this book has changed it, else its live value. False for an unknown asset
        /// or field.
        /// </summary>
        public bool TryGetShippedToken(string assetName, string field, out string token)
        {
            token = null;
            if (!TryBuildIndex(out var index, out _) || !index.TryGetValue(assetName, out var so)) return false;
            if (!BaselineOf(so).TryGet(field, out var value) || (value.Kind != JsonKind.Number && value.Kind != JsonKind.Bool)) return false;
            token = TokenOf(value);
            return true;
        }

        private static string GroupOf(ScriptableObject so) =>
            so is MatchConfigSO ? "Match" : so is ChickenStatsSO ? "Class" : so is PassiveAbilitySO ? "Specialization" : "Ability";

        private static BalanceFieldKind KindOf(FieldInfo field)
        {
            var t = field.FieldType;
            if (t == typeof(bool)) return BalanceFieldKind.Bool;
            if (t.IsEnum) return BalanceFieldKind.Enum;
            return TryIntegerRange(t, out _, out _) ? BalanceFieldKind.Whole : BalanceFieldKind.Number;
        }

        /// <summary>A parsed field value as the editor shows it: true/false, an enum's member name, a float shortest-round-trip.</summary>
        private static string DisplayOf(FieldInfo field, JsonValue v)
        {
            if (v.Kind == JsonKind.Bool) return v.Bool ? "true" : "false";
            if (field.FieldType.IsEnum)
            {
                object value = Enum.ToObject(field.FieldType, (long)v.Number);
                return Enum.IsDefined(field.FieldType, value) ? Enum.GetName(field.FieldType, value) : v.Text;
            }
            return field.FieldType == typeof(float) ? ((float)v.Number).ToString("R", CultureInfo.InvariantCulture) : v.Text;
        }

        // ---- Building the delta --------------------------------------------------------------------------------

        /// <summary>
        /// Builds the full delta over the shipped assets that results from <paramref name="edits"/> laid over the delta in
        /// force now (so a field the editor never touched keeps its applied value, and a kit change from the remote delta
        /// is carried over unchanged). Only values that DIFFER from the shipped assets are kept, as <c>make_delta.py</c>
        /// does: a field edited back to its shipped value leaves the delta. Enums are written as member names, the form the
        /// publisher accepts. The result is not validated here: pass it to <see cref="Evaluate"/>. False, with
        /// <paramref name="problems"/>, when an edit names an unknown or deny-listed field or its text does not parse.
        /// </summary>
        public bool TryBuildDeltaJson(IEnumerable<BalanceEdit> edits, out string json, out List<string> problems)
        {
            json = null;
            problems = new List<string>();
            if (!CheckLocalAllowed("delta building")) { problems.Add("dev tools are off"); return false; }
            if (!TryBuildIndex(out var index, out var indexError)) { problems.Add(indexError); return false; }

            var table = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
            if (_current != null)
                foreach (var change in _current.Assets)
                {
                    var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in change.Tokens) fields[kv.Key] = PublishToken(change.Asset, kv.Key, kv.Value);
                    table[change.Name] = fields;
                }

            foreach (var edit in edits)
            {
                string where = $"{edit.Asset}.{edit.Field}";
                if (!index.TryGetValue(edit.Asset, out var so)) { problems.Add($"{edit.Asset}: no such asset"); continue; }
                var field = FindField(so.GetType(), edit.Field);
                if (field == null || IsDenied(field)) { problems.Add($"{where}: not an editable field"); continue; }
                if (!TryTokenFromText(field, edit.Text, out string token, out string why)) { problems.Add($"{where}: {why}"); continue; }
                if (!table.TryGetValue(edit.Asset, out var fields)) table[edit.Asset] = fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
                fields[edit.Field] = token;
            }
            if (problems.Count > 0) return false;

            var sb = new StringBuilder("{");
            bool anyAsset = false;
            var assets = new StringBuilder();
            foreach (var asset in table)
            {
                var so = index[asset.Key];
                var shipped = BaselineOf(so);
                var kept = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in asset.Value)
                {
                    var field = FindField(so.GetType(), kv.Key);
                    if (field != null && shipped.TryGet(kv.Key, out var shippedValue) && SameAsShipped(field, kv.Value, shippedValue)) continue;
                    kept[kv.Key] = kv.Value;
                }
                if (kept.Count == 0) continue;
                if (anyAsset) assets.Append(',');
                assets.Append(BalanceJson.Quote(asset.Key)).Append(':').Append(PartialJson(kept));
                anyAsset = true;
            }
            if (anyAsset) sb.Append("\"assets\":{").Append(assets).Append("},");

            if (_current != null && _current.Kits.Count > 0)
            {
                var kits = new List<BalanceVerdict.KitChange>(_current.Kits);
                kits.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                sb.Append("\"kits\":{");
                for (int i = 0; i < kits.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(BalanceJson.Quote(kits[i].Name)).Append(":[");
                    for (int j = 0; j < kits[i].Kit.Length; j++)
                    {
                        if (j > 0) sb.Append(',');
                        sb.Append(BalanceJson.Quote(kits[i].Kit[j].name));
                    }
                    sb.Append("]");
                }
                sb.Append("},");
            }
            json = sb.Append("\"schema\":1}").ToString();
            return true;
        }

        private static bool TryTokenFromText(FieldInfo field, string text, out string token, out string problem)
        {
            token = null;
            problem = null;
            string t = (text ?? string.Empty).Trim();
            var type = field.FieldType;

            if (type == typeof(bool))
            {
                if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) { token = "true"; return true; }
                if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) { token = "false"; return true; }
                problem = $"\"{t}\" is not true or false";
                return false;
            }
            if (type.IsEnum)
            {
                if (t.Length > 0 && char.IsLetter(t[0]) && Enum.IsDefined(type, t)) { token = BalanceJson.Quote(t); return true; }
                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)
                    && TryIntegerRange(Enum.GetUnderlyingType(type), out long lo, out long hi) && n >= lo && n <= hi
                    && Enum.IsDefined(type, Enum.ToObject(type, n)))
                {
                    token = BalanceJson.Quote(Enum.GetName(type, Enum.ToObject(type, n)));
                    return true;
                }
                problem = $"\"{t}\" is not a {type.Name} member";
                return false;
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
            {
                problem = $"\"{t}\" is not a number (use a point for decimals)";
                return false;
            }
            if (TryIntegerRange(type, out _, out _))
            {
                if (!IsIntegral(d)) { problem = $"\"{t}\" is not a whole number"; return false; }
                token = ((long)d).ToString(CultureInfo.InvariantCulture);
                return true;
            }
            token = type == typeof(float) ? ((float)d).ToString("R", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>True when the (publish-form) token equals the shipped value, compared the way the app reads it: floats as floats, enums by number.</summary>
        private static bool SameAsShipped(FieldInfo field, string token, JsonValue shipped)
        {
            if (shipped.Kind == JsonKind.Bool) return token == (shipped.Bool ? "true" : "false");
            if (shipped.Kind != JsonKind.Number) return false;
            if (field.FieldType.IsEnum)
            {
                string name = token.Trim('"');
                return Enum.IsDefined(field.FieldType, name)
                       && Convert.ToInt64(Enum.Parse(field.FieldType, name)) == (long)shipped.Number;
            }
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
            return field.FieldType == typeof(float) ? (float)d == (float)shipped.Number : d == shipped.Number;
        }

        // ---- Reading the delta in force ------------------------------------------------------------------------

        /// <summary>
        /// The delta in force as the JSON <c>make_delta.py</c> / <c>publish_delta.py</c> accept: <see cref="Canonical"/> with
        /// enums as member names. It reads back through <see cref="Evaluate"/> to the same canonical form, so its
        /// <c>bal=</c> hash is <see cref="Hash"/>. "{}" when nothing is applied.
        /// </summary>
        public string PublishJson => PublishJsonOf(_current);

        /// <summary><see cref="PublishJson"/> of any accepted verdict.</summary>
        public string PublishJsonOf(BalanceVerdict verdict) =>
            verdict == null || !verdict.IsAccepted || verdict.IsEmpty ? "{}" : Canonicalize(verdict, true);

        // ---- The oracle check ----------------------------------------------------------------------------------

        /// <summary>
        /// Runs the Balance Oracle on <paramref name="verdict"/> laid over the shipped assets, on copies: the live assets are not
        /// touched (see <see cref="Preview{T}"/>). It reports numbers and never blocks an apply. See <see cref="BalanceSctCheck"/>.
        /// </summary>
        public OracleCheck CheckOracle(BalanceVerdict verdict)
        {
            if (!CheckLocalAllowed("oracle check")) return new OracleCheck { Relevant = true, Note = "dev tools are off" };
            var check = BalanceSctCheck.Run(this, verdict, _abilities, _classes, _match, _oracleMap);
            if (check.Relevant && !check.Ran)
                _log.Warn(Source, $"Balance: the oracle check could not run ({check.Note}); the apply is not affected.");
            return check;
        }
    }
}
