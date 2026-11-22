using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CluckWars.Balance
{
    /// <summary>A win rate with its Wilson score interval. <see cref="Rate"/> is NaN when there were no trials (the interval is then 0 to 1).</summary>
    public readonly struct WinInterval
    {
        public readonly int Wins;
        public readonly int Trials;
        public readonly float Rate;
        public readonly float Low;
        public readonly float High;

        public WinInterval(int wins, int trials, float rate, float low, float high)
        {
            Wins = wins; Trials = trials; Rate = rate; Low = low; High = high;
        }

        /// <summary>The Wilson score interval of <paramref name="wins"/> in <paramref name="trials"/> at <paramref name="z"/> standard deviations (1.96 is 95 %).</summary>
        public static WinInterval Wilson(int wins, int trials, float z = 1.96f)
        {
            if (trials < 0 || wins < 0 || wins > trials)
                throw new ArgumentOutOfRangeException(nameof(wins), $"{wins} wins in {trials} trials is not a count.");
            if (trials == 0) return new WinInterval(0, 0, float.NaN, 0f, 1f);

            double n = trials, p = wins / n, z2 = (double)z * z;
            double denom = 1.0 + z2 / n;
            double centre = (p + z2 / (2.0 * n)) / denom;
            double half = z * Math.Sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / denom;
            return new WinInterval(wins, trials, (float)p, (float)Math.Max(0.0, centre - half), (float)Math.Min(1.0, centre + half));
        }
    }

    /// <summary>
    /// One row of the Spoiler's kills-to-win table, from matches where the Spoiler was granted exactly <see cref="Kills"/> kills.
    /// <see cref="Win"/> is the win rate without the passive's end-of-clock bonus, <see cref="WinWithPassive"/> with it.
    /// </summary>
    public sealed class SpoilerKillRow
    {
        public int Kills;
        public ForcedVictims Victims;
        public WinInterval Win;
        public WinInterval WinWithPassive;
        /// <summary>The Spoiler's final banked food (the passive's bonus included when the clock ended), averaged.</summary>
        public float MeanFinal;
        /// <summary>The highest banked food among the other seats, averaged.</summary>
        public float MeanBestRival;
        /// <summary>Share of matches that went to the clock (no seat banked the target first).</summary>
        public float NobodyReachedTarget;
        /// <summary>How the row stands against the reference's kill targets; null when the reference holds no target for this many kills.</summary>
        public string Label;
    }

    /// <summary>Statistics of the match report: pure functions over plain numbers.</summary>
    public static class MatchStats
    {
        /// <summary>
        /// Kendall's tau-b rank correlation of two equally long series: +1 for the same order, -1 for the reverse, 0 for none. Ties count
        /// as neither concordant nor discordant (tau-b). NaN when fewer than two points or one series is constant.
        /// </summary>
        public static float KendallTau(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count != b.Count) throw new ArgumentException($"The series differ in length ({a.Count} and {b.Count}).");

            int n = a.Count;
            long concordant = 0, discordant = 0, tiedA = 0, tiedB = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    int sa = Math.Sign(a[i].CompareTo(a[j]));
                    int sb = Math.Sign(b[i].CompareTo(b[j]));
                    if (sa == 0) tiedA++;
                    if (sb == 0) tiedB++;
                    if (sa == 0 || sb == 0) continue;
                    if (sa == sb) concordant++; else discordant++;
                }
            double pairs = n * (n - 1) / 2.0;
            double denom = Math.Sqrt((pairs - tiedA) * (pairs - tiedB));
            return denom <= 0.0 ? float.NaN : (float)((concordant - discordant) / denom);
        }

        /// <summary>
        /// The kills-to-win row of a set of matches in which seat <paramref name="spoilerSeat"/> was granted exactly <paramref name="kills"/>
        /// kills on <paramref name="victims"/> (the caller passes only those matches; the row records what they were). The rate without the
        /// passive counts <see cref="MatchResult.WinnerWithoutSpoilerBonus"/>, the rate with it counts <see cref="MatchResult.Winner"/>.
        /// </summary>
        public static SpoilerKillRow KillsToWin(IReadOnlyList<MatchResult> results, int spoilerSeat, int kills, ForcedVictims victims,
            IReadOnlyList<SpoilerKillTarget> targets)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var played = new List<(MatchResult Result, int Seat)>(results.Count);
            foreach (var r in results) played.Add((r, spoilerSeat));
            return KillsToWin(played, kills, victims, targets);
        }

        /// <summary>
        /// The same row for matches in which the Spoiler sat in a different seat each time: each entry names the match and the seat the Spoiler
        /// held in it. Used for the table of kills the policies really made, where the Spoiler's seat varies with the seating.
        /// </summary>
        public static SpoilerKillRow KillsToWin(IReadOnlyList<(MatchResult Result, int Seat)> played, int kills, ForcedVictims victims,
            IReadOnlyList<SpoilerKillTarget> targets)
        {
            if (played == null) throw new ArgumentNullException(nameof(played));
            int wins = 0, winsWith = 0, clock = 0;
            double final = 0.0, best = 0.0;
            foreach (var (r, spoilerSeat) in played)
            {
                if (r.WinnerWithoutSpoilerBonus == spoilerSeat) wins++;
                if (r.Winner == spoilerSeat) winsWith++;
                if (r.NobodyReachedTarget) clock++;
                final += r.Banked[spoilerSeat];
                float rival = 0f;
                for (int s = 0; s < r.Banked.Length; s++)
                    if (s != spoilerSeat && r.Banked[s] > rival) rival = r.Banked[s];
                best += rival;
            }
            int n = played.Count;
            var row = new SpoilerKillRow
            {
                Kills = kills,
                Victims = victims,
                Win = WinInterval.Wilson(wins, n),
                WinWithPassive = WinInterval.Wilson(winsWith, n),
                MeanFinal = n > 0 ? (float)(final / n) : float.NaN,
                MeanBestRival = n > 0 ? (float)(best / n) : float.NaN,
                NobodyReachedTarget = n > 0 ? clock / (float)n : float.NaN,
            };
            row.Label = Label(row, targets);
            return row;
        }

        private static string Label(SpoilerKillRow row, IReadOnlyList<SpoilerKillTarget> targets)
        {
            if (targets == null) return null;
            StringBuilder sb = null;
            foreach (var t in targets)
            {
                if (t.Kills != row.Kills) continue;
                float rate = t.WithPassive ? row.WinWithPassive.Rate : row.Win.Rate;
                string verdict = float.IsNaN(rate) ? "NO DATA" : rate >= t.MinWinRate - 1e-6f && rate <= t.MaxWinRate + 1e-6f ? "IN" : "OUT";
                if (sb == null) sb = new StringBuilder(); else sb.Append("; ");
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} passive {1} [{2:0.00}, {3:0.00}]",
                    t.WithPassive ? "with" : "without", verdict, t.MinWinRate, t.MaxWinRate));
            }
            return sb?.ToString();
        }
    }
}
