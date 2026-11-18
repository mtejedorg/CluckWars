using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>One farming run of the SCT check: a class (naked) or a class with a specialization.</summary>
    public sealed class SctLine
    {
        public string Label;
        public bool Ok;
        /// <summary>The seat that was furthest from the target (the failing one when <see cref="Ok"/> is false), 0 to 3.</summary>
        public int Seat;
        public float Seconds;
        public int Trips;
        public float TargetSeconds;
        public int TargetTrips;
        public bool Reached;
        /// <summary>Why the run could not be made (no stats for the class); null for a real run.</summary>
        public string Problem;
    }

    /// <summary>What <see cref="BalanceSctCheck.Run"/> found for one proposed delta.</summary>
    public sealed class SctReport
    {
        /// <summary>False when the delta touches no SCT input (class stats, specializations, Peck, MatchConfig): nothing was run.</summary>
        public bool Relevant;
        /// <summary>True when the shipped map was available and the runs were made.</summary>
        public bool Available;
        /// <summary>Why nothing ran or the map is unavailable (null when the runs were made).</summary>
        public string Note;
        public readonly List<SctLine> Lines = new List<SctLine>();

        /// <summary>
        /// True when the proposal may be applied: it touches no SCT input, or every run landed inside the shipped
        /// ±<see cref="SctTargets.ShippedAssetToleranceSeconds"/> band. A relevant proposal whose check could not run does NOT pass.
        /// </summary>
        public bool Passed
        {
            get
            {
                if (!Relevant) return true;
                if (!Available || Lines.Count == 0) return false;
                foreach (var line in Lines) if (!line.Ok) return false;
                return true;
            }
        }
    }

    /// <summary>
    /// The SCT axiom (CODE-DESIGN-01, -02) asked of a proposed delta before it is applied locally: the same two checks
    /// <c>BalanceOracleTests</c> runs on the shipped assets (each governed class naked, and each shipped specialization
    /// through its real hooks, on all four seats, inside the ±0.5 s band), but on COPIES of the assets that carry the proposed
    /// values over the shipped ones (<see cref="BalanceBook.Preview{T}"/>), so the live assets and the book's snapshots stay
    /// as they are and nothing needs restoring.
    /// </summary>
    /// <remarks>
    /// The map (arena geometry, pile footprints, deposit radius, peck reach) is read from the Game scene and two prefabs, which
    /// a player build cannot read; <see cref="MapProvider"/> is the seam. The Editor registers one
    /// (<c>OracleMapHook</c>, test assembly); a player has none, so a proposal that touches an SCT input is blocked there with
    /// the reason shown, until the map is baked into a runtime asset (reported as a follow-up).
    /// </remarks>
    public static class BalanceSctCheck
    {
        public const int Seats = 4;

        /// <summary>Returns the shipped map as seat 0 to 3 sees it, or throws. Null in a player build.</summary>
        // ARCH-DI-12 exemption: Editor-registered delegate (OracleMapHook), not scene state; it is re-registered on every domain load and play-mode entry, and null in a player.
        public static Func<int, OracleMap> MapProvider;

        /// <summary>True when <paramref name="verdict"/> changes an input of the SCT runs.</summary>
        public static bool Touches(BalanceVerdict verdict)
        {
            if (verdict == null) return false;
            foreach (var change in verdict.Assets)
                if (change.Asset is ChickenStatsSO || change.Asset is PassiveAbilitySO || (change.Asset is AbilityBaseSO ability && ability.Job == AbilityJob.Peck) || change.Asset is MatchConfigSO)
                    return true;
            return false;
        }

        public static SctReport Run(BalanceBook book, BalanceVerdict verdict, AbilityRegistrySO abilities, ChickenClassRegistrySO classes, MatchConfigSO match)
        {
            var report = new SctReport { Relevant = Touches(verdict) };
            if (!report.Relevant) { report.Note = "no class stat, specialization, Peck or MatchConfig value changes"; return report; }
            if (abilities == null || classes == null || match == null) { report.Note = "the registries are not bound"; return report; }

            var maps = new OracleMap[Seats];
            var provider = MapProvider;
            if (provider == null) { report.Note = "no map data in this build (the shipped arena is readable only in the Editor)"; return report; }
            try
            {
                for (int seat = 0; seat < Seats; seat++) maps[seat] = provider(seat);
            }
            catch (Exception e)
            {
                report.Note = $"the shipped map could not be read: {e.GetType().Name}: {e.Message}";
                return report;
            }
            report.Available = true;

            var copies = new List<ScriptableObject>();
            try
            {
                T Copy<T>(T so) where T : ScriptableObject
                {
                    var c = book.Preview(so, verdict);
                    copies.Add(c);
                    return c;
                }

                var config = Copy(match);
                var stats = new List<ChickenStatsSO>();
                foreach (ChickenClass cls in Enum.GetValues(typeof(ChickenClass)))
                    if (classes.TryGet(cls, out var entry) && entry.Stats != null) stats.Add(Copy(entry.Stats));

                PeckAbilitySO peck = null;
                if (abilities.All != null)
                    foreach (var a in abilities.All)
                        if (a != null && a.Job == AbilityJob.Peck) { peck = Copy((PeckAbilitySO)a); break; }

                foreach (var target in SctTargets.All)
                {
                    var s = stats.Find(x => x.DisplayName == target.ClassName);
                    if (s == null) { report.Lines.Add(Missing(target.ClassName, $"no class stats named {target.ClassName}")); continue; }
                    var chicken = new OracleChicken
                    {
                        MoveSpeed = s.MoveSpeed,
                        CargoCapacity = s.CargoCapacity,
                        PeckAmount = s.PeckAmount,
                        PeckCooldown = s.PeckCooldown,
                        DepositRate = config.DepositRatePerSecond,
                    };
                    report.Lines.Add(RunSeats(target.ClassName, maps, chicken, config.FoodTargetToWin, target.Seconds, target.Trips));
                }

                if (abilities.Passives != null)
                    foreach (var shippedPassive in abilities.Passives)
                    {
                        string className = shippedPassive.Subclass.ClassOf().ToString();
                        if (SctTargets.IsExempt(className)) continue;
                        var classTarget = Array.Find(SctTargets.All, t => t.ClassName == className);
                        string label = $"{className}+{shippedPassive.name}";
                        var s = stats.Find(x => x.DisplayName == className);
                        if (s == null || classTarget.ClassName == null || peck == null)
                        {
                            report.Lines.Add(Missing(label, "no class stats, SCT target or Peck to run it against"));
                            continue;
                        }

                        var passive = Copy(shippedPassive);
                        var chicken = new OracleChicken
                        {
                            MoveSpeed = s.MoveSpeed,
                            CargoCapacity = Mathf.RoundToInt(passive.ModifyCargoCapacity(s.CargoCapacity, null)),
                            PeckAmount = s.PeckAmount,
                            PeckCooldown = passive.ModifyCooldown(s.PeckCooldown, peck, null),
                            DepositRate = passive.ModifyDepositRate(config.DepositRatePerSecond, null),
                        };

                        // A declared exception REPLACES the class target (SpecSctTargets), as in BalanceOracleTests.
                        bool declared = SpecSctTargets.TryGet(shippedPassive.Subclass, out var spec);
                        if (declared && spec.CargoCapacity != chicken.CargoCapacity)
                        {
                            var line = RunSeats(label, maps, chicken, config.FoodTargetToWin, spec.Seconds, spec.Trips);
                            line.Ok = false;
                            line.Problem = $"carries {chicken.CargoCapacity}, its declared SCT entry was measured at {spec.CargoCapacity}";
                            report.Lines.Add(line);
                            continue;
                        }
                        report.Lines.Add(declared
                            ? RunSeats(label, maps, chicken, config.FoodTargetToWin, spec.Seconds, spec.Trips)
                            : RunSeats(label, maps, chicken, config.FoodTargetToWin, classTarget.Seconds, classTarget.Trips));
                    }
            }
            finally
            {
                foreach (var c in copies)
                {
                    if (c == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(c);
                    else UnityEngine.Object.DestroyImmediate(c);
                }
            }
            return report;
        }

        private static SctLine Missing(string label, string problem) => new SctLine { Label = label, Ok = false, Problem = problem };

        /// <summary>Runs the four seats; the line is the worst one (a failing seat first, then the seat furthest from the target).</summary>
        private static SctLine RunSeats(string label, OracleMap[] maps, OracleChicken chicken, float winTarget, float targetSeconds, int targetTrips)
        {
            var target = new SctTarget { ClassName = label, Seconds = targetSeconds, Trips = targetTrips };
            SctLine pick = null;
            float pickGap = -1f;
            bool pickOk = true;
            bool anyFail = false;
            for (int seat = 0; seat < maps.Length; seat++)
            {
                var result = BalanceOracle.Simulate(maps[seat], chicken, winTarget);
                bool ok = SctTargets.Within(result, target, SctTargets.ShippedAssetToleranceSeconds);
                float gap = result.ReachedTarget ? Mathf.Abs(result.SctSeconds - targetSeconds) : float.MaxValue;
                if (!ok) anyFail = true;
                if (pick == null || (!ok && pickOk) || (ok == pickOk && gap > pickGap))
                {
                    pick = new SctLine
                    {
                        Label = label, Seat = seat, Seconds = result.SctSeconds, Trips = result.Trips,
                        TargetSeconds = targetSeconds, TargetTrips = targetTrips, Reached = result.ReachedTarget,
                    };
                    pickGap = gap;
                    pickOk = ok;
                }
            }
            pick.Ok = !anyFail;
            return pick;
        }
    }
}
