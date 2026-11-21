using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CluckWars.Balance
{
    /// <summary>What one foraging kit does with one pile setup: the seat mean on its best route (same numbers as the economy table).</summary>
    public sealed class SweepKit
    {
        public string Label;
        public FarmRoute BestRoute;
        /// <summary>Food banked when the clock ends on the fastest route, mean over the four seats (supporting: capped by the food on the map).</summary>
        public float FoodAtClock;
        /// <summary>Seconds to the win target on the fastest route, mean over the four seats; a seat that misses the goal is extrapolated (<see cref="KitLine.SecondsBySeatRoute"/>). The headline.</summary>
        public float SecondsToGoal;
        /// <summary>How many of the four seats really reach the win target before the clock ends.</summary>
        public int SeatsReachingGoal;
    }

    /// <summary>One pile setup and what it does: the food rules, the four plain Warriors, when the map runs dry, and every foraging kit.</summary>
    public sealed class SweepRow
    {
        public OracleFood Food;
        public float TotalFood;
        public float WinTarget;
        public float ClockSeconds;
        /// <summary>Own pile plus one contested pile stays under the win target (<see cref="FoodRules"/>).</summary>
        public bool OwnPlusOneIsNotEnough;
        /// <summary>Own pile plus two contested piles reaches the win target (<see cref="FoodRules"/>).</summary>
        public bool OwnPlusTwoIsEnough;
        /// <summary>The lowest and highest seat of four standard farmers (goal off, no event).</summary>
        public float StandardMin;
        public float StandardMax;
        /// <summary>All four seats added up.</summary>
        public float StandardTotal;
        /// <summary>Seconds into the clock at which all piles were empty in that run; -1 when food was left at the end.</summary>
        public float PilesEmptyAt = -1f;
        public List<SweepKit> Kits = new List<SweepKit>();

        public bool RulesHold => OwnPlusOneIsNotEnough && OwnPlusTwoIsEnough;
    }

    /// <summary>
    /// Tries several pile setups through the oracle and tabulates what each does to every kit (read-only: the setups go in as
    /// <see cref="OracleInputs.FoodOverride"/>, no asset is changed). A setup that breaks the food rules is flagged, never dropped.
    /// Pure: no scene, no Fusion, no logging.
    /// </summary>
    public static class OracleSweep
    {
        /// <summary>The setups the menu sweeps: personal / contested / centre.</summary>
        public static readonly OracleFood[] DefaultCandidates =
        {
            Setup(5, 10, 20), Setup(10, 15, 20), Setup(10, 15, 30), Setup(10, 15, 40), Setup(8, 16, 30),
            Setup(8, 16, 40), Setup(5, 18, 30), Setup(5, 18, 40), Setup(10, 18, 30), Setup(10, 20, 30),
        };

        private static OracleFood Setup(float personal, float contested, float centre) =>
            new OracleFood { Personal = personal, Contested = contested, Centre = centre };

        public static List<SweepRow> Food(OracleInputs inputs, IReadOnlyList<OracleFood> candidates, IReadOnlyDictionary<string, float> offsets)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var rows = new List<SweepRow>(candidates.Count);
            foreach (var food in candidates)
            {
                var report = OracleRunner.Run(With(inputs, food), offsets);
                var seats = report.StandardSeatBanked;
                var row = new SweepRow
                {
                    Food = food,
                    TotalFood = food.Centre + OracleMapSO.SeatCount * (food.Personal + food.Contested),
                    WinTarget = report.WinTarget,
                    ClockSeconds = report.ClockSeconds,
                    OwnPlusOneIsNotEnough = FoodRules.OwnPlusOneIsNotEnough(food, report.WinTarget),
                    OwnPlusTwoIsEnough = FoodRules.OwnPlusTwoIsEnough(food, report.WinTarget),
                    StandardMin = seats.Min(),
                    StandardMax = seats.Max(),
                    StandardTotal = seats.Sum(),
                    PilesEmptyAt = report.StandardPilesEmptyAt,
                };
                foreach (var line in report.Economy.Where(l => l.Applicable))
                    row.Kits.Add(new SweepKit
                    {
                        Label = line.Label,
                        BestRoute = line.BestRoute,
                        FoodAtClock = line.BestBanked,
                        SecondsToGoal = line.BestSeconds,
                        SeatsReachingGoal = line.SeatsReachingGoal,
                    });
                rows.Add(row);
            }
            return rows;
        }

        private static OracleInputs With(OracleInputs inputs, OracleFood food) => new OracleInputs
        {
            Map = inputs.Map, Book = inputs.Book, Verdict = inputs.Verdict, Abilities = inputs.Abilities,
            Classes = inputs.Classes, Match = inputs.Match, FoodOverride = food,
        };

        // ------------------------------------------------------------ output

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The setup as "personal / contested / centre".</summary>
        public static string Name(OracleFood f) => $"{F0(f.Personal)} / {F0(f.Contested)} / {F0(f.Centre)}";

        public static string ToMarkdown(IReadOnlyList<SweepRow> rows, string balanceHash, string mapFingerprint)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("# Balance Oracle food sweep");
            sb.AppendLine();
            sb.AppendLine("Each row is one way to set the starting food of the piles, run through the same simulated four-chicken matches as the oracle report. Nothing in the game's data was changed.");
            sb.AppendLine();
            sb.AppendLine($"- Balance in use: `{balanceHash}`");
            sb.AppendLine($"- Map bake: `{mapFingerprint}`");
            if (rows.Count > 0) sb.AppendLine($"- Match: {F0(rows[0].WinTarget)} food wins, clock {F0(rows[0].ClockSeconds)} s");
            sb.AppendLine();
            sb.AppendLine("How to read the table: \"Piles\" is personal / contested / centre food per pile and \"Total\" is all the food on the map. "
                + "\"Food rules\" says whether your own pile plus one contested pile stays short of winning and your own pile plus two contested piles reaches it; a row that breaks them is kept and marked BREAKS. "
                + "\"Four plain Warriors\" is what the lowest and the highest seat bank when all four chickens are standard farmers, and \"Map empty at\" is the second the map runs out of food in that run. "
                + "Each kit column then gives, for that kit on its fastest route, the seconds to bank the win target (averaged over the four seats; a seat that misses it inside the clock is worked out from the food it banked) with how many of the four seats really reached it, and the food banked when the clock ends.");
            sb.AppendLine();

            var kits = rows.Count > 0 ? rows[0].Kits.Select(k => k.Label).ToList() : new List<string>();
            sb.Append("| Piles | Total | Food rules | Four plain Warriors | Map empty at |");
            foreach (var k in kits) sb.Append($" {k} |");
            sb.AppendLine();
            sb.Append("|---|---|---|---|---|");
            foreach (var _ in kits) sb.Append("---|");
            sb.AppendLine();
            foreach (var r in rows)
            {
                string dry = r.PilesEmptyAt < 0f ? "never" : $"{F1(r.PilesEmptyAt)} s";
                sb.Append($"| {Name(r.Food)} | {F0(r.TotalFood)} | {RulesText(r)} | {F1(r.StandardMin)} to {F1(r.StandardMax)} | {dry} |");
                foreach (var label in kits)
                {
                    var k = r.Kits.FirstOrDefault(x => x.Label == label);
                    sb.Append(k == null ? " |" : $" {KitText(k)} |");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private static string RulesText(SweepRow r)
        {
            if (r.RulesHold) return "both hold";
            var broken = new List<string>();
            if (!r.OwnPlusOneIsNotEnough) broken.Add("own pile plus one contested already wins");
            if (!r.OwnPlusTwoIsEnough) broken.Add("own pile plus two contested falls short");
            return "BREAKS: " + string.Join("; ", broken);
        }

        private static string KitText(SweepKit k) =>
            $"{F1(k.SecondsToGoal)} s to goal ({k.SeatsReachingGoal} of {OracleMapSO.SeatCount} seats reach it), {F1(k.FoodAtClock)} food";

        public static string ToJson(IReadOnlyList<SweepRow> rows, string balanceHash, string mapFingerprint)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"balanceHash\":").Append(BalanceJson.Quote(balanceHash ?? string.Empty));
            sb.Append(",\"mapFingerprint\":").Append(BalanceJson.Quote(mapFingerprint ?? string.Empty));
            sb.Append(",\"rows\":[");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"personal\":").Append(N(r.Food.Personal));
                sb.Append(",\"contested\":").Append(N(r.Food.Contested));
                sb.Append(",\"centre\":").Append(N(r.Food.Centre));
                sb.Append(",\"totalFood\":").Append(N(r.TotalFood));
                sb.Append(",\"ownPlusOneIsNotEnough\":").Append(r.OwnPlusOneIsNotEnough ? "true" : "false");
                sb.Append(",\"ownPlusTwoIsEnough\":").Append(r.OwnPlusTwoIsEnough ? "true" : "false");
                sb.Append(",\"standardMin\":").Append(N(r.StandardMin));
                sb.Append(",\"standardMax\":").Append(N(r.StandardMax));
                sb.Append(",\"pilesEmptyAtSeconds\":").Append(N(r.PilesEmptyAt));
                sb.Append(",\"kits\":[");
                for (int j = 0; j < r.Kits.Count; j++)
                {
                    var k = r.Kits[j];
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"label\":").Append(BalanceJson.Quote(k.Label));
                    sb.Append(",\"bestRoute\":").Append(BalanceJson.Quote(k.BestRoute.ToString()));
                    sb.Append(",\"foodAtClock\":").Append(N(k.FoodAtClock));
                    sb.Append(",\"secondsToGoal\":").Append(N(k.SecondsToGoal));
                    sb.Append(",\"seatsReachingGoal\":").Append(k.SeatsReachingGoal.ToString(Inv)).Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);
        private static string F0(float v) => v.ToString("0", Inv);
        private static string F1(float v) => v.ToString("0.0", Inv);
    }
}
