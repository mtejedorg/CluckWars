using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>Which action wrote an <see cref="OracleReferenceSO"/>: the one that keeps the prices or the one that recomputes them.</summary>
    public enum ReferenceOrigin
    {
        /// <summary>An asset written before the origin was stored.</summary>
        Unknown = 0,
        /// <summary><see cref="OracleReferenceSO.Record"/>: measured numbers stored, the offsets left as they were.</summary>
        Recorded = 1,
        /// <summary><see cref="OracleReferenceSO.Accept"/>: the offsets were recomputed so every kit's result is equal.</summary>
        AcceptedAsBalanced = 2,
    }

    /// <summary>
    /// The state of the game Maestro last recorded or accepted: the seconds every foraging kit needs to bank the win target in the oracle's
    /// economy run, with the offset in seconds each kit is worth beyond that (a credit when positive; spec 5.4, 5.6). The offsets feed
    /// <see cref="OracleRunner.Run"/>; the drift test compares a fresh measurement with <see cref="Entry.BestSeconds"/>. Two Editor-only actions write it:
    /// <see cref="Record"/> (<c>Record baseline (keep prices)</c>) re-arms the drift guard and leaves the prices alone,
    /// <see cref="Accept"/> (<c>Accept as balanced (recompute prices)</c>) is the deliberate "I consider this state balanced".
    /// The asset is committed, so a player build only measures.
    /// </summary>
    public sealed class OracleReferenceSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Label;
            /// <summary>The measured seconds to the win target (<see cref="KitLine.BestSeconds"/>), before the offset.</summary>
            public float BestSeconds;
            /// <summary>The kit's price in seconds; positive is a credit, taken off the seconds to give <see cref="KitLine.Result"/>.</summary>
            public float OffsetSeconds;
        }

        public string AcceptedOn;
        public string Note;
        public string MapFingerprint;
        /// <summary>Which action produced the stored offsets.</summary>
        public ReferenceOrigin Origin;
        public List<Entry> Entries = new List<Entry>();

        /// <summary>Offset in seconds per kit label, the shape <see cref="OracleRunner.Run"/> takes.</summary>
        public IReadOnlyDictionary<string, float> Offsets()
        {
            var offsets = new Dictionary<string, float>(Entries.Count);
            foreach (var entry in Entries) offsets[entry.Label] = entry.OffsetSeconds;
            return offsets;
        }

        /// <summary>The origin of the prices in plain words, for the report header; empty when nothing was stored.</summary>
        public string PricesText()
        {
            switch (Origin)
            {
                case ReferenceOrigin.Recorded:
                    return $"prices set by Maestro (offsets typed or carried over; baseline recorded {AcceptedOn})";
                case ReferenceOrigin.AcceptedAsBalanced:
                    return $"prices recomputed by the oracle when this state was accepted as balanced ({AcceptedOn})";
                default:
                    return $"prices of unknown origin (reference stored {AcceptedOn})";
            }
        }

        /// <summary>
        /// Stores the measured numbers of <paramref name="report"/>'s foraging kits and KEEPS the offsets already in the asset (a kit
        /// with no stored offset gets 0). Re-arms the drift guard after an intended change without touching anyone's prices.
        /// A kit that never forages has no number and gets no entry.
        /// </summary>
        public static void Record(OracleReferenceSO target, OracleReport report, string note, string date)
        {
            var applicable = Validate(target, report, date);
            var kept = target.Offsets();

            var entries = new List<Entry>(applicable.Count);
            foreach (var line in applicable)
                entries.Add(new Entry
                {
                    Label = line.Label,
                    BestSeconds = line.BestSeconds,
                    OffsetSeconds = kept.TryGetValue(line.Label, out var offset) ? offset : 0f,
                });
            Store(target, report, note, date, entries, ReferenceOrigin.Recorded);
        }

        /// <summary>
        /// Stores the measured numbers of <paramref name="report"/>'s foraging kits and sets each kit's offset so every one
        /// of them lands on the mean seconds: <c>BestSeconds - mean</c>, so <see cref="KitLine.Result"/> (<c>BestSeconds - offset</c>) is the mean for all. This replaces every stored offset:
        /// it is the deliberate "I consider this state balanced". A kit that never forages has no number and gets no entry.
        /// </summary>
        public static void Accept(OracleReferenceSO target, OracleReport report, string note, string date)
        {
            var applicable = Validate(target, report, date);

            float sum = 0f;
            foreach (var line in applicable) sum += line.BestSeconds;
            float mean = sum / applicable.Count;

            var entries = new List<Entry>(applicable.Count);
            foreach (var line in applicable)
                entries.Add(new Entry { Label = line.Label, BestSeconds = line.BestSeconds, OffsetSeconds = line.BestSeconds - mean });
            Store(target, report, note, date, entries, ReferenceOrigin.AcceptedAsBalanced);
        }

        private static List<KitLine> Validate(OracleReferenceSO target, OracleReport report, string date)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(date)) throw new ArgumentException("A date is required.", nameof(date));
            var applicable = report.Economy.FindAll(l => l.Applicable);
            if (applicable.Count == 0) throw new InvalidOperationException("The report has no foraging kit to store.");
            return applicable;
        }

        private static void Store(OracleReferenceSO target, OracleReport report, string note, string date, List<Entry> entries, ReferenceOrigin origin)
        {
            target.AcceptedOn = date;
            target.Note = note ?? string.Empty;
            target.MapFingerprint = report.MapFingerprint;
            target.Origin = origin;
            target.Entries = entries;
        }
    }
}
