using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// The state of the game Maestro last accepted: what every foraging kit banked in the oracle's economy run, with the
    /// offset in seconds each kit is worth beyond that (spec 5.4, 5.6). The offsets feed <see cref="OracleRunner.Run"/>;
    /// the drift test compares a fresh measurement with <see cref="Entry.BestBanked"/>. Accepting is Editor-only
    /// (<c>Cluck Wars/Balance/Oracle/Accept as reference</c>); the asset is committed, so a player build only measures.
    /// </summary>
    public sealed class OracleReferenceSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Label;
            public float BestBanked;
            public float OffsetSeconds;
        }

        public string AcceptedOn;
        public string Note;
        public string MapFingerprint;
        public List<Entry> Entries = new List<Entry>();

        /// <summary>Offset in seconds per kit label, the shape <see cref="OracleRunner.Run"/> takes.</summary>
        public IReadOnlyDictionary<string, float> Offsets()
        {
            var offsets = new Dictionary<string, float>(Entries.Count);
            foreach (var entry in Entries) offsets[entry.Label] = entry.OffsetSeconds;
            return offsets;
        }

        /// <summary>
        /// Stores the measured numbers of <paramref name="report"/>'s foraging kits and sets each kit's offset so every one
        /// of them lands on the mean food banked: <c>(mean - BestBanked) / IncomePerSecond</c>. A kit that never forages has
        /// no number and gets no entry.
        /// </summary>
        public static void Accept(OracleReferenceSO target, OracleReport report, string note, string date)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(date)) throw new ArgumentException("A date is required.", nameof(date));

            var applicable = report.Economy.FindAll(l => l.Applicable);
            if (applicable.Count == 0) throw new InvalidOperationException("The report has no foraging kit to accept.");

            float sum = 0f;
            foreach (var line in applicable) sum += line.BestBanked;
            float mean = sum / applicable.Count;

            var entries = new List<Entry>(applicable.Count);
            foreach (var line in applicable)
            {
                if (line.IncomePerSecond <= 0f)
                    throw new InvalidOperationException($"{line.Label} banked no food, so an offset in seconds cannot be worked out for it.");
                entries.Add(new Entry
                {
                    Label = line.Label,
                    BestBanked = line.BestBanked,
                    OffsetSeconds = (mean - line.BestBanked) / line.IncomePerSecond,
                });
            }

            target.AcceptedOn = date;
            target.Note = note ?? string.Empty;
            target.MapFingerprint = report.MapFingerprint;
            target.Entries = entries;
        }
    }
}
