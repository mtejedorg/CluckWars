using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Everything the oracle reads to turn the shipped assets into kits. With a <see cref="Verdict"/> every asset is read
    /// from a <see cref="BalanceBook.Preview{T}"/> copy (the proposed values over the shipped ones), so the live assets are
    /// never touched; <see cref="OracleKits"/> owns those copies and destroys them before it returns.
    /// </summary>
    public sealed class OracleInputs
    {
        public OracleMapSO Map;
        /// <summary>Needed only to read a <see cref="Verdict"/>; null reads the assets as they are.</summary>
        public BalanceBook Book;
        public BalanceVerdict Verdict;
        public AbilityRegistrySO Abilities;
        public ChickenClassRegistrySO Classes;
        public MatchConfigSO Match;
        /// <summary>Pile food to use instead of the amounts in <see cref="Match"/> (what a test or a proposal sets).</summary>
        public OracleFood? FoodOverride;
        /// <summary>
        /// The Peck cast per class (key = <see cref="ChickenClass"/> name), seconds; oracle-only, because the game has no Peck cast yet.
        /// Null or a missing class reads 0: the food comes at the press, as in today's game (the post-peck movement lock still applies,
        /// see <see cref="OracleKit.PeckLockSeconds"/>).
        /// </summary>
        public IReadOnlyDictionary<string, float> PeckCastSeconds;
    }

    /// <summary>
    /// The farming kits of the shipped roster, resolved to numbers for <see cref="FarmSim"/>. A kit is the passive's fixed
    /// loadout (<see cref="FixedKit"/>, the one resolver the spawner uses), its class stats and its passive hooks called
    /// with no owner, exactly as <c>BalanceSctCheck</c> does. Pure: no scene, no Fusion, no logging.
    /// </summary>
    public static class OracleKits
    {
        public const string StandardLabel = "Standard";

        /// <summary>One kit per shipped passive, labelled like its <see cref="ChickenSubclass"/> (<c>Fatty_Hoarder</c>).</summary>
        public static OracleKit[] All(OracleInputs inputs)
        {
            Require(inputs, needMap: false, needAbilities: true);
            var kits = new List<OracleKit>();
            using (var scope = new CopyScope(inputs))
            {
                var match = scope.Of(inputs.Match);
                var peck = FindPeck(inputs.Abilities);
                foreach (var shipped in inputs.Abilities.Passives)
                {
                    if (shipped == null) continue;
                    var kit = ResolveKit(inputs, shipped);
                    kits.Add(Build(scope, inputs, shipped.Subclass.ToString(), shipped.Subclass.ClassOf(), scope.Of(shipped), kit, match, peck));
                }
            }
            return kits.ToArray();
        }

        /// <summary>The naked Warrior: its class stats, no passive, no abilities beyond the Peck, no burst, no jump.</summary>
        public static OracleKit StandardFarmer(OracleInputs inputs)
        {
            Require(inputs, needMap: false, needAbilities: true);
            using (var scope = new CopyScope(inputs))
                return Build(scope, inputs, StandardLabel, ChickenClass.Warrior, null, Array.Empty<AbilityBaseSO>(), scope.Of(inputs.Match), FindPeck(inputs.Abilities));
        }

        /// <summary>Starting food per pile kind: the override when set, else the amounts in the match config (as the verdict would have them).</summary>
        public static OracleFood Food(OracleInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.FoodOverride.HasValue) return inputs.FoodOverride.Value;
            Require(inputs, needMap: false, needAbilities: false);
            using (var scope = new CopyScope(inputs))
            {
                var match = scope.Of(inputs.Match);
                return new OracleFood { Personal = match.PersonalPileFood, Contested = match.ContestedPileFood, Centre = match.CentrePileFood };
            }
        }

        /// <summary>The match rules of one run: win target, clock and event window from the match config, the event amounts and pile slow from the game's own constants.</summary>
        public static FarmRules Rules(OracleInputs inputs, bool stopAtTarget, FarmFoodEvent foodEvent)
        {
            Require(inputs, needMap: false, needAbilities: false);
            using (var scope = new CopyScope(inputs))
            {
                var match = scope.Of(inputs.Match);
                return new FarmRules
                {
                    WinTarget = match.FoodTargetToWin,
                    ClockSeconds = match.MatchDurationSeconds,
                    StopAtTarget = stopAtTarget,
                    TickSeconds = FarmRules.DefaultTickSeconds,
                    Event = foodEvent,
                    EventSecondsLeft = match.ComebackEventSecondsLeft,
                    GoldenPileFood = GameManager.GoldenPileFood,
                    RestockFood = GameManager.RestockFood,
                    PileSlowFactor = ChickenController.PileSlowFactor,
                };
            }
        }

        private static void Require(OracleInputs inputs, bool needMap, bool needAbilities)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Match == null) throw new ArgumentException("OracleInputs.Match is required.", nameof(inputs));
            if (needAbilities && (inputs.Abilities == null || inputs.Classes == null))
                throw new ArgumentException("OracleInputs.Abilities and OracleInputs.Classes are required.", nameof(inputs));
            if (needMap && inputs.Map == null) throw new ArgumentException("OracleInputs.Map is required.", nameof(inputs));
        }

        private static PeckAbilitySO FindPeck(AbilityRegistrySO abilities)
        {
            if (abilities.All != null)
                foreach (var a in abilities.All)
                    if (a != null && a.Job == AbilityJob.Peck) return (PeckAbilitySO)a;
            throw new InvalidOperationException("The ability registry holds no Peck ability.");
        }

        private static float PeckCastOf(OracleInputs inputs, ChickenClass cls) =>
            inputs.PeckCastSeconds != null && inputs.PeckCastSeconds.TryGetValue(cls.ToString(), out float seconds) ? seconds : 0f;

        /// <summary>The passive's four abilities: a kit the verdict proposes, else the one <see cref="FixedKit"/> resolves. A malformed kit is an error, never a silent fallback.</summary>
        private static AbilityBaseSO[] ResolveKit(OracleInputs inputs, PassiveAbilitySO shipped)
        {
            if (inputs.Verdict != null)
                foreach (var change in inputs.Verdict.Kits)
                    if (change.Passive == shipped) return change.Kit;
            if (FixedKit.TryResolve(LoadoutMode.Fixed, shipped, out var kit, out var problem)) return kit;
            throw new InvalidOperationException($"{shipped.name}: the fixed kit is not legal, so the oracle cannot build it: {problem}");
        }

        private static OracleKit Build(CopyScope scope, OracleInputs inputs, string label, ChickenClass cls,
            PassiveAbilitySO passive, AbilityBaseSO[] kit, MatchConfigSO match, PeckAbilitySO peck)
        {
            if (!inputs.Classes.TryGet(cls, out var entry) || entry.Stats == null)
                throw new InvalidOperationException($"{label}: the class registry has no stats for {cls}.");
            var stats = scope.Of(entry.Stats);

            float cargo = stats.CargoCapacity;
            float peckCooldown = stats.PeckCooldown;
            float deposit = match.DepositRatePerSecond;
            bool ignoresSlow = false;
            if (passive != null)
            {
                cargo = passive.ModifyCargoCapacity(cargo, null);
                peckCooldown = passive.ModifyCooldown(peckCooldown, peck, null);
                deposit = passive.ModifyDepositRate(deposit, null);
                ignoresSlow = passive.IgnoresPileSlow(null);
            }

            var result = new OracleKit
            {
                Label = label,
                Forages = ChickenClasses.MayForage(cls),
                MoveSpeed = stats.MoveSpeed,
                CargoCapacity = Mathf.RoundToInt(cargo),
                PeckAmount = stats.PeckAmount,
                PeckCooldown = peckCooldown,
                PeckCastSeconds = PeckCastOf(inputs, cls),
                PeckLockSeconds = scope.Of(peck).Duration,
                DepositRate = deposit,
                IgnoresPileSlow = ignoresSlow,
            };

            foreach (var ability in kit)
            {
                if (ability == null) continue;
                if (ability.Job == AbilityJob.Jump) result.Jumps = true;
                if (ability.Job != AbilityJob.Speed) continue;
                var burst = scope.Of(ability);
                var facts = burst.OracleFacts;
                result.BurstMultiplier = facts.SpeedMultiplier;
                result.BurstSeconds = facts.SpeedSeconds;
                result.BurstCooldown = passive != null ? passive.ModifyCooldown(burst.Cooldown, burst, null) : burst.Cooldown;
            }
            return result;
        }

        /// <summary>Owns the <see cref="BalanceBook.Preview{T}"/> copies of one build and destroys them on dispose (as <c>BalanceSctCheck</c> does).</summary>
        private sealed class CopyScope : IDisposable
        {
            private readonly OracleInputs _inputs;
            private readonly List<ScriptableObject> _copies = new List<ScriptableObject>();

            public CopyScope(OracleInputs inputs) { _inputs = inputs; }

            /// <summary>The asset as the verdict would have it; the asset itself when there is nothing to apply.</summary>
            public T Of<T>(T asset) where T : ScriptableObject
            {
                if (asset == null || _inputs.Book == null || _inputs.Verdict == null || _inputs.Verdict.IsEmpty) return asset;
                var copy = _inputs.Book.Preview(asset, _inputs.Verdict);
                _copies.Add(copy);
                return copy;
            }

            public void Dispose()
            {
                foreach (var copy in _copies)
                {
                    if (copy == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
                    else UnityEngine.Object.DestroyImmediate(copy);
                }
                _copies.Clear();
            }
        }
    }
}
