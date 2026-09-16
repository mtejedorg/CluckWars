using System;
using System.Globalization;

namespace CluckWars.Progression
{
    /// <summary>Why a round's Grain could, or could not, be evaluated.</summary>
    public enum EvaluationStatus
    {
        /// <summary>Evaluated; <see cref="GrainAward.Grain"/> is the round's Grain (which may be zero).</summary>
        Ok,
        /// <summary>No outcome was given.</summary>
        MissingOutcome,
        /// <summary>No config was given.</summary>
        MissingConfig,
        /// <summary>The config holds a value the formula cannot use (NaN, negative, no placement table).</summary>
        InvalidConfig,
        /// <summary>The outcome's schema is below 1 or newer than <see cref="RoundOutcome.CurrentSchemaVersion"/>.</summary>
        UnknownSchema,
        /// <summary>The placement is below 1 or past the end of the placement table.</summary>
        InvalidPlacement,
        /// <summary>A fact is NaN, infinite or negative, or the result would not fit an int.</summary>
        InvalidFacts,
    }

    /// <summary>
    /// The result of <see cref="ProgressionRules.Evaluate"/>. Keeps "earned zero" (status
    /// <see cref="EvaluationStatus.Ok"/>, Grain 0) apart from "could not evaluate" (any other status).
    /// </summary>
    public readonly struct GrainAward
    {
        public readonly EvaluationStatus Status;
        public readonly int Grain;
        public readonly bool Rested;

        public GrainAward(EvaluationStatus status, int grain, bool rested)
        {
            Status = status;
            Grain = grain;
            Rested = rested;
        }

        public bool IsEvaluable => Status == EvaluationStatus.Ok;

        public static GrainAward NotEvaluable(EvaluationStatus status) => new GrainAward(status, 0, false);
    }

    /// <summary>The Grain formula. Pure and total: it never throws, whatever it is given.</summary>
    public static class ProgressionRules
    {
        /// <summary>
        /// Added before flooring so a product that is an exact integer on paper cannot floor one
        /// short because of binary rounding (e.g. <c>0.29 × 100 = 28.999999999999996</c> in double).
        /// Only a result within one millionth of a Grain below an integer is rounded up, far finer
        /// than any amount of play can move the total (one hundredth of a banked unit at
        /// <c>PerBank</c> 0.4 is 0.004 Grain).
        /// </summary>
        public const double FloorEpsilon = 1e-6;

        /// <summary>
        /// <c>floor((Participation + PerBank·banked + min(PerSteal·stolen, StealCap) + PlacementGrain[placement-1])
        /// × (rested ? RestedMultiplier : 1))</c>, with <c>rested = priorRoundsToday &lt; RestedRounds</c>.
        /// </summary>
        /// <param name="priorRoundsToday">
        /// Evaluable rounds earlier on the same local day (<see cref="ProgressionCalendar"/>), in the
        /// journal's canonical order. A negative count is not a fact the journal can produce and is
        /// refused as <see cref="EvaluationStatus.InvalidFacts"/>.
        /// </param>
        /// <remarks>
        /// Computed in <c>double</c>. Every <c>float</c> input is widened through its shortest
        /// round-trip decimal form, so an authored <c>0.7</c> multiplies as 0.7 rather than as
        /// <c>0.699999988…</c> (which would floor <c>0.7 × 1000</c> to 699); the remaining double
        /// error is covered by <see cref="FloorEpsilon"/>.
        /// </remarks>
        public static GrainAward Evaluate(RoundOutcome outcome, ProgressionConfigSO config, int priorRoundsToday)
        {
            if (outcome == null) return GrainAward.NotEvaluable(EvaluationStatus.MissingOutcome);

            // Unity's overloaded == so a destroyed config reads as missing too.
            if (config == null) return GrainAward.NotEvaluable(EvaluationStatus.MissingConfig);
            if (!IsUsable(config)) return GrainAward.NotEvaluable(EvaluationStatus.InvalidConfig);

            if (!RoundOutcome.IsReadableSchema(outcome.SchemaVersion))
                return GrainAward.NotEvaluable(EvaluationStatus.UnknownSchema);

            if (outcome.Placement < 1 || outcome.Placement > config.PlacementGrain.Length)
                return GrainAward.NotEvaluable(EvaluationStatus.InvalidPlacement);

            if (!IsNonNegativeFinite(outcome.BankedTotal) || !IsNonNegativeFinite(outcome.StolenTotal) ||
                priorRoundsToday < 0)
            {
                return GrainAward.NotEvaluable(EvaluationStatus.InvalidFacts);
            }

            bool rested = priorRoundsToday < config.RestedRounds;

            double steal = Math.Min(Widen(config.PerSteal) * Widen(outcome.StolenTotal), config.StealCap);
            double raw = config.Participation
                         + Widen(config.PerBank) * Widen(outcome.BankedTotal)
                         + steal
                         + config.PlacementGrain[outcome.Placement - 1];
            double total = raw * (rested ? Widen(config.RestedMultiplier) : 1.0);
            double floored = Math.Floor(total + FloorEpsilon);

            if (double.IsNaN(floored) || floored < 0 || floored > int.MaxValue)
                return GrainAward.NotEvaluable(EvaluationStatus.InvalidFacts);

            return new GrainAward(EvaluationStatus.Ok, (int)floored, rested);
        }

