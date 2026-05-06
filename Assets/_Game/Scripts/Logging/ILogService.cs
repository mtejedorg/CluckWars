using System;

namespace CluckWars.Logging
{
    /// <summary>
    /// Project-wide structured logger. Replaces ad-hoc <c>Debug.Log</c> calls so we get
    /// a single chokepoint for filtering, formatting, and (later) routing to an
    /// on-screen overlay or a file sink. Bound app-wide in <c>ProjectInstaller</c>.
    /// </summary>
    public interface ILogService
    {
        /// <summary>Messages below this level are dropped. Default is <see cref="LogLevel.Verbose"/>.</summary>
        LogLevel MinLevel { get; set; }

        /// <summary>Cheap level check so callers can avoid building expensive log strings.</summary>
        bool IsEnabled(LogLevel level);

        void Verbose(string source, string message);
        void Debug(string source, string message);
        void Info(string source, string message);
        void Warn(string source, string message);
        void Error(string source, string message, Exception exception = null);
    }
}
