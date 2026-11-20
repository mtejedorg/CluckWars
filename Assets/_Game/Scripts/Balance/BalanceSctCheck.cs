using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>One foraging kit's seconds to the win target (lower is better) before and after a proposed delta.</summary>
    public readonly struct OracleCheckRow
    {
        public readonly string Label;
        public readonly float Before;
        public readonly float After;

        public OracleCheckRow(string label, float before, float after)
        {
            Label = label;
            Before = before;
            After = after;
        }

        public float Change => After - Before;
    }

    /// <summary>What <see cref="BalanceSctCheck.Run"/> measured for one proposed delta. It reports; nothing in it blocks anything.</summary>
    public sealed class OracleCheck
    {
        /// <summary>False when the delta touches no oracle input (class stats, specializations, Peck, MatchConfig): nothing was run.</summary>
        public bool Relevant;
        /// <summary>Why nothing ran (not relevant, nothing bound, or the run threw); null when both reports were made.</summary>
        public string Note;
        /// <summary>The oracle on the balance in force now.</summary>
        public OracleReport Before;
        /// <summary>The oracle on the proposed delta laid over the shipped assets.</summary>
        public OracleReport After;

        public bool Ran => Before != null && After != null;

        /// <summary>One row per kit that forages in both reports, in the report's order (the no-event economy table).</summary>
        public List<OracleCheckRow> Rows()
        {
            var rows = new List<OracleCheckRow>();
            if (!Ran) return rows;
            foreach (var before in Before.Economy)
            {
                if (!before.Applicable) continue;
                var after = After.Economy.Find(l => l.Label == before.Label);
                if (after == null || !after.Applicable) continue;
                rows.Add(new OracleCheckRow(before.Label, before.Result, after.Result));
            }
            return rows;
        }
    }

    /// <summary>
    /// The Balance Oracle asked of a proposed delta before it is applied locally: <see cref="OracleRunner"/> on the balance in
    /// force and again on copies that carry the proposed values over the shipped ones (<see cref="BalanceBook.Preview{T}"/>), so
    /// the live assets and the book's snapshots stay as they are. It reports the numbers and never blocks an apply. The map is
    /// the serialized <see cref="OracleMapSO"/> the installer hands to the book, so it runs in a player build too.
    /// </summary>
    public static class BalanceSctCheck
    {
        private static readonly IReadOnlyDictionary<string, float> NoOffsets = new Dictionary<string, float>();

        /// <summary>True when <paramref name="verdict"/> changes an input of the oracle's runs.</summary>
        public static bool Touches(BalanceVerdict verdict)
        {
            if (verdict == null) return false;
            foreach (var change in verdict.Assets)
                if (change.Asset is ChickenStatsSO || change.Asset is PassiveAbilitySO || (change.Asset is AbilityBaseSO ability && ability.Job == AbilityJob.Peck) || change.Asset is MatchConfigSO)
                    return true;
            return false;
        }

        public static OracleCheck Run(BalanceBook book, BalanceVerdict verdict, AbilityRegistrySO abilities, ChickenClassRegistrySO classes, MatchConfigSO match, OracleMapSO map)
        {
            var check = new OracleCheck { Relevant = Touches(verdict) };
            if (!check.Relevant) { check.Note = "no class stat, specialization, Peck or MatchConfig value changes"; return check; }
            if (abilities == null || classes == null || match == null) { check.Note = "the registries are not bound"; return check; }
            if (map == null) { check.Note = "no oracle map is bound (ProjectInstaller needs the OracleMap asset)"; return check; }

            try
            {
                var before = new OracleInputs { Map = map, Abilities = abilities, Classes = classes, Match = match };
                var after = new OracleInputs { Map = map, Book = book, Verdict = verdict, Abilities = abilities, Classes = classes, Match = match };
                check.Before = OracleRunner.Run(before, NoOffsets);
                check.After = OracleRunner.Run(after, NoOffsets);
            }
            catch (Exception e)
            {
                check.Before = null;
                check.After = null;
                check.Note = $"the oracle could not run: {e.GetType().Name}: {e.Message}";
            }
            return check;
        }
    }
}