        private static bool IsUsable(ProgressionConfigSO c)
        {
            if (c.PlacementGrain == null || c.PlacementGrain.Length == 0) return false;
            foreach (int grain in c.PlacementGrain)
            {
                if (grain < 0) return false;
            }

            return c.Participation >= 0 && c.StealCap >= 0 && c.RestedRounds >= 0 &&
                   IsNonNegativeFinite(c.PerBank) && IsNonNegativeFinite(c.PerSteal) &&
                   IsNonNegativeFinite(c.RestedMultiplier);
        }

        private static bool IsNonNegativeFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

        /// <summary>A finite float as the double its shortest round-trip decimal form names.</summary>
        private static double Widen(float value) =>
            double.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The one definition of "a local calendar day" for progression. The rested bonus uses it, and
    /// slice 4's daily task must reuse it rather than define its own.
    /// </summary>
    public static class ProgressionCalendar
    {
        /// <summary>The ISO-8601 round-trip format every journal timestamp is written in.</summary>
        public const string TimestampFormat = "o";

        /// <summary>The format of <see cref="RoundOutcome.LocalDay"/>: an ISO-8601 calendar date.</summary>
        public const string DayFormat = "yyyy-MM-dd";

        /// <summary>Formats a UTC instant the way the journal stores it. Unspecified kinds are taken as UTC.</summary>
        public static string FormatUtc(DateTime instant) =>
            AsUtc(instant).ToString(TimestampFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses a journal timestamp to a UTC instant. Accepts the round-trip format with a <c>Z</c>
        /// or an explicit offset; refuses anything without one, since its instant would be ambiguous.
        /// </summary>
        public static bool TryParseUtc(string timestamp, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(timestamp)) return false;
            if (!DateTime.TryParseExact(timestamp, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
            {
                return false;
            }

            if (parsed.Kind == DateTimeKind.Unspecified) return false;
            utc = parsed.ToUniversalTime();
            return true;
        }

        /// <summary>The local calendar day (midnight, <c>Kind.Unspecified</c>) of a UTC instant in <paramref name="zone"/>.</summary>
        public static DateTime LocalDay(DateTime utc, TimeZoneInfo zone) =>
            TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone ?? TimeZoneInfo.Local).Date;

        /// <summary><see cref="LocalDay(DateTime, TimeZoneInfo)"/> of a journal timestamp; false if it does not parse.</summary>
        public static bool TryLocalDay(string timestamp, TimeZoneInfo zone, out DateTime day)
        {
            day = default;
            if (!TryParseUtc(timestamp, out var utc)) return false;
            day = LocalDay(utc, zone);
            return true;
        }

        /// <summary>A calendar day as <see cref="RoundOutcome.LocalDay"/> stores it (<c>yyyy-MM-dd</c>, invariant).</summary>
        public static string FormatDay(DateTime day) => day.Date.ToString(DayFormat, CultureInfo.InvariantCulture);

        /// <summary>Parses a <see cref="RoundOutcome.LocalDay"/>; false for null, empty or any other shape.</summary>
        public static bool TryParseDay(string text, out DateTime day)
        {
            day = default;
            return !string.IsNullOrEmpty(text) &&
                   DateTime.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        /// <summary>
        /// The day a journal round counts toward: its stamped <paramref name="stampedDay"/> when it has one
        /// that parses, else the day of <paramref name="endedUtc"/> in <paramref name="zone"/> (lines written
        /// before the stamp existed).
        /// </summary>
        public static DateTime DayOfRound(string stampedDay, DateTime endedUtc, TimeZoneInfo zone) =>
            TryParseDay(stampedDay, out var day) ? day : LocalDay(endedUtc, zone);

        /// <summary>
        /// The Monday (midnight, <c>Kind.Unspecified</c>) of the ISO-8601 week <paramref name="day"/>
        /// falls in. ISO weeks run Monday–Sunday, so a Sunday belongs to the week that started the
        /// Monday before it, not the Monday after.
        /// </summary>
        public static DateTime IsoWeekStart(DateTime day)
        {
            day = day.Date;
            // DayOfWeek.Sunday is 0; ISO wants Monday=1..Sunday=7 so the offset back to Monday is
            // never negative for a Sunday.
            int iso = (int)day.DayOfWeek == 0 ? 7 : (int)day.DayOfWeek;
            return day.AddDays(1 - iso);
        }

        /// <summary>
        /// A stable, human-legible key for the ISO week <paramref name="day"/> falls in, e.g.
        /// <c>"2026-W38"</c>. <see cref="GoalRotation"/> seeds the weekly goal selection from this —
        /// never a ticking countdown, so two devices (or a reload) agree on the week's three goals
        /// without any of them being stored.
        /// </summary>
        public static string IsoWeekKey(DateTime day)
        {
            var monday = IsoWeekStart(day);
            // ISO 8601: the week containing the year's first Thursday is week 1, and Jan 4 always
            // falls in week 1 — so the ISO year is the Thursday's year, and the week number is the
            // whole number of Mondays between this week's Monday and week 1's Monday.
            int isoYear = monday.AddDays(3).Year;
            var week1Monday = IsoWeekStart(new DateTime(isoYear, 1, 4));
            int weekNumber = (int)Math.Floor((monday - week1Monday).TotalDays / 7.0) + 1;
            return isoYear.ToString(CultureInfo.InvariantCulture) + "-W" + weekNumber.ToString("00", CultureInfo.InvariantCulture);
        }

        private static DateTime AsUtc(DateTime instant) =>
            instant.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(instant, DateTimeKind.Utc)
                : instant.ToUniversalTime();
    }
}
