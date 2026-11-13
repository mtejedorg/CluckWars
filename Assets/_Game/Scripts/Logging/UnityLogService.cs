using System;
using UnityEngine;

namespace CluckWars.Logging
{
    /// <summary>
    /// Default <see cref="ILogService"/> implementation that routes to Unity's
    /// <c>Debug.Log</c> family with our own min-level filter and a uniform format
    /// (<see cref="LogLineFormat"/>): <c>[HH:mm:ss.fff][mm:ss.fff][Level][Source] message</c> —
    /// UTC wall clock first (merges logs from several devices on one timeline), then the time
    /// since process start.
    /// </summary>
    public sealed class UnityLogService : ILogService
    {
        public LogLevel MinLevel { get; set; }

        public UnityLogService(LogLevel minLevel = LogLevel.Verbose)
        {
            MinLevel = minLevel;
        }

        public bool IsEnabled(LogLevel level) => level >= MinLevel && MinLevel != LogLevel.Off;

        public void Verbose(string source, string message) => Emit(LogLevel.Verbose, source, message);
        public void Debug(string source, string message)   => Emit(LogLevel.Debug,   source, message);
        public void Info(string source, string message)    => Emit(LogLevel.Info,    source, message);
        public void Warn(string source, string message)    => Emit(LogLevel.Warn,    source, message);

        public void Error(string source, string message, Exception exception = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            var line = Format(LogLevel.Error, source, message);
            // Debug.LogException walks to the INNERMOST exception for the console headline, so wrapping
            // our line in one hid it: the Console showed the cause's type and message and the [Source]
            // tag was only visible inside the trace. LogError keeps our line as the headline, with the
            // exception's own type, message and stack under it.
            UnityEngine.Debug.LogError(exception != null ? line + "\n" + exception : line);
        }

        private void Emit(LogLevel level, string source, string message)
        {
            if (!IsEnabled(level)) return;
            var line = Format(level, source, message);
            switch (level)
            {
                case LogLevel.Warn:  UnityEngine.Debug.LogWarning(line); break;
                case LogLevel.Error: UnityEngine.Debug.LogError(line);   break;
                default:             UnityEngine.Debug.Log(line);        break;
            }
        }

        private static string Format(LogLevel level, string source, string message) =>
            LogLineFormat.Format(DateTime.UtcNow, Time.realtimeSinceStartupAsDouble, level, source, message);
    }
}
