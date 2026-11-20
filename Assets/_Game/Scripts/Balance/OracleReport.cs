using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CluckWars.Balance
{
    /// <summary>
    /// One kit's economy numbers: the kit farming in each seat with each route, the other three seats standard farmers.
    /// A kit that never forages has <see cref="Applicable"/> false and no numbers at all (not zeros).
    /// </summary>
    public sealed class KitLine
    {
        public string Label;
        public bool Applicable;
        /// <summary>Food banked when the clock ends (goal switched off). 4 seats x route count, row-major: cell = seat * routes + route.</summary>
        public float[] BankedBySeatRoute = Array.Empty<float>();
        /// <summary>Clock time the kit first banked the win target, same layout; -1 where it never did.</summary>
        public float[] SecondsBySeatRoute = Array.Empty<float>();
        /// <summary>Route of the cell holding <see cref="BestBanked"/>.</summary>
        public FarmRoute BestRoute;
        public float BestBanked;
        /// <summary>Mean over every seat and route cell.</summary>
        public float MeanBanked;
        /// <summary>Best cell minus worst cell over every seat and route.</summary>
        public float Spread;
        /// <summary>Fastest time to the win target over every cell; -1 when no cell reached it.</summary>
        public float BestSeconds = -1f;
        /// <summary><see cref="BestBanked"/> divided by the clock.</summary>
        public float IncomePerSecond;
        public float OffsetSeconds;
        /// <summary><c>BestBanked + OffsetSeconds * IncomePerSecond</c>.</summary>
        public float Result;
    }

    /// <summary>What the Spoiler ends on after <see cref="Kills"/> executions of victims holding <see cref="CargoPerKill"/>.</summary>
    public sealed class SpoilerRow
    {
        public int Kills;
        public float CargoPerKill;
        public float Final;
        public float FinalWithPassive;
        public float LeaderFinal;
        public bool Wins;
        public bool WinsWithPassive;
    }

    /// <summary>What the Thief has after <see cref="Steals"/> steals from victims holding <see cref="VictimCargo"/>.</summary>
    public sealed class ThiefRow
    {
        public int Steals;
        public float VictimCargo;
        public float Banked;
        public float SecondsNeeded;
        public bool FitsTheClock;
    }

    /// <summary>The oracle's output for one set of inputs: economy per kit (three food-event variants), the Spoiler and Thief tables.</summary>
    public sealed class OracleReport
    {
        public static readonly string[] DefaultNotModelled =
        {
            "Fighting: no stun, shove, slow or steal happens in the economy runs.",
            "Pursuit: nobody chases anybody, so escape speed and Egg Shell show no value.",
            "Scatter cover: the walls are the only obstacles.",
            "Bots' decisions: routes are fixed rules, not what a bot would choose.",
            "The Peck cast, until it ships in the game: a peck gives its food at the end of its cooldown.",
            "Straight-line jump legs: a jumping kit crosses arms in a straight line, which is an approximation.",
        };

        public string MapFingerprint;
        public string BalanceHash;
        public OracleFood Food;
        public float ClockSeconds;
        public float WinTarget;
        public List<KitLine> Economy = new List<KitLine>();
        public List<KitLine> EconomyGoldenPile = new List<KitLine>();
        public List<KitLine> EconomyRestock = new List<KitLine>();
        public List<SpoilerRow> Spoiler = new List<SpoilerRow>();
        public List<ThiefRow> Thief = new List<ThiefRow>();
        public List<string> NotModelled = new List<string>(DefaultNotModelled);

        /// <summary>Spoiler inputs echoed so the page can say them in words.</summary>
        public float SpoilerBountyPerKill;
        public float SpoilerPassiveBonus;
        public float SpoilerRemovalSeconds;
        public float StandardMeanCargo;
        public float StandardCapacity;
        public float StandardLeaderBanked;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- JSON

        public string ToJson()
        {
            var sb = new StringBuilder(8192);
            sb.Append('{');
            Str(sb, "mapFingerprint", MapFingerprint); sb.Append(',');
            Str(sb, "balanceHash", BalanceHash); sb.Append(',');
            Num(sb, "clockSeconds", ClockSeconds); sb.Append(',');
            Num(sb, "winTarget", WinTarget); sb.Append(',');
            sb.Append("\"food\":{");
            Num(sb, "personal", Food.Personal); sb.Append(',');
            Num(sb, "contested", Food.Contested); sb.Append(',');
            Num(sb, "centre", Food.Centre); sb.Append("},");
            Lines(sb, "economy", Economy); sb.Append(',');
            Lines(sb, "economyGoldenPile", EconomyGoldenPile); sb.Append(',');
            Lines(sb, "economyRestock", EconomyRestock); sb.Append(',');

            sb.Append("\"standard\":{");
            Num(sb, "meanCargo", StandardMeanCargo); sb.Append(',');
            Num(sb, "capacity", StandardCapacity); sb.Append(',');
            Num(sb, "leaderBanked", StandardLeaderBanked); sb.Append("},");

            sb.Append("\"spoiler\":{");
            Num(sb, "bountyPerKill", SpoilerBountyPerKill); sb.Append(',');
            Num(sb, "passiveBonus", SpoilerPassiveBonus); sb.Append(',');
            Num(sb, "removalSeconds", SpoilerRemovalSeconds); sb.Append(',');
            sb.Append("\"rows\":[");
            for (int i = 0; i < Spoiler.Count; i++)
            {
                var r = Spoiler[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Num(sb, "kills", r.Kills); sb.Append(',');
                Num(sb, "cargoPerKill", r.CargoPerKill); sb.Append(',');
                Num(sb, "final", r.Final); sb.Append(',');
                Num(sb, "finalWithPassive", r.FinalWithPassive); sb.Append(',');
                Num(sb, "leaderFinal", r.LeaderFinal); sb.Append(',');
                Bool(sb, "wins", r.Wins); sb.Append(',');
                Bool(sb, "winsWithPassive", r.WinsWithPassive);
                sb.Append('}');
            }
            sb.Append("]},");

            sb.Append("\"thief\":[");
            for (int i = 0; i < Thief.Count; i++)
            {
                var r = Thief[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Num(sb, "steals", r.Steals); sb.Append(',');
                Num(sb, "victimCargo", r.VictimCargo); sb.Append(',');
                Num(sb, "banked", r.Banked); sb.Append(',');
                Num(sb, "secondsNeeded", r.SecondsNeeded); sb.Append(',');
                Bool(sb, "fitsTheClock", r.FitsTheClock);
                sb.Append('}');
            }
            sb.Append("],\"notModelled\":[");
            for (int i = 0; i < NotModelled.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(BalanceJson.Quote(NotModelled[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void Lines(StringBuilder sb, string key, List<KitLine> lines)
        {
            sb.Append(BalanceJson.Quote(key)).Append(":[");
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Str(sb, "label", l.Label); sb.Append(',');
                Bool(sb, "applicable", l.Applicable); sb.Append(',');
                Arr(sb, "bankedBySeatRoute", l.BankedBySeatRoute); sb.Append(',');
                Arr(sb, "secondsBySeatRoute", l.SecondsBySeatRoute); sb.Append(',');
                Str(sb, "bestRoute", l.BestRoute.ToString()); sb.Append(',');
                Num(sb, "bestBanked", l.BestBanked); sb.Append(',');
                Num(sb, "meanBanked", l.MeanBanked); sb.Append(',');
                Num(sb, "spread", l.Spread); sb.Append(',');
                Num(sb, "bestSeconds", l.BestSeconds); sb.Append(',');
                Num(sb, "incomePerSecond", l.IncomePerSecond); sb.Append(',');
                Num(sb, "offsetSeconds", l.OffsetSeconds); sb.Append(',');
                Num(sb, "result", l.Result);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void Str(StringBuilder sb, string key, string value) =>
            sb.Append(BalanceJson.Quote(key)).Append(':').Append(BalanceJson.Quote(value ?? string.Empty));

        private static void Num(StringBuilder sb, string key, float value) =>
            sb.Append(BalanceJson.Quote(key)).Append(':').Append(Number(value));

        private static void Bool(StringBuilder sb, string key, bool value) =>
            sb.Append(BalanceJson.Quote(key)).Append(':').Append(value ? "true" : "false");

        private static void Arr(StringBuilder sb, string key, float[] values)
        {
            sb.Append(BalanceJson.Quote(key)).Append(":[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Number(values[i]));
            }
            sb.Append(']');
        }

        /// <summary>JSON has no NaN or Infinity: a non-finite number is a bug upstream, so it is written as null rather than hidden as 0.</summary>
        private static string Number(float v) =>
            float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);

        // ------------------------------------------------------------ Markdown

        public string ToMarkdown()
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine("# Balance Oracle report");
            sb.AppendLine();
            sb.AppendLine("The numbers below come from simulated four-chicken matches on the shipped map, using the values in the game's data files.");
            sb.AppendLine("The oracle reports; it never blocks a change.");
            sb.AppendLine();
            sb.AppendLine($"- Balance in use: `{BalanceHash}`");
            sb.AppendLine($"- Map bake: `{MapFingerprint}`");
            sb.AppendLine($"- Match: {F0(WinTarget)} food wins, clock {F0(ClockSeconds)} s");
            sb.AppendLine($"- Starting food per pile: personal {F0(Food.Personal)}, contested {F0(Food.Contested)}, centre {F0(Food.Centre)}");
            sb.AppendLine();

            EconomySection(sb, "## Economy (no comeback event)", Economy,
                "Each row is one kit farming food while the other three chickens are plain Warriors with no abilities. "
                + $"Food is counted when the {F0(ClockSeconds)} s clock ends, with the {F0(WinTarget)}-food goal switched off so a fast kit is not cut short.");
            EconomySection(sb, "## Economy with a Golden Pile", EconomyGoldenPile,
                "The same runs, with the Golden Pile comeback event adding food to the centre pile near the end of the clock.");
            EconomySection(sb, "## Economy with a Restock", EconomyRestock,
                "The same runs, with the Restock comeback event refilling every pile near the end of the clock.");

            SpoilerSection(sb);
            ThiefSection(sb);

            sb.AppendLine("## What this report does not model");
            sb.AppendLine();
            foreach (var item in NotModelled) sb.AppendLine($"- {item}");
            return sb.ToString();
        }

        private void EconomySection(StringBuilder sb, string heading, List<KitLine> lines, string sentence)
        {
            sb.AppendLine(heading);
            sb.AppendLine();
            sb.AppendLine(sentence);
            sb.AppendLine("Best, average and spread are over every seat and route tried; Result is the best food plus the kit's offset in seconds at its own income per second.");
            sb.AppendLine();
            sb.AppendLine("| Kit | Best food | Best route | Average food | Spread | Fastest to goal (s) | Offset (s) | Result |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var l in lines)
            {
                if (!l.Applicable)
                {
                    sb.AppendLine($"| {l.Label} | n/a, does not forage | | | | | | |");
                    continue;
                }
                string goal = l.BestSeconds < 0f ? "not reached" : F1(l.BestSeconds);
                sb.AppendLine($"| {l.Label} | {F1(l.BestBanked)} | {RouteName(l.BestRoute)} | {F1(l.MeanBanked)} | {F1(l.Spread)} | {goal} | {F1(l.OffsetSeconds)} | {F1(l.Result)} |");
            }
            sb.AppendLine();
        }

        private void SpoilerSection(StringBuilder sb)
        {
            sb.AppendLine("## Spoiler: what it ends on");
            sb.AppendLine();
            sb.AppendLine($"Each row is the Spoiler after some executions of victims who each hold the cargo shown. A kill pays {F0(SpoilerBountyPerKill)} food plus the victim's cargo. "
                + $"The passive adds {F0(SpoilerPassiveBonus)} food when the clock runs out with nobody at {F0(WinTarget)}. "
                + $"The best farmer starts at {F1(StandardLeaderBanked)} food and loses cargo and {F1(SpoilerRemovalSeconds)} s of income for each kill that lands on it. "
                + "The Spoiler wins a row when it ends above the best farmer; with the passive it must also be that nobody reached the goal.");
            sb.AppendLine();
            sb.AppendLine("| Kills | Cargo per victim | Spoiler ends on | With the passive | Best farmer ends on | Wins | Wins with passive |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var r in Spoiler)
                sb.AppendLine($"| {r.Kills} | {F1(r.CargoPerKill)} | {F1(r.Final)} | {F1(r.FinalWithPassive)} | {F1(r.LeaderFinal)} | {YesNo(r.Wins)} | {YesNo(r.WinsWithPassive)} |");
            sb.AppendLine();
        }

        private void ThiefSection(StringBuilder sb)
        {
            sb.AppendLine("## Thief: steals needed");
            sb.AppendLine();
            sb.AppendLine("Each row is the Thief after some steals from victims who each hold the cargo shown (a steal takes the Thief's amount, or everything the victim has if that is less). "
                + "Time counts the steal cooldowns, one walk home from a contested pile for every trip its cargo capacity forces, and the time to bank the food. "
                + $"The goal is {F0(WinTarget)} food; the clock is {F0(ClockSeconds)} s.");
            sb.AppendLine();
            sb.AppendLine("| Steals | Cargo per victim | Food taken | Seconds needed | Fits the clock | Reaches the goal |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var r in Thief)
                sb.AppendLine($"| {r.Steals} | {F1(r.VictimCargo)} | {F1(r.Banked)} | {F1(r.SecondsNeeded)} | {YesNo(r.FitsTheClock)} | {YesNo(r.Banked >= WinTarget)} |");
            sb.AppendLine();
        }

        public static string RouteName(FarmRoute route)
        {
            switch (route)
            {
                case FarmRoute.Nearest: return "nearest pile";
                case FarmRoute.SidesFirst: return "side piles first";
                case FarmRoute.CentreFirst: return "centre first";
                case FarmRoute.EasySideThenCentre: return "easy side, then centre";
                default: return route.ToString();
            }
        }

        private static string F0(float v) => v.ToString("0", Inv);
        private static string F1(float v) => v.ToString("0.0", Inv);
        private static string YesNo(bool b) => b ? "yes" : "no";
    }
}
