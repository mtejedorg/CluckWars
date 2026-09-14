using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>One valid journal line: the outcome it holds and its canonical serialization.</summary>
    /// <remarks>
    /// <see cref="Canonical"/> is <c>JsonUtility.ToJson</c> of the parsed outcome, not the raw line, so two
    /// lines that differ only in whitespace or field order are the same record when duplicates fold.
    /// </remarks>
    public readonly struct JournalRecord
    {
        public readonly RoundOutcome Outcome;
        public readonly string Canonical;

        public JournalRecord(RoundOutcome outcome, string canonical)
        {
            Outcome = outcome;
            Canonical = canonical;
        }

        public static JournalRecord Of(RoundOutcome outcome) =>
            new JournalRecord(outcome, outcome == null ? string.Empty : JsonUtility.ToJson(outcome));
    }

    /// <summary>What <see cref="JournalStore.Load"/> did with a final line that had no newline.</summary>
    public enum TornTail
    {
        /// <summary>The file ended with a newline (or was empty).</summary>
        None,
        /// <summary>The fragment was a complete, valid record: kept, and its newline written.</summary>
        Repaired,
        /// <summary>The fragment was not a valid record: moved to the <c>.torn</c> file and cut from the journal.</summary>
        Dropped,
    }

    /// <summary>The result of <see cref="JournalStore.Load"/>. Never null, never thrown.</summary>
    public sealed class JournalLoadResult
    {
        /// <summary>
        /// False when the journal could not be read, or a torn tail could not be made safe to append
        /// after. The caller must not append in that case, and <see cref="Error"/> says why.
        /// </summary>
        public bool Succeeded;

        /// <summary>Why loading failed, when it did.</summary>
        public Exception Error;

        /// <summary>Every valid record, in file order. Duplicates are kept; the fold resolves them.</summary>
        public readonly List<JournalRecord> Records = new List<JournalRecord>();

        /// <summary>One entry per malformed middle line: <c>"line N: reason"</c>. The file is not rewritten for these.</summary>
        public readonly List<string> SkippedLines = new List<string>();

        /// <summary>What happened to a final line with no newline.</summary>
        public TornTail TornTail;

        /// <summary>Why the torn tail was dropped, and how many bytes it held.</summary>
        public string TornTailDetail;
    }

    /// <summary>The result of <see cref="JournalStore.Append"/>. Never thrown.</summary>
    public readonly struct JournalAppendResult
    {
        public readonly bool Succeeded;
        public readonly Exception Error;
        public readonly double Milliseconds;

        /// <summary>
        /// The record exactly as the written line reads back — what <see cref="JournalStore.Load"/> will
        /// return for it. Default (no outcome) when the append failed.
        /// </summary>
        public readonly JournalRecord Record;

        public JournalAppendResult(bool succeeded, Exception error, double milliseconds, JournalRecord record)
        {
            Succeeded = succeeded;
            Error = error;
            Milliseconds = milliseconds;
            Record = record;
        }
    }

    /// <summary>
    /// The progression journal: one <see cref="RoundOutcome"/> per line, JSON Lines via
    /// <c>JsonUtility</c>, UTF-8 without BOM, at <c>&lt;directory&gt;/journal.jsonl</c>.
    /// </summary>
    /// <remarks>
    /// <b>The journal is the wallet.</b> Nothing derived — Grain, the balance, stats — is stored; it is
    /// folded from these lines (<see cref="ProgressionLedger"/>). Append-only: a line is written and
    /// flushed to disk before anything based on it is shown.
    /// <para>
    /// <b>Memory equals disk.</b> <see cref="Append"/> serializes an outcome once, writes exactly that
    /// string, and returns the record parsed back from it, so what the caller folds is what a reload reads.
    /// </para>
    /// <para>
    /// <b>A crash can only tear the final line.</b> <see cref="Load"/> keeps a torn tail that is a
    /// complete record and writes its missing newline; anything else it moves to
    /// <c>journal.jsonl.torn</c> and cuts from the journal. <see cref="Append"/> also starts every record on
    /// a line of its own, so a partial line left by an earlier failed append stays one malformed line
    /// instead of swallowing the next round. Malformed lines in the middle are skipped and reported, never
    /// rewritten.
    /// </para>
    /// <para>
    /// Constructed with a directory and never reads <c>Application.persistentDataPath</c> itself, so
    /// tests run against a temporary directory and never touch a real journal. The constructor does
    /// no I/O. Every method reports failure in its result instead of throwing.
    /// </para>
    /// </remarks>
    public sealed class JournalStore
    {
        public const string FileName = "journal.jsonl";
        public const string TornSuffix = ".torn";

        private const byte Newline = (byte)'\n';

        // Strict decoder: a line torn inside a multi-byte character is malformed, not silently patched.
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public JournalStore(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A journal directory is required.", nameof(directory));
            DirectoryPath = directory;
            JournalPath = Path.Combine(directory, FileName);
            TornPath = JournalPath + TornSuffix;
        }

        public string DirectoryPath { get; }
        public string JournalPath { get; }
        public string TornPath { get; }

        /// <summary>
        /// Appends <paramref name="outcome"/> as one line — one open, one write, then a flush to disk —
        /// and returns the record as that line reads back. Creates the directory on first use.
        /// </summary>
        /// <remarks>
        /// An outcome whose serialization would not read back as a valid record is refused rather than
        /// written: a line <see cref="Load"/> would skip is worth nothing and would make memory disagree
        /// with disk.
        /// </remarks>
        public JournalAppendResult Append(RoundOutcome outcome)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (outcome == null) throw new ArgumentNullException(nameof(outcome));

                string json = JsonUtility.ToJson(outcome);
                if (!TryParse(json, out var readBack, out string reason))
                {
                    throw new InvalidDataException(
                        $"Round {outcome.RoundId} would not read back from the journal ({reason}), so it was not written.");
                }
                byte[] line = Utf8.GetBytes(json + "\n");

                Directory.CreateDirectory(DirectoryPath);
                using (var stream = new FileStream(JournalPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
                           bufferSize: 4096, FileOptions.WriteThrough))
                {
                    // A failed earlier append can leave a partial line with no newline. Start this record on
                    // a line of its own, so the fragment stays one malformed line and this round survives.
                    if (stream.Length > 0)
                    {
                        stream.Seek(-1, SeekOrigin.End);
                        if (stream.ReadByte() != Newline) stream.WriteByte(Newline);
                    }

                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(line, 0, line.Length);
                    stream.Flush(flushToDisk: true);
                }

                return new JournalAppendResult(true, null, clock.Elapsed.TotalMilliseconds, JournalRecord.Of(readBack));
            }
            catch (Exception e)
            {
                // Reported, not swallowed: the caller logs it and withholds the award.
                return new JournalAppendResult(false, e, clock.Elapsed.TotalMilliseconds, default);
            }
        }

        /// <summary>
        /// Reads every valid record. A missing file is an empty journal (first run), not a failure.
        /// </summary>
        public JournalLoadResult Load()
        {
            var result = new JournalLoadResult();

            byte[] bytes;
            try
            {
                if (Directory.Exists(JournalPath))
                {
                    // Not a first run: nothing could ever be appended here.
                    result.Error = new IOException($"{JournalPath} is a directory, not a journal file.");
                    return result;
                }

                if (!File.Exists(JournalPath))
                {
                    result.Succeeded = true;
                    return result;
                }
                bytes = File.ReadAllBytes(JournalPath);
            }
            catch (Exception e)
            {
                result.Error = e;
                return result;
            }

            // Tolerate a BOM someone's editor added; the store itself never writes one.
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            int completeEnd = Array.LastIndexOf(bytes, Newline) + 1; // [0, completeEnd) holds whole lines
            int lineNumber = 0;

            for (int i = start; i < completeEnd;)
            {
                int nl = Array.IndexOf(bytes, Newline, i, completeEnd - i);
                lineNumber++;
                ReadMiddleLine(bytes, i, nl - i, lineNumber, result);
                i = nl + 1;
            }

            int tailStart = Math.Max(completeEnd, start);
            if (tailStart < bytes.Length)
            {
                lineNumber++;
                if (!ReadTornTail(bytes, tailStart, lineNumber, completeEnd, result)) return result;
            }

            result.Succeeded = true;
            return result;
        }

        private static void ReadMiddleLine(byte[] bytes, int offset, int length, int lineNumber, JournalLoadResult result)
        {
            if (!TryDecode(bytes, offset, length, out string text, out string reason))
            {
                result.SkippedLines.Add($"line {lineNumber}: {reason}");
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) return; // blank lines carry nothing

            if (TryParse(text, out var outcome, out reason)) result.Records.Add(JournalRecord.Of(outcome));
            else result.SkippedLines.Add($"line {lineNumber}: {reason}");
        }

        /// <returns>False when the tail could not be made safe; <see cref="JournalLoadResult.Error"/> is set.</returns>
        private bool ReadTornTail(byte[] bytes, int tailStart, int lineNumber, int completeEnd, JournalLoadResult result)
        {
            int length = bytes.Length - tailStart;
            string reason;
            if (TryDecode(bytes, tailStart, length, out string text, out reason) &&
                !string.IsNullOrWhiteSpace(text) &&
                TryParse(text, out var outcome, out reason))
            {
                // A complete record that only lost its newline: keep it, and write the newline so the
                // next append starts on a line of its own.
                try
                {
                    using (var stream = new FileStream(JournalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        stream.WriteByte(Newline);
                        stream.Flush(flushToDisk: true);
                    }
                }
                catch (Exception e)
                {
                    result.Error = new IOException($"Line {lineNumber} of {JournalPath} has no trailing newline and it " +
                        "could not be written, so appending would corrupt the next record.", e);
                    return false;
                }

                result.Records.Add(JournalRecord.Of(outcome));
                result.TornTail = TornTail.Repaired;
                return true;
            }

            reason = string.IsNullOrWhiteSpace(text) ? "only whitespace" : reason;
            try
            {
                // Preserve before cutting: if preserving fails, nothing is cut.
                using (var torn = new FileStream(TornPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    torn.Write(bytes, tailStart, length);
                    torn.WriteByte(Newline); // keeps successive fragments apart
                    torn.Flush(flushToDisk: true);
                }

                using (var journal = new FileStream(JournalPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    journal.SetLength(completeEnd);
                    journal.Flush(flushToDisk: true);
                }
            }
            catch (Exception e)
            {
                result.Error = new IOException($"Line {lineNumber} of {JournalPath} is incomplete ({reason}) and could " +
                    $"not be moved to {TornPath}, so appending would corrupt the next record.", e);
                return false;
            }

            result.TornTail = TornTail.Dropped;
            result.TornTailDetail = $"line {lineNumber}, {length} bytes: {reason}";
            return true;
        }

        private static bool TryDecode(byte[] bytes, int offset, int length, out string text, out string reason)
        {
            try
            {
                text = Utf8.GetString(bytes, offset, length).TrimEnd('\r');
                reason = null;
                return true;
            }
            catch (DecoderFallbackException e)
            {
                text = null;
                reason = "not valid UTF-8 (" + e.Message + ")";
                return false;
            }
        }

        /// <summary>Parses and validates one line. <c>JsonUtility</c> returns a defaulted object for <c>{}</c>, hence the checks.</summary>
        private static bool TryParse(string text, out RoundOutcome outcome, out string reason)
        {
            try
            {
                outcome = JsonUtility.FromJson<RoundOutcome>(text);
            }
            catch (Exception e)
            {
                outcome = null;
                reason = $"not JSON ({e.GetType().Name}: {e.Message})";
                return false;
            }

            reason = outcome == null ? "empty"
                : !RoundOutcome.IsReadableSchema(outcome.SchemaVersion)
                    ? $"SchemaVersion {outcome.SchemaVersion} is not one this build reads (1..{RoundOutcome.CurrentSchemaVersion})"
                : string.IsNullOrEmpty(outcome.RoundId) ? "no RoundId"
                : !ProgressionCalendar.TryParseUtc(outcome.EndedAtUtc, out _) ? $"EndedAtUtc '{outcome.EndedAtUtc}' is not an ISO-8601 instant"
                : null;
            return reason == null;
        }
    }
}
