using System;
using System.Collections.Generic;
using System.Text;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.Localization
{
    /// <summary>
    /// Declares the named <c>{placeholders}</c> a <see cref="UiKeys"/> template expects.
    /// The contract between the CSV row and every <c>UiText.Format</c> call site; <c>UiTextTests</c> keeps the attribute, the template and the call sites in agreement.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UiArgsAttribute : Attribute
    {
        public readonly string[] Names;
        public UiArgsAttribute(params string[] names) => Names = names;
    }

    /// <summary>
    /// The wording dictionary: every player-facing string, keyed by id, loaded from
    /// <c>Resources/Text/UiText.csv</c> (<c>key,en</c>; RFC-4180 quoting; UTF-8). One column
    /// per language when a second one is added.
    /// </summary>
    /// <remarks>
    /// <para><b>Static, like <c>PlayerPreferences</c>.</b> It is read-only data every screen
    /// needs, loaded lazily on first use, and nobody injects or mocks it. The reset hook below
    /// covers Enter-Play-Mode-without-domain-reload.</para>
    ///
    /// <para><b>Code:</b> <c>UiText.Get(UiKeys.BtnReady)</c>; templates take NAMED placeholders:
    /// <c>UiText.Format(UiKeys.BtnPickMoreMany, ("n", missing))</c>. A key is never a literal in
    /// UI code - name a <see cref="UiKeys"/> constant.</para>
    ///
    /// <para><b>UXML:</b> set an element's text to <c>@key</c> (e.g. <c>text="@screen.class.title"</c>
    /// on a Label/Button/Toggle). The controller calls <see cref="ResolveTree"/> once after cloning
    /// a page and every such element gets its text from the dictionary. Anything without the
    /// leading <c>@</c> is left alone, and a test fails on a non-empty literal in the menu UXML.
    /// Elements whose text is filled by code at runtime keep <c>text=""</c>.</para>
    ///
    /// <para><b>Failures are visible, not silent.</b> A missing key renders as <c>#key#</c>; a
    /// placeholder with no argument renders as <c>#name#</c>. Each is logged once through the
    /// logger registered with <see cref="SetLogger"/> (a static class cannot be injected;
    /// <c>MenuUiController</c> hands over its own). Until a logger is registered the reports are
    /// held in <see cref="Problems"/> so a test (or a late logger) still sees them.</para>
    /// </remarks>
    public static class UiText
    {
        private const string Source = "UiText";
        public const string ResourcePath = "Text/UiText";
        private const string UxmlPrefix = "@";

        private static Dictionary<string, string> _table;
        private static ILogService _log;
        private static readonly HashSet<string> Reported = new();
        private static readonly List<string> ProblemList = new();

        /// <summary>Every missing-key / missing-argument report so far (each distinct one once).</summary>
        public static IReadOnlyList<string> Problems => ProblemList;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            _table = null;
            _log = null;
            Reported.Clear();
            ProblemList.Clear();
        }

        /// <summary>Registers the logger problems are reported through, flushing any held ones.</summary>
        public static void SetLogger(ILogService log)
        {
            bool hadLogger = _log != null;
            _log = log;
            if (_log == null || hadLogger) return;
            foreach (var p in ProblemList) _log.Error(Source, p);
        }

        /// <summary>The raw template for <paramref name="key"/>, or <c>#key#</c> if it is missing.</summary>
        public static string Get(string key)
        {
            if (TryGet(key, out var value)) return value;
            Report($"missing key:{key}", $"Missing UI text key '{key}'. Add it to Resources/{ResourcePath}.csv.");
            return $"#{key}#";
        }

        public static bool TryGet(string key, out string value)
        {
            value = null;
            return !string.IsNullOrEmpty(key) && Table().TryGetValue(key, out value);
        }

        public static bool HasKey(string key) => !string.IsNullOrEmpty(key) && Table().ContainsKey(key);

        /// <summary>All keys in the loaded table (for tests and tooling).</summary>
        public static IEnumerable<string> Keys => Table().Keys;

        /// <summary>Fills the template's named <c>{placeholders}</c> from <paramref name="args"/>.</summary>
        public static string Format(string key, params (string name, object value)[] args)
        {
            var dict = new Dictionary<string, object>(args?.Length ?? 0);
            if (args != null) foreach (var (n, v) in args) dict[n] = v;
            return Format(key, dict);
        }

        public static string Format(string key, IReadOnlyDictionary<string, object> args)
        {
            string template = Get(key);
            if (template.Length == 0 || template.IndexOf('{') < 0) return template;
            return Fill(key, template, args);
        }

        /// <summary>The distinct <c>{placeholder}</c> names in <paramref name="template"/>, in order.</summary>
        public static IReadOnlyList<string> Placeholders(string template)
        {
            var names = new List<string>();
            ScanPlaceholders(template, (name, _, _) => { if (!names.Contains(name)) names.Add(name); });
            return names;
        }

        /// <summary>
        /// Replaces every <c>@key</c> text under <paramref name="root"/> (Label, Button, Toggle,
        /// any <see cref="TextElement"/>) with its dictionary text. See the class remarks.
        /// </summary>
        public static void ResolveTree(VisualElement root)
        {
            if (root == null) return;
            root.Query<VisualElement>().ForEach(ve =>
            {
                switch (ve)
                {
                    case Toggle t: t.text = Resolve(t.text); break;
                    case TextElement te: te.text = Resolve(te.text); break;
                }
            });
        }

        /// <summary>True when <paramref name="authoredText"/> is a UXML key reference.</summary>
        public static bool IsKeyReference(string authoredText) =>
            authoredText != null && authoredText.StartsWith(UxmlPrefix, StringComparison.Ordinal) && authoredText.Length > 1;

        /// <summary>The key inside a UXML <c>@key</c> reference.</summary>
        public static string KeyOfReference(string authoredText) => authoredText.Substring(UxmlPrefix.Length);

        private static string Resolve(string text) =>
            IsKeyReference(text) ? Get(KeyOfReference(text)) : text;

        // ---- Template filling ---------------------------------------------------------

        private static string Fill(string key, string template, IReadOnlyDictionary<string, object> args)
        {
            var sb = new StringBuilder(template.Length + 16);
            int last = 0;
            ScanPlaceholders(template, (name, start, end) =>
            {
                sb.Append(template, last, start - last);
                if (args != null && args.TryGetValue(name, out var v))
                {
                    sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    Report($"missing arg:{key}:{name}",
                        $"UI text '{key}' needs a value for {{{name}}} and the caller did not supply one.");
                    sb.Append('#').Append(name).Append('#');
                }
                last = end;
            });
            sb.Append(template, last, template.Length - last);
            return sb.ToString();
        }

        private static void ScanPlaceholders(string template, Action<string, int, int> onFound)
        {
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0) break;
                int close = template.IndexOf('}', open + 1);
                if (close < 0) break;
                string name = template.Substring(open + 1, close - open - 1);
                if (IsIdentifier(name)) { onFound(name, open, close + 1); i = close + 1; }
                else i = open + 1;
            }
        }

        private static bool IsIdentifier(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private static void Report(string dedupeKey, string message)
        {
            if (!Reported.Add(dedupeKey)) return;
            ProblemList.Add(message);
            _log?.Error(Source, message);
        }

        // ---- Loading --------------------------------------------------------------------

        private static Dictionary<string, string> Table()
        {
            if (_table != null) return _table;

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                // Not cached: a later call retries. Reported once so the log is not flooded.
                Report("missing csv", $"UI text table not found at Resources/{ResourcePath}.csv. Every string will render as #key#.");
                return new Dictionary<string, string>();
            }

            try
            {
                _table = ParseCsv(asset.text);
            }
            catch (FormatException e)
            {
                Report("bad csv", $"UI text table is malformed: {e.Message}");
                return new Dictionary<string, string>();
            }
            return _table;
        }

        /// <summary>
        /// Parses <c>key,en</c> CSV (RFC 4180: quoted fields, doubled quotes, commas and
        /// newlines inside quotes). The header row is skipped; the second column is the
        /// English text. Throws <see cref="FormatException"/> on an unterminated quote or a
        /// duplicate key.
        /// </summary>
        public static Dictionary<string, string> ParseCsv(string text)
        {
            var table = new Dictionary<string, string>();
            var rows = ReadRecords(text);
            for (int r = 1; r < rows.Count; r++) // row 0 is the header
            {
                var row = rows[r];
                if (row.Count == 1 && row[0].Length == 0) continue; // blank line
                if (row.Count < 2) throw new FormatException($"row {r + 1} has no value column.");
                if (!table.TryAdd(row[0], row[1])) throw new FormatException($"duplicate key '{row[0]}'.");
            }
            return table;
        }

        private static List<List<string>> ReadRecords(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1; // tolerate a BOM

            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default: field.Append(c); break;
                }
            }

            if (inQuotes) throw new FormatException("unterminated quoted field.");
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
