using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>Where the balance currently in force came from.</summary>
    public enum BalanceOrigin : byte
    {
        /// <summary>The shipped assets, nothing applied.</summary>
        Baseline = 0,
        /// <summary>The last good delta, read from this device's cache at boot.</summary>
        Cache = 1,
        /// <summary>A delta fetched from the live-ops source this launch.</summary>
        Remote = 2,
        /// <summary>
        /// A delta applied on this device by the dev-tools balance editor (<see cref="BalanceBook.ApplyLocal"/>), from the
        /// editor or from its local file at boot. Dev-tools builds only (ARCH-DATA-14, owner ruling 2026-11-18).
        /// </summary>
        Local = 3,
    }

    /// <summary>What <see cref="BalanceBook.Evaluate"/> decided about one delta.</summary>
    public enum BalanceVerdictKind : byte
    {
        /// <summary>Every value passed; <see cref="BalanceBook.Apply"/> may apply it (possibly with ignored entries).</summary>
        Accepted = 0,
        /// <summary>The schema is missing or newer than this build understands: the whole delta is ignored (Warn).</summary>
        IgnoredSchema = 1,
        /// <summary>A value failed a check: nothing of it applies, the previous state stays (Error).</summary>
        Refused = 2,
    }

    /// <summary>The validated, ready-to-apply form of one delta, or the reason it was not accepted.</summary>
    public sealed class BalanceVerdict
    {
        public BalanceVerdictKind Kind;
        /// <summary>Why it was ignored or refused (null when accepted).</summary>
        public string Reason;
        /// <summary>The accepted delta as canonical JSON (sorted keys, invariant numbers, no whitespace); "{}" for none.</summary>
        public string Canonical = "{}";
        /// <summary>FNV-1a 32 of <see cref="Canonical"/>, 8 lowercase hex.</summary>
        public string Hash = BalanceBook.BaselineHash;
        public int Schema;
        /// <summary>"Asset.Field old -> new" and "Passive.Kit [..] -> [..]" lines.</summary>
        public readonly List<string> Changes = new List<string>();
        /// <summary>Entries skipped with a Warn: unknown asset or field, deny-listed or not overridable field, unknown section.</summary>
        public readonly List<string> Ignored = new List<string>();

        internal readonly List<AssetChange> Assets = new List<AssetChange>();
        internal readonly List<KitChange> Kits = new List<KitChange>();

        public bool IsAccepted => Kind == BalanceVerdictKind.Accepted;
        public bool IsEmpty => Assets.Count == 0 && Kits.Count == 0;

        internal sealed class AssetChange
        {
            public string Name;
            public ScriptableObject Asset;
            /// <summary>Field name -> canonical JSON token of the new value.</summary>
            public readonly SortedDictionary<string, string> Tokens = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        internal sealed class KitChange
        {
            public string Name;
            public PassiveAbilitySO Passive;
            public AbilityBaseSO[] Kit;
        }
    }

    /// <summary>
    /// The intermediary that holds and applies a server-published balance delta (live-ops config, D1c/D2c): the owner
    /// publishes it, clients only read it, so a balance update needs no app update. The baseline is the loaded SO assets;
    /// a delta overrides number / bool / enum fields of named assets and the fixed kits. Consumers keep reading the SO
    /// fields unchanged, so ability text follows the applied value (ARCH-DATA-12).
    /// </summary>
    /// <remarks>
    /// <para><b>Delta shape (schema 1).</b>
    /// <c>{"schema":1,"note":"free text","assets":{"Snatch":{"Cooldown":3.5},"FeatherAura":{"AreaKind":"StaticAoE"},
    /// "Speedy":{"MoveSpeed":12.5},"MatchConfig":{"FoodTargetToWin":50}},"kits":{"Bully":["Peck","Snatch","WingSlam","SpeedBurst"]}}</c>.
    /// Assets are addressed by SO asset name: every non-null entry of <see cref="AbilityRegistrySO.All"/> (abilities and
    /// passives), each class's <see cref="ChickenStatsSO"/>, and the bound <see cref="MatchConfigSO"/> under the fixed key
    /// <see cref="MatchConfigKey"/>. Two different assets with one name refuse the whole delta.</para>
    /// <para><b>All or nothing.</b> Everything is validated before anything is written (ARCH-ERR-04). A bad value
    /// (wrong type, non-finite, negative where the shipped value is not, past <see cref="MagnitudeBound"/>, outside the
    /// field's [Min]/[Range], an illegal kit) refuses the whole delta with an Error and keeps the previous state. Unknown
    /// assets and fields, deny-listed fields (<see cref="IsDenied"/>) and fields that are not numbers or bools are skipped
    /// with a Warn and the rest applies. Applying always restores the baseline first, so deltas never stack.</para>
    /// <para><b>Never mid-match.</b> The apply window is open from boot until this peer creates or joins a lobby
    /// (<c>UGSService</c>, so the lobby's <c>bal</c> hash is what it plays) or a Game scene loads (<c>GameInstaller</c>,
    /// for solo and the Ability Lab), whichever is first (<see cref="CloseApplyWindow"/>); a delta that arrives later is
    /// cached for the next launch. Nothing here branches on SessionMode (ARCH-NET-20).</para>
    /// <para><b>Peers can disagree, and only warn.</b> A peer with a stale or missing delta (offline at boot, an older
    /// cache) runs other numbers: that is the warn-only mismatch case (D3c, the <c>bal=</c> hash in the Session line, the
    /// debug HUD and the lobby check). Each peer judges RPC claims against its own copy of the registry (ARCH-NET-05), so a
    /// peer with a smaller ceiling refuses a larger claim and says so in its log; nothing is forced into agreement.</para>
    /// <para><b>Leaving the assets clean.</b> In the Editor the SOs are the real assets. Before the first change to an
    /// asset its <c>JsonUtility.ToJson</c> is snapshotted; <see cref="RestoreBaseline"/> writes the touched fields back
    /// and checks the snapshot matches. It runs on <see cref="Dispose"/> (ProjectContext teardown), on
    /// <c>Application.quitting</c>, and from the Editor guard (<c>BalanceEditorHooks</c>: domain reload, exiting Play
    /// Mode) through <see cref="Live"/>. Nothing marks an asset dirty or saves it.</para>
    /// <para><b>Derived values.</b> Nothing in Gameplay caches a value derived from these fields across scenes: the RPC
    /// ceilings (<c>ChickenController</c>'s control / knockback ceilings, <c>ChickenCargo</c>'s max single steal) are
    /// computed per chicken at spawn, after the window has closed. <c>AbilityTargeting.Mode</c>, the only value pushed
    /// into a static at install, comes from a deny-listed field.</para>
    /// </remarks>
    public sealed partial class BalanceBook : IDisposable
    {
        public const string Source = "Balance";
        /// <summary>The newest delta schema this build reads. A newer one is ignored whole ("update the app").</summary>
        public const int SupportedSchema = 1;
        /// <summary>The key the bound MatchConfig is addressed by, whatever its asset name.</summary>
        public const string MatchConfigKey = "MatchConfig";
        /// <summary>Hash of the canonical empty delta <c>{}</c>: the baseline.</summary>
        public static readonly string BaselineHash = HashOf("{}");
        /// <summary>Short label of the baseline in log lines.</summary>
        public const string BaselineLabel = "base";

        /// <summary>
        /// The book of this ProjectContext, for the Editor guard that restores the assets on a domain reload or when Play
        /// Mode ends (ARCH-DI-12: set by the constructor, cleared by <see cref="Dispose"/> and on subsystem registration).
        /// </summary>
        public static BalanceBook Live { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live = null;

        private readonly ILogService _log;
        private readonly AbilityRegistrySO _abilities;
        private readonly ChickenClassRegistrySO _classes;
        private readonly MatchConfigSO _match;
        private readonly OracleMapSO _oracleMap;

        private readonly Dictionary<ScriptableObject, Snapshot> _snapshots = new Dictionary<ScriptableObject, Snapshot>();
        private readonly Dictionary<PassiveAbilitySO, AbilityBaseSO[]> _kitSnapshots = new Dictionary<PassiveAbilitySO, AbilityBaseSO[]>();
        private BalanceVerdict _current;
        private bool _disposed;

        /// <param name="oracleMap">The baked arena the oracle check reads (serialized on the installer); null leaves the check unable to run, never blocking.</param>
        public BalanceBook(ILogService log, AbilityRegistrySO abilities, ChickenClassRegistrySO classes, MatchConfigSO match, OracleMapSO oracleMap = null)
        {
            _log = log;
            _abilities = abilities;
            _classes = classes;
            _match = match;
            _oracleMap = oracleMap;
            Live = this;
            Application.quitting += OnQuitting;
        }

        // ---- State -----------------------------------------------------------------------------------------------

        /// <summary>8-hex hash of the applied delta; <see cref="BaselineHash"/> when nothing is applied.</summary>
        public string Hash => _current?.Hash ?? BaselineHash;
        /// <summary><see cref="BaselineLabel"/> when nothing is applied, else <see cref="Hash"/>.</summary>
        public string Label => _current == null ? BaselineLabel : Hash;
        public BalanceOrigin Origin { get; private set; } = BalanceOrigin.Baseline;
        /// <summary>Schema of the applied delta, 0 when nothing is applied.</summary>
        public int Schema => _current?.Schema ?? 0;
        /// <summary>The applied delta as canonical JSON ("{}" for none).</summary>
        public string Canonical => _current?.Canonical ?? "{}";
        public IReadOnlyList<string> Applied => _current != null ? _current.Changes : (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<string> Ignored => _current != null ? _current.Ignored : (IReadOnlyList<string>)Array.Empty<string>();
        /// <summary>Why the last refused or ignored delta did not apply (null when none was).</summary>
        public string LastRefusal { get; private set; }
        /// <summary>True from boot until the first lobby is created or joined, or a match scene loads.</summary>
        public bool ApplyWindowOpen { get; private set; } = true;

        // ---- Evaluate ------------------------------------------------------------------------------------------

        /// <summary>
        /// Validates <paramref name="json"/> against the baseline without changing anything. Null or blank is the empty
        /// delta (accepted, baseline). Logs one Warn per ignored entry, and an Error or Warn when it is refused or ignored.
        /// </summary>
        public BalanceVerdict Evaluate(string json, string from)
        {
            var v = new BalanceVerdict();
            if (string.IsNullOrWhiteSpace(json)) return v;

            try
            {
                EvaluateInto(v, json);
            }
            catch (Exception e)
            {
                v.Kind = BalanceVerdictKind.Refused;
                v.Reason = $"unexpected {e.GetType().Name}: {e.Message}";
            }

            if (v.Kind == BalanceVerdictKind.Accepted)
            {
                foreach (var line in v.Ignored) _log.Warn(Source, $"Balance delta ({from}): ignored {line}");
                if (v.IsEmpty) { v.Canonical = "{}"; v.Hash = BaselineHash; }
                else { v.Canonical = Canonicalize(v); v.Hash = HashOf(v.Canonical); }
            }
            else
            {
                LastRefusal = v.Reason;
                if (v.Kind == BalanceVerdictKind.IgnoredSchema)
                    _log.Warn(Source, $"Balance delta ({from}) ignored: {v.Reason}. Keeping {Label} ({Origin}).");
                else
                    _log.Error(Source, $"Balance delta ({from}) REFUSED, nothing applied: {v.Reason}. Keeping {Label} ({Origin}).");
            }
            return v;
        }

        private void EvaluateInto(BalanceVerdict v, string json)
        {
            if (!BalanceJson.TryParse(json, out var root, out var parseError)) { Refuse(v, $"not valid JSON ({parseError})"); return; }
            if (root.Kind != JsonKind.Object) { Refuse(v, "the delta is not a JSON object"); return; }

            if (!root.TryGet("schema", out var schema))
            {
                v.Kind = BalanceVerdictKind.IgnoredSchema;
                v.Reason = "no \"schema\" field";
                return;
            }
            if (schema.Kind != JsonKind.Number || !IsIntegral(schema.Number) || schema.Number < 1)
            {
                Refuse(v, $"\"schema\" must be a whole number >= 1, got {Describe(schema)}");
                return;
            }
            if (schema.Number > SupportedSchema)
            {
                v.Kind = BalanceVerdictKind.IgnoredSchema;
                v.Reason = $"schema {schema.Text} is newer than this build reads ({SupportedSchema}); update the app";
                return;
            }
            v.Schema = (int)schema.Number;

            if (!TryBuildIndex(out var index, out var dupError)) { Refuse(v, dupError); return; }

            foreach (var member in root.Members)
            {
                switch (member.Key)
                {
                    case "schema": break;
                    case "note":
                        if (member.Value.Kind != JsonKind.String) v.Ignored.Add("\"note\" (not a string)");
                        break;
                    case "assets":
                        if (member.Value.Kind != JsonKind.Object) { Refuse(v, "\"assets\" must be an object"); return; }
                        foreach (var asset in member.Value.Members)
                            if (!EvaluateAsset(v, index, asset.Key, asset.Value)) return;
                        break;
                    case "kits":
                        if (member.Value.Kind != JsonKind.Object) { Refuse(v, "\"kits\" must be an object"); return; }
                        foreach (var kit in member.Value.Members)
                            if (!EvaluateKit(v, index, kit.Key, kit.Value)) return;
                        break;
                    default:
                        v.Ignored.Add($"unknown section \"{member.Key}\"");
                        break;
                }
            }
        }

        private bool EvaluateAsset(BalanceVerdict v, Dictionary<string, ScriptableObject> index, string name, JsonValue fields)
        {
            if (!index.TryGetValue(name, out var so)) { v.Ignored.Add($"unknown asset \"{name}\""); return true; }
            if (fields.Kind != JsonKind.Object) return Refuse(v, $"assets.{name} must be an object of field: value");

            var baseline = BaselineOf(so);
            var change = new BalanceVerdict.AssetChange { Name = name, Asset = so };
            foreach (var f in fields.Members)
            {
                string where = $"{name}.{f.Key}";
                if (!baseline.TryGet(f.Key, out var baseValue)) { v.Ignored.Add($"unknown field {where}"); continue; }
                if (baseValue.Kind != JsonKind.Number && baseValue.Kind != JsonKind.Bool)
                {
                    v.Ignored.Add($"{where} (a {baseValue.Kind.ToString().ToLowerInvariant()} field: only numbers, bools and enums are overridable)");
                    continue;
                }
                var field = FindField(so.GetType(), f.Key);
                if (field == null) { v.Ignored.Add($"unknown field {where}"); continue; }
                if (IsDenied(field)) { v.Ignored.Add($"{where} (deny-listed: changes loadout legality, networking or a value pushed at install)"); continue; }

                if (!TryValidateValue(field, f.Value, baseValue, out string token, out string problem))
                    return Refuse(v, $"{where}: {problem}");
                change.Tokens[f.Key] = token;
                v.Changes.Add($"{where} {BaselineText(baseValue)} -> {token}");
            }
            if (change.Tokens.Count > 0) v.Assets.Add(change);
            return true;
        }

        private bool EvaluateKit(BalanceVerdict v, Dictionary<string, ScriptableObject> index, string name, JsonValue entries)
        {
            if (!index.TryGetValue(name, out var so)) { v.Ignored.Add($"unknown kit owner \"{name}\""); return true; }
            if (!(so is PassiveAbilitySO passive)) return Refuse(v, $"kits.{name}: \"{name}\" is not a specialization (passive)");
            if (entries.Kind != JsonKind.Array) return Refuse(v, $"kits.{name} must be an array of ability names");

            var baseKit = _kitSnapshots.TryGetValue(passive, out var snap) ? snap : passive.Kit;
            int expected = baseKit?.Length ?? 0;
            if (entries.Items.Count != expected)
                return Refuse(v, $"kits.{name} has {entries.Items.Count} abilities, the shipped kit has {expected}");

            var kit = new AbilityBaseSO[entries.Items.Count];
            for (int i = 0; i < kit.Length; i++)
            {
                var e = entries.Items[i];
                if (e.Kind != JsonKind.String) return Refuse(v, $"kits.{name}[{i}] must be an ability name, got {Describe(e)}");
                if (!index.TryGetValue(e.Text, out var entry) || !(entry is AbilityBaseSO ability) || ability is PassiveAbilitySO)
                    return Refuse(v, $"kits.{name}[{i}] \"{e.Text}\" is not a registered active ability");
                kit[i] = ability;
            }

            // The same rules the spawner and the proxies apply to a kit (FixedKit, ARCH-NET-11), asked before applying.
            var problems = FixedKit.Problems(passive, kit);
            if (problems.Count > 0) return Refuse(v, $"kits.{name} is not a legal kit: {string.Join(" ", problems)}");

            v.Kits.Add(new BalanceVerdict.KitChange { Name = name, Passive = passive, Kit = kit });
            v.Changes.Add($"{name}.Kit [{KitNames(baseKit)}] -> [{KitNames(kit)}]");
            return true;
        }

        private static bool Refuse(BalanceVerdict v, string reason)
        {
            v.Kind = BalanceVerdictKind.Refused;
            v.Reason = reason;
            return false;
        }

        // ---- Value rules -----------------------------------------------------------------------------------------

        /// <summary>
        /// The largest magnitude a delta may set: max(1000, 100 x |shipped value|). 1000 clears every shipped number
        /// (the largest is TurnSpeed 720) with room, 100x allows a deliberate tenfold swing either way while still catching
        /// a slipped decimal or an extra digit group (3500 typed for 3.5 is 1000x).
        /// </summary>
        public static double MagnitudeBound(double baseline) => Math.Max(1000d, 100d * Math.Abs(baseline));

        /// <summary>
        /// Fields a delta may not touch, by declaring type. They are not balance numbers: they change loadout legality
        /// (which every peer judges, ARCH-NET-11), classification and presentation keys, room capacity (networking,
        /// ARCH-NET-14), or a value pushed into a static at install (<c>TargetingMode</c> into <c>AbilityTargeting.Mode</c>).
        /// The tick rate lives in <c>NetworkProjectConfig.fusion</c>, not in any SO, so no SO field carries it.
        /// </summary>
        public static readonly IReadOnlyDictionary<Type, string[]> DeniedFields = new Dictionary<Type, string[]>
        {
            [typeof(AbilityBaseSO)] = new[]
            {
                nameof(AbilityBaseSO.AllowedClasses), nameof(AbilityBaseSO.PeckSlotPreEquippedBy),
                nameof(AbilityBaseSO.SignaturePreEquippedBy), nameof(AbilityBaseSO.Category), nameof(AbilityBaseSO.BotRole),
                nameof(AbilityBaseSO.AccentKey), nameof(AbilityBaseSO.TerrainTraversal), nameof(AbilityBaseSO.JumpTier),
                nameof(AbilityBaseSO.CastMotion), nameof(AbilityBaseSO.Retired),
            },
            [typeof(PassiveAbilitySO)] = new[] { nameof(PassiveAbilitySO.Subclass) },
            [typeof(MatchConfigSO)] = new[]
            {
                nameof(MatchConfigSO.MaxPlayers), nameof(MatchConfigSO.TargetingMode), nameof(MatchConfigSO.LoadoutMode),
            },
        };

        public static bool IsDenied(FieldInfo field) =>
            DeniedFields.TryGetValue(field.DeclaringType, out var names) && Array.IndexOf(names, field.Name) >= 0;

        private static bool TryValidateValue(FieldInfo field, JsonValue v, JsonValue baseValue, out string token, out string problem)
        {
            token = null;
            problem = null;
            var t = field.FieldType;

            if (t == typeof(bool))
            {
                if (v.Kind != JsonKind.Bool) { problem = $"expects true or false, got {Describe(v)}"; return false; }
                token = v.Bool ? "true" : "false";
                return true;
            }

            if (t.IsEnum) return TryValidateEnum(t, v, out token, out problem);

            if (v.Kind != JsonKind.Number) { problem = $"expects a number, got {Describe(v)}"; return false; }
            double d = v.Number;
            if (double.IsNaN(d) || double.IsInfinity(d)) { problem = $"{v.Text} is not a finite number"; return false; }

            if (t == typeof(float))
            {
                float f = (float)d;
                if (float.IsInfinity(f)) { problem = $"{v.Text} does not fit a float"; return false; }
                token = f.ToString("R", CultureInfo.InvariantCulture);
                d = f;
            }
            else if (t == typeof(double))
            {
                token = d.ToString("R", CultureInfo.InvariantCulture);
            }
            else if (TryIntegerRange(t, out long min, out long max))
            {
                if (!IsIntegral(d)) { problem = $"expects a whole number, got {v.Text}"; return false; }
                if (d < min || d > max) { problem = $"{v.Text} is outside a {t.Name}"; return false; }
                token = ((long)d).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                problem = $"field type {t.Name} is not overridable";
                return false;
            }

            double b = baseValue.Number;
            if (b >= 0d && d < 0d) { problem = $"{token} is negative and the shipped value ({BaselineText(baseValue)}) is not"; return false; }
            double bound = MagnitudeBound(b);
            if (Math.Abs(d) > bound) { problem = $"{token} is past the sanity bound {bound.ToString("R", CultureInfo.InvariantCulture)} (max(1000, 100 x shipped {BaselineText(baseValue)}))"; return false; }

            var range = field.GetCustomAttribute<RangeAttribute>();
            if (range != null && (d < range.min || d > range.max)) { problem = $"{token} is outside the field's [Range({range.min}, {range.max})]"; return false; }
            var minAttr = field.GetCustomAttribute<MinAttribute>();
            if (minAttr != null && d < minAttr.min) { problem = $"{token} is below the field's [Min({minAttr.min})]"; return false; }
            return true;
        }

        private static bool TryValidateEnum(Type t, JsonValue v, out string token, out string problem)
        {
            token = null;
            problem = null;
            object value;
            if (v.Kind == JsonKind.String)
            {
                // A member name, exactly as declared; a numeric string is not a name.
                if (v.Text.Length == 0 || !char.IsLetter(v.Text[0]) || !Enum.IsDefined(t, v.Text))
                {
                    problem = $"\"{v.Text}\" is not a {t.Name} member";
                    return false;
                }
                value = Enum.Parse(t, v.Text);
            }
            else if (v.Kind == JsonKind.Number && IsIntegral(v.Number))
            {
                var underlying = Enum.GetUnderlyingType(t);
                if (!TryIntegerRange(underlying, out long min, out long max) || v.Number < min || v.Number > max)
                {
                    problem = $"{v.Text} is outside {t.Name}";
                    return false;
                }
                value = Enum.ToObject(t, (long)v.Number);
                bool flags = t.GetCustomAttribute<FlagsAttribute>() != null;
                if (!flags && !Enum.IsDefined(t, value)) { problem = $"{v.Text} is not a {t.Name} member"; return false; }
                if (flags && (Convert.ToInt64(value) & ~AllFlagBits(t)) != 0) { problem = $"{v.Text} sets bits {t.Name} does not define"; return false; }
            }
            else
            {
                problem = $"expects a {t.Name} member (name or number), got {Describe(v)}";
                return false;
            }
            token = Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static long AllFlagBits(Type t)
        {
            long all = 0;
            foreach (var x in Enum.GetValues(t)) all |= Convert.ToInt64(x);
            return all;
        }

        private static bool TryIntegerRange(Type t, out long min, out long max)
        {
            if (t == typeof(int)) { min = int.MinValue; max = int.MaxValue; return true; }
            if (t == typeof(uint)) { min = 0; max = uint.MaxValue; return true; }
            if (t == typeof(short)) { min = short.MinValue; max = short.MaxValue; return true; }
            if (t == typeof(ushort)) { min = 0; max = ushort.MaxValue; return true; }
            if (t == typeof(byte)) { min = 0; max = byte.MaxValue; return true; }
            if (t == typeof(sbyte)) { min = sbyte.MinValue; max = sbyte.MaxValue; return true; }
            if (t == typeof(long)) { min = long.MinValue; max = long.MaxValue; return true; }
            min = max = 0;
            return false;
        }

        private static bool IsIntegral(double d) => !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d;

        // ---- Apply / restore -------------------------------------------------------------------------------------

        /// <summary>Evaluates and applies <paramref name="json"/> in one go (the boot cache path and tests).</summary>
        public bool TryApply(string json, BalanceOrigin origin) => Apply(Evaluate(json, origin.ToString()), origin);

        /// <summary>
        /// Applies an accepted verdict: restores the baseline, then writes every change. False (and nothing changed)
        /// for a verdict that was not accepted, after <see cref="Dispose"/>, or once the apply window has closed.
        /// </summary>
        public bool Apply(BalanceVerdict verdict, BalanceOrigin origin)
        {
            if (verdict == null || !verdict.IsAccepted) return false;
            // The single enforcement point of the local path: whatever calls Apply, a Local origin needs dev tools.
            if (origin == BalanceOrigin.Local && !CheckLocalAllowed("local apply")) return false;
            if (_disposed) { _log.Warn(Source, "Balance apply after dispose ignored."); return false; }
            if (!ApplyWindowOpen)
            {
                _log.Warn(Source, $"Balance {verdict.Hash} not applied: a match has started this launch (applies at next launch).");
                return false;
            }

            RestoreBaseline();
            if (verdict.IsEmpty)
            {
                _log.Info(Source, $"Balance: no delta ({origin}); running the shipped assets, bal={BaselineHash}.");
                return true;
            }

            try
            {
                foreach (var change in verdict.Assets) ApplyAsset(change);
                foreach (var kit in verdict.Kits)
                {
                    if (!_kitSnapshots.ContainsKey(kit.Passive))
                        _kitSnapshots[kit.Passive] = kit.Passive.Kit != null ? (AbilityBaseSO[])kit.Passive.Kit.Clone() : null;
                    kit.Passive.Kit = (AbilityBaseSO[])kit.Kit.Clone();
                }
            }
            catch (Exception e)
            {
                _log.Error(Source, $"Balance {verdict.Hash} failed while applying; restoring the shipped assets.", e);
                RestoreBaseline();
                return false;
            }

            _current = verdict;
            Origin = origin;
            LastRefusal = null;
            _log.Info(Source, $"Balance applied ({origin}) bal={verdict.Hash} schema={verdict.Schema}: {verdict.Changes.Count} change(s): " +
                              string.Join("; ", verdict.Changes));
            return true;
        }

        private void ApplyAsset(BalanceVerdict.AssetChange change)
        {
            var so = change.Asset;
            if (!_snapshots.TryGetValue(so, out var snap))
            {
                snap = new Snapshot(JsonUtility.ToJson(so));
                _snapshots[so] = snap;
            }
            foreach (var key in change.Tokens.Keys) snap.Touched.Add(key);

            JsonUtility.FromJsonOverwrite(PartialJson(change.Tokens), so);

            // Read back: a write JsonUtility silently skipped would otherwise report as applied.
            BalanceJson.TryParse(JsonUtility.ToJson(so), out var now, out _);
            foreach (var kv in change.Tokens)
            {
                JsonValue got = null;
                if (now == null || !now.TryGet(kv.Key, out got) || !SameValue(got, kv.Value))
                    _log.Error(Source, $"Balance: {change.Name}.{kv.Key} did not take the value {kv.Value} (reads {(got != null ? Describe(got) : "nothing")}).");
            }
        }

        /// <summary>
        /// Writes every touched field and kit back to its shipped value and checks each asset matches its snapshot.
        /// Idempotent; safe with nothing applied.
        /// </summary>
        public void RestoreBaseline()
        {
            foreach (var kv in _snapshots)
            {
                var so = kv.Key;
                if (so == null) continue; // destroyed with the domain; nothing left to restore
                var snap = kv.Value;
                try
                {
                    BalanceJson.TryParse(snap.Json, out var shipped, out _);
                    var tokens = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var key in snap.Touched)
                        if (shipped != null && shipped.TryGet(key, out var value)) tokens[key] = TokenOf(value);
                    JsonUtility.FromJsonOverwrite(PartialJson(tokens), so);
                    if (JsonUtility.ToJson(so) != snap.Json)
                        _log.Error(Source, $"Balance restore: '{so.name}' does not match its shipped snapshot after restoring.");
                }
                catch (Exception e)
                {
                    _log.Error(Source, $"Balance restore of '{so.name}' failed.", e);
                }
            }
            _snapshots.Clear();

            foreach (var kv in _kitSnapshots)
                if (kv.Key != null) kv.Key.Kit = kv.Value;
            _kitSnapshots.Clear();

            if (_current != null) _log.Info(Source, $"Balance: shipped assets restored (was bal={_current.Hash}).");
            _current = null;
            Origin = BalanceOrigin.Baseline;
        }

        /// <summary>Closes the apply window for this launch (first lobby create / join, or a Game scene load). Idempotent.</summary>
        public void CloseApplyWindow(string reason)
        {
            if (!ApplyWindowOpen) return;
            ApplyWindowOpen = false;
            _log.Info(Source, $"Balance apply window closed ({reason}); this launch plays bal={Hash} ({Origin}).");
        }

        public void Dispose()
        {
            if (_disposed) return;
            RestoreBaseline();
            _disposed = true;
            Application.quitting -= OnQuitting;
            if (Live == this) Live = null;
        }

        private void OnQuitting() => RestoreBaseline();

        // ---- Helpers -------------------------------------------------------------------------------------------

        private sealed class Snapshot
        {
            public readonly string Json;
            public readonly HashSet<string> Touched = new HashSet<string>(StringComparer.Ordinal);
            public Snapshot(string json) { Json = json; }
        }

        /// <summary>Every addressable asset by name; false (with the reason) when two different assets share a name.</summary>
        private bool TryBuildIndex(out Dictionary<string, ScriptableObject> index, out string error)
        {
            index = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
            error = null;
            if (_abilities != null && _abilities.All != null)
                foreach (var a in _abilities.All)
                    if (a != null && !Add(index, a.name, a, ref error)) return false;
            if (_classes != null)
                foreach (ChickenClass cls in Enum.GetValues(typeof(ChickenClass)))
                    if (_classes.TryGet(cls, out var entry) && entry.Stats != null && !Add(index, entry.Stats.name, entry.Stats, ref error))
                        return false;
            if (_match != null && !Add(index, MatchConfigKey, _match, ref error)) return false;
            return true;
        }

        private static bool Add(Dictionary<string, ScriptableObject> index, string name, ScriptableObject so, ref string error)
        {
            if (index.TryGetValue(name, out var existing))
            {
                if (existing == so) return true;
                error = $"two assets are named \"{name}\" ({existing.GetType().Name} and {so.GetType().Name}); names must be unique";
                return false;
            }
            index[name] = so;
            return true;
        }

        /// <summary>The asset's shipped values: its snapshot if this book has changed it, else its current ToJson.</summary>
        private JsonValue BaselineOf(ScriptableObject so)
        {
            string json = _snapshots.TryGetValue(so, out var snap) ? snap.Json : JsonUtility.ToJson(so);
            return BalanceJson.TryParse(json, out var value, out _) ? value : new JsonValue { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonValue>>() };
        }

        internal static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(ScriptableObject); t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        private static string PartialJson(SortedDictionary<string, string> tokens)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in tokens)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(BalanceJson.Quote(kv.Key)).Append(':').Append(kv.Value);
            }
            return sb.Append('}').ToString();
        }

        private static string TokenOf(JsonValue v) => v.Kind == JsonKind.Bool ? (v.Bool ? "true" : "false") : v.Text;

        private static bool SameValue(JsonValue got, string token)
        {
            if (got.Kind == JsonKind.Bool) return (got.Bool ? "true" : "false") == token;
            if (got.Kind != JsonKind.Number) return false;
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double want)
                   && (float)got.Number == (float)want;
        }

        private static string BaselineText(JsonValue v) =>
            v.Kind == JsonKind.Bool ? (v.Bool ? "true" : "false")
            : v.Kind == JsonKind.Number ? ((float)v.Number).ToString("R", CultureInfo.InvariantCulture)
            : v.Kind.ToString();

        private static string Describe(JsonValue v) => v.Kind switch
        {
            JsonKind.String => $"\"{v.Text}\"",
            JsonKind.Number => v.Text,
            JsonKind.Bool => v.Bool ? "true" : "false",
            _ => v.Kind.ToString().ToLowerInvariant(),
        };

        private static string KitNames(AbilityBaseSO[] kit)
        {
            if (kit == null) return "";
            var names = new string[kit.Length];
            for (int i = 0; i < kit.Length; i++) names[i] = kit[i] != null ? kit[i].name : "-";
            return string.Join(",", names);
        }

        /// <summary>
        /// Canonical JSON of an accepted verdict: sorted keys, invariant numbers, no whitespace, note left out. With
        /// <paramref name="publish"/> an enum field is written as its member name instead of its number: the form
        /// <c>make_delta.py</c> accepts (it refuses a numeric enum). Both read back to the same canonical form and hash.
        /// </summary>
        private static string Canonicalize(BalanceVerdict v, bool publish = false)
        {
            var sb = new StringBuilder("{");
            bool any = false;
            if (v.Assets.Count > 0)
            {
                var assets = new List<BalanceVerdict.AssetChange>(v.Assets);
                assets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                sb.Append("\"assets\":{");
                for (int i = 0; i < assets.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(BalanceJson.Quote(assets[i].Name)).Append(':')
                      .Append(publish ? PublishTokens(assets[i]) : PartialJson(assets[i].Tokens));
                }
                sb.Append('}');
                any = true;
            }
            if (v.Kits.Count > 0)
            {
                var kits = new List<BalanceVerdict.KitChange>(v.Kits);
                kits.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                if (any) sb.Append(',');
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
                    sb.Append(']');
                }
                sb.Append('}');
                any = true;
            }
            if (any) sb.Append(',');
            sb.Append("\"schema\":").Append(v.Schema.ToString(CultureInfo.InvariantCulture));
            return sb.Append('}').ToString();
        }

        private static string PublishTokens(BalanceVerdict.AssetChange change)
        {
            var tokens = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in change.Tokens) tokens[kv.Key] = PublishToken(change.Asset, kv.Key, kv.Value);
            return PartialJson(tokens);
        }

        /// <summary>An enum field's numeric token as its quoted member name (when the number is a defined member); other tokens unchanged.</summary>
        private static string PublishToken(ScriptableObject asset, string field, string token)
        {
            var f = FindField(asset.GetType(), field);
            if (f != null && f.FieldType.IsEnum
                && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
            {
                object value = Enum.ToObject(f.FieldType, n);
                if (Enum.IsDefined(f.FieldType, value)) return BalanceJson.Quote(Enum.GetName(f.FieldType, value));
            }
            return token;
        }

        /// <summary>
        /// The shipped value of one overridable number field and the hard limits a delta must respect for it (its
        /// <c>[Range]</c> / <c>[Min]</c>; infinities where it has none). False, with the reason, for an unknown asset or
        /// field, a deny-listed field or a field that is not a number. Read-only (the oracle solver reads its starting
        /// point and limits here, and the delta it proposes still goes through <see cref="Evaluate"/>).
        /// </summary>
        public bool TryProbe(string asset, string field, out float shipped, out float min, out float max, out string problem)
        {
            shipped = 0f;
            min = float.NegativeInfinity;
            max = float.PositiveInfinity;
            if (!TryBuildIndex(out var index, out problem)) return false;
            if (!index.TryGetValue(asset ?? string.Empty, out var so)) { problem = $"unknown asset \"{asset}\""; return false; }
            var info = FindField(so.GetType(), field ?? string.Empty);
            if (info == null) { problem = $"unknown field {asset}.{field}"; return false; }
            if (IsDenied(info)) { problem = $"{asset}.{field} is deny-listed: a delta may not change it"; return false; }
            if (!BaselineOf(so).TryGet(field, out var value) || value.Kind != JsonKind.Number)
            {
                problem = $"{asset}.{field} is not a number field";
                return false;
            }
            shipped = (float)value.Number;
            var range = info.GetCustomAttribute<RangeAttribute>();
            if (range != null) { min = range.min; max = range.max; }
            var minAttr = info.GetCustomAttribute<MinAttribute>();
            if (minAttr != null && minAttr.min > min) min = minAttr.min;
            problem = null;
            return true;
        }

        /// <summary>
        /// A throwaway copy of <paramref name="original"/> as it would read with <paramref name="verdict"/> applied over the
        /// SHIPPED assets (not over what is applied now): the oracle check reads proposed values from it without touching
        /// the live asset or this book's snapshots. The caller destroys the copy.
        /// </summary>
        public T Preview<T>(T original, BalanceVerdict verdict) where T : ScriptableObject
        {
            var copy = UnityEngine.Object.Instantiate(original);
            try
            {
                if (_snapshots.TryGetValue(original, out var snap)) JsonUtility.FromJsonOverwrite(snap.Json, copy);
                if (verdict != null)
                    foreach (var change in verdict.Assets)
                        if (change.Asset == original) JsonUtility.FromJsonOverwrite(PartialJson(change.Tokens), copy);
                return copy;
            }
            catch
            {
                // The caller never receives the copy, so it cannot destroy it: do it here, then let the error surface.
                if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
                else UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
        }

        /// <summary>FNV-1a 32-bit over the UTF-8 bytes of <paramref name="canonical"/>, as 8 lowercase hex digits.</summary>
        public static string HashOf(string canonical)
        {
            uint h = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(canonical ?? ""))
            {
                h ^= b;
                h *= 16777619u;
            }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
