using System;
using UnityEngine;

namespace CluckWars.Logging
{
    /// <summary>
    /// Default <see cref="ILogService"/> implementation that routes to Unity's
    /// <c>Debug.Log</c> family with our own min-level filter and a uniform format:
    /// <c>[mm:ss.fff][Level][Source] message</c>.
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
            if (exception != null) UnityEngine.Debug.LogException(new Exception(line, exception));
            else UnityEngine.Debug.LogError(line);
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

        private static string Format(LogLevel level, string source, string message)
        {
            var t = Time.realtimeSinceStartupAsDouble;
            var minutes = (int)(t / 60d);
            var seconds = t - minutes * 60d;
            return $"[{minutes:00}:{seconds:00.000}][{level}][{source}] {message}";
        }
    }
}
