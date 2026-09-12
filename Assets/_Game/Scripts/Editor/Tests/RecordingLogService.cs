using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Logging;

namespace CluckWars.Tests
{
    /// <summary>
    /// Test double for <see cref="ILogService"/>: records every line, at every level, in order.
    /// </summary>
    internal sealed class RecordingLogService : ILogService
    {
        public sealed class Entry
        {
            public LogLevel Level;
            public string Source;
            public string Message;
            public Exception Exception;
        }

        public readonly List<Entry> Entries = new List<Entry>();

        public LogLevel MinLevel { get; set; } = LogLevel.Verbose;

        public bool IsEnabled(LogLevel level) => true;

        public List<Entry> OfLevel(LogLevel level) => Entries.Where(e => e.Level == level).ToList();

        public void Verbose(string source, string message) => Add(LogLevel.Verbose, source, message, null);
        public void Debug(string source, string message)   => Add(LogLevel.Debug, source, message, null);
        public void Info(string source, string message)    => Add(LogLevel.Info, source, message, null);
        public void Warn(string source, string message)    => Add(LogLevel.Warn, source, message, null);

        public void Error(string source, string message, Exception exception = null) =>
            Add(LogLevel.Error, source, message, exception);

        private void Add(LogLevel level, string source, string message, Exception exception) =>
            Entries.Add(new Entry { Level = level, Source = source, Message = message, Exception = exception });
    }
}
