using System;
using System.Globalization;
using System.Text;
using CluckWars.Balance;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Settings;
using UnityEngine;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>The values of one <c>Session</c> line, already as text (see <see cref="SessionLine"/>).</summary>
    public struct SessionFields
    {
        public string Sha, Built, Build, Platform, Device, Gpu, Api, Quality, Os, Unity, Date, Battery;
        /// <summary>8-hex hash of the live-ops balance delta in force (<c>BalanceBook.Hash</c>).</summary>
        public string Balance;
        /// <summary>The network protocol version (<c>NetworkProtocol.Version</c>): builds with different ones never share a room.</summary>
        public string Protocol;
        public bool DevTools, PerfMode;
        public int Width, Height;
        public double RefreshHz;
    }

    /// <summary>
    /// Formats the <c>Session</c> line: <c>Session sha=.. built=.. build=.. devtools=0|1 platform=.. device=.. gpu=.. api=..
    /// quality=.. perfMode=0|1 res=WxH hz=.. os=.. unity=.. date=yyyy-MM-dd battery=.. bal=.. proto=..</c>. Values are made
    /// key=value-safe (whitespace → <c>_</c>, no <c>=</c>), empty ones print <c>n/a</c>. Pure; pinned by tests.
    /// </summary>
    public static class SessionLine
    {
        public const string BootPrefix = "Session";
        public const string ChangePrefix = "SessionChange";

        public static string Format(string prefix, in SessionFields f)
        {
            var sb = new StringBuilder(256);
            sb.Append(prefix);
            Add(sb, "sha", f.Sha);
            Add(sb, "built", f.Built);
            Add(sb, "build", f.Build);
            Add(sb, "devtools", f.DevTools ? "1" : "0");
            Add(sb, "platform", f.Platform);
            Add(sb, "device", f.Device);
            Add(sb, "gpu", f.Gpu);
            Add(sb, "api", f.Api);
            Add(sb, "quality", f.Quality);
            Add(sb, "perfMode", f.PerfMode ? "1" : "0");
            Add(sb, "res", f.Width.ToString(CultureInfo.InvariantCulture) + "x" + f.Height.ToString(CultureInfo.InvariantCulture));
            Add(sb, "hz", f.RefreshHz > 0d ? f.RefreshHz.ToString("0.##", CultureInfo.InvariantCulture) : null);
            Add(sb, "os", f.Os);
            Add(sb, "unity", f.Unity);
            Add(sb, "date", f.Date);
            Add(sb, "battery", f.Battery);
            Add(sb, "bal", f.Balance);
            Add(sb, "proto", f.Protocol);
            return sb.ToString();
        }

        /// <summary>Makes a value safe for a space-separated key=value line.</summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "n/a";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                if (char.IsWhiteSpace(c)) sb.Append('_');
                else if (c != '=') sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "n/a";
        }

        /// <summary>Battery level 0..1 (Unity reports -1 when unknown) as a percentage, or n/a.</summary>
        public static string Battery(float level) =>
            level < 0f ? "n/a" : Mathf.RoundToInt(Mathf.Clamp01(level) * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        private static void Add(StringBuilder sb, string key, string value) =>
            sb.Append(' ').Append(key).Append('=').Append(Sanitize(value));
    }

    /// <summary>
    /// Logs the <c>Session</c> line once at boot (Source <c>Session</c>, Info) and again, prefixed
    /// <c>SessionChange</c>, whenever the quality tier, Performance Mode or the balance hash (<c>bal=</c>) changes. Two peers
    /// whose <c>bal=</c> differ play different numbers: warn-only by design (D3c, <c>BalanceBook</c> remarks). Always on (one line at boot plus
    /// two int/bool compares per frame), unlike the dev-tools probes: it is what ties a log file to a build and a device.
    /// Bound non-lazy in <c>ProjectInstaller</c>.
    /// </summary>
    /// <remarks>Battery temperature is not included: Unity has no API for it (it needs an Android broadcast receiver).
    /// The battery level is, since <c>SystemInfo.batteryLevel</c> is free.</remarks>
    public sealed class SessionStamp : IInitializable, ITickable
    {
        private const string Source = "Session";

        private readonly ILogService _log;
        private readonly BalanceBook _balance;
        private string _sha = "unknown", _built = "unknown", _build = "unknown";
        private int _quality = -1;
        private bool _perfMode;
        private string _balanceHash;

        public SessionStamp(ILogService log, BalanceBook balance)
        {
            _log = log;
            _balance = balance;
        }

        public void Initialize()
        {
            if (!BuildInfo.TryLoad(out _sha, out _built, out _build, out var problem))
                _log.Warn(Source, $"Build info unavailable ({problem}); the Session line says sha=unknown.");
            _quality = QualitySettings.GetQualityLevel();
            _perfMode = PlayerPreferences.PerformanceModeEnabled;
            _balanceHash = PublishBalance();
            _log.Info(Source, SessionLine.Format(SessionLine.BootPrefix, Capture()));
        }

        public void Tick()
        {
            int quality = QualitySettings.GetQualityLevel();
            bool perf = PlayerPreferences.PerformanceModeEnabled;
            string balance = PublishBalance();
            if (quality == _quality && perf == _perfMode && balance == _balanceHash) return;
            _quality = quality;
            _perfMode = perf;
            _balanceHash = balance;
            _log.Info(Source, SessionLine.Format(SessionLine.ChangePrefix, Capture()));
        }

        private SessionFields Capture()
        {
            var names = QualitySettings.names;
            int q = QualitySettings.GetQualityLevel();
            return new SessionFields
            {
                Sha = _sha,
                Built = _built,
                Build = _build,
                DevTools = DevTools.Available,
                Platform = Application.platform.ToString(),
                Device = SystemInfo.deviceModel,
                Gpu = SystemInfo.graphicsDeviceName,
                Api = SystemInfo.graphicsDeviceType.ToString(),
                Quality = q >= 0 && q < names.Length ? names[q] : q.ToString(CultureInfo.InvariantCulture),
                PerfMode = PlayerPreferences.PerformanceModeEnabled,
                Width = Screen.width,
                Height = Screen.height,
                RefreshHz = Screen.currentResolution.refreshRateRatio.value,
                Os = SystemInfo.operatingSystem,
                Unity = Application.unityVersion,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Battery = SessionLine.Battery(SystemInfo.batteryLevel),
                Balance = _balanceHash,
                Protocol = NetworkProtocol.Version.ToString(CultureInfo.InvariantCulture),
            };
        }

        /// <summary>The book's hash, also handed to the debug HUD (<see cref="DiagnosticsReadout"/>).</summary>
        private string PublishBalance()
        {
            DiagnosticsReadout.BalanceHash = _balance.Hash;
            DiagnosticsReadout.BalanceOrigin = _balance.Origin.ToString();
            DiagnosticsReadout.ProtocolVersion = NetworkProtocol.Version;
            return _balance.Hash;
        }
    }
}
