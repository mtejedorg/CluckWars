using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CluckWars.Balance
{
    /// <summary>The kind of a parsed <see cref="BalanceJson"/> value.</summary>
    public enum JsonKind : byte { Null = 0, Bool = 1, Number = 2, String = 3, Array = 4, Object = 5 }

    /// <summary>One parsed JSON value. Objects keep their keys in document order; numbers keep their source text.</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        /// <summary>The number's token exactly as written, or the string's decoded text.</summary>
        public string Text;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Members;

        public bool TryGet(string key, out JsonValue value)
        {
            if (Members != null)
                for (int i = 0; i < Members.Count; i++)
                    if (Members[i].Key == key) { value = Members[i].Value; return true; }
            value = null;
            return false;
        }
    }

    /// <summary>
    /// A small strict JSON reader for the balance delta and for <c>JsonUtility.ToJson</c> snapshots. Strict on purpose: a
    /// duplicate key, trailing text, a comment, a NaN/Infinity literal or a bad escape is an error, so a malformed delta is
    /// refused whole instead of half-read. Written here rather than referencing Newtonsoft from Gameplay because
    /// <c>JsonUtility</c> cannot read dictionaries and Gameplay's pinned reference list (ARCH-LAY-05) stays as it is.
    /// </summary>
    public static class BalanceJson
    {
        /// <summary>Nesting deeper than this is refused (the delta needs three levels).</summary>
        public const int MaxDepth = 16;

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            value = null;
            error = null;
            if (text == null) { error = "no text"; return false; }
            var reader = new Reader(text);
            try
            {
                reader.SkipWhitespace();
                value = reader.ReadValue(0);
                reader.SkipWhitespace();
                if (!reader.AtEnd) throw reader.Fail("unexpected text after the JSON value");
                return true;
            }
            catch (FormatException e)
            {
                value = null;
                error = e.Message;
                return false;
            }
        }

        /// <summary>A JSON string literal (quotes and escapes included).</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Fail(string what) => new FormatException($"{what} at character {_i}");

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            public JsonValue ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Fail("nesting too deep");
                if (AtEnd) throw Fail("unexpected end");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return new JsonValue { Kind = JsonKind.String, Text = ReadString() };
                    case 't': Expect("true"); return new JsonValue { Kind = JsonKind.Bool, Bool = true };
                    case 'f': Expect("false"); return new JsonValue { Kind = JsonKind.Bool, Bool = false };
                    case 'n': Expect("null"); return new JsonValue { Kind = JsonKind.Null };
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Fail($"unexpected '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Fail($"expected '{word}'");
                _i += word.Length;
            }

            private JsonValue ReadObject(int depth)
            {
                _i++; // {
                var members = new List<KeyValuePair<string, JsonValue>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return new JsonValue { Kind = JsonKind.Object, Members = members }; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Fail("expected a key");
                    string key = ReadString();
                    if (!seen.Add(key)) throw Fail($"duplicate key '{key}'");
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Fail("expected ':'");
                    _i++;
                    SkipWhitespace();
                    members.Add(new KeyValuePair<string, JsonValue>(key, ReadValue(depth + 1)));
                    SkipWhitespace();
                    if (AtEnd) throw Fail("unexpected end in an object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return new JsonValue { Kind = JsonKind.Object, Members = members }; }
                    throw Fail("expected ',' or '}'");
                }
            }

            private JsonValue ReadArray(int depth)
            {
                _i++; // [
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return new JsonValue { Kind = JsonKind.Array, Items = items }; }
                while (true)
                {
                    SkipWhitespace();
                    items.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Fail("unexpected end in an array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return new JsonValue { Kind = JsonKind.Array, Items = items }; }
                    throw Fail("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Fail("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Fail("control character in a string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Fail("unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length ||
                                !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Fail("bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Fail($"bad escape '\\{e}'");
                    }
                }
            }

            private JsonValue ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                if (AtEnd || !char.IsDigit(_s[_i])) throw Fail("bad number");
                if (_s[_i] == '0') _i++;
                else while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                if (!AtEnd && _s[_i] == '.')
                {
                    _i++;
                    if (AtEnd || !char.IsDigit(_s[_i])) throw Fail("bad fraction");
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (AtEnd || !char.IsDigit(_s[_i])) throw Fail("bad exponent");
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Fail($"bad number '{token}'");
                return new JsonValue { Kind = JsonKind.Number, Number = d, Text = token };
            }
        }
    }
}
