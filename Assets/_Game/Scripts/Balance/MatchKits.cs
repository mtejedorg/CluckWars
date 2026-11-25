using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Turns the shipped assets into the match simulator's kits: part 1's farming numbers plus every ability and passive hook as plain
    /// values. Every asset is read through the <see cref="OracleKits.CopyScope"/> of the inputs (the proposed values over the shipped
    /// ones), on the calling thread, and the copies are destroyed before a method returns, so the simulation never touches a
    /// ScriptableObject. Dispatch is on <see cref="AbilityJob"/>: no concrete ability type is named here.
    /// </summary>
    public static class MatchKits
    {
        /// <summary>One kit per shipped passive: the same kits, in the same order and with the same labels as <see cref="OracleKits.All"/>.</summary>
        public static MatchKit[] All(OracleInputs inputs)
        {
            var farms = OracleKits.All(inputs);   // checks the inputs
            var shipped = inputs.Abilities.Passives.Where(p => p != null).ToArray();
            if (shipped.Length != farms.Length)
                throw new InvalidOperationException($"The registry holds {shipped.Length} passives but OracleKits built {farms.Length} kits.");

            var kits = new MatchKit[farms.Length];
            using (var scope = new OracleKits.CopyScope(inputs))
            {
                var abilities = new AbilityBaseSO[farms.Length][];
                for (int i = 0; i < farms.Length; i++)
                    abilities[i] = OracleKits.ResolveKit(inputs, shipped[i]).Select(a => scope.Of(a)).ToArray();

                var slows = EmittedSlowFactors(abilities);
                for (int i = 0; i < farms.Length; i++)
                {
                    var passive = scope.Of(shipped[i]);
                    var cls = shipped[i].Subclass.ClassOf();
                    var kit = new MatchKit
                    {
                        Label = farms[i].Label,
                        Class = cls,
                        Farm = farms[i],
                        Abilities = new MatchAbility[abilities[i].Length],
                        Passive = ReadPassive(passive, slows),
                        Bot = BotTactics.ProfileFor(cls),
                    };
                    for (int slot = 0; slot < abilities[i].Length; slot++)
                        kit.Abilities[slot] = ReadAbility(kit.Label, abilities[i][slot], passive, farms[i]);
                    kits[i] = kit;
                }
            }
            return kits;
        }

        /// <summary>The rules of one match: the part 1 farm rules, the match config's combat numbers and the engine's named constants.</summary>
        public static MatchRules Rules(OracleInputs inputs, float arenaHalf, float sightArrivalRadius)
        {
            var farm = OracleKits.Rules(inputs, false, FarmFoodEvent.None);   // checks the inputs
            using (var scope = new OracleKits.CopyScope(inputs))
            {
                var match = scope.Of(inputs.Match);
                return new MatchRules
                {
                    Farm = farm,
                    ArenaHalf = arenaHalf,
                    ThinkSeconds = MatchRules.DefaultThinkSeconds,
                    KeepOutRadius = match.BaseKeepOutRadius,
                    ExecuteBounty = match.ExecuteBounty,
                    KnockbackDecayRate = ChickenMovement.KnockbackDecayRate,
                    SurgeSpeedFactor = ChickenMovement.UnderdogSurgeSpeedFactor,
                    SightArrivalRadius = sightArrivalRadius,
                    Comeback = new ComebackSettings
                    {
                        SecondsLeft = match.ComebackEventSecondsLeft,
                        GoldenPileFood = GameManager.GoldenPileFood,
                        RestockFood = GameManager.RestockFood,
                        GoldenRingMin = GameManager.GoldenPileRingMin,
                        GoldenRingMax = GameManager.GoldenPileRingMax,
                    },
                };
            }
        }

        /// <summary>
        /// A passive's hooks as factors: each is called with no owner and a unit input, then again at 2 to prove it is linear.
        /// <paramref name="emittedSlowFactors"/> are the slow strengths whose received value is recorded.
        /// </summary>
        public static MatchPassive ReadPassive(PassiveAbilitySO passive, float[] emittedSlowFactors)
        {
            if (passive == null) throw new ArgumentNullException(nameof(passive));
            string who = passive.name;
            // Read as a method group: CooldownsKitsSlipperyTests.EachNewHook_HasExactlyOneProductionCallSite counts direct calls, and the
            // game's one chokepoint is ChickenController.ApplyAbilitySlow. This is a read of the hook's answer, not a second game call site.
            Func<float, ChickenController, float> incomingSlow = passive.ModifyIncomingSlowFactor;
            var received = new float[emittedSlowFactors.Length];
            for (int i = 0; i < received.Length; i++) received[i] = incomingSlow(emittedSlowFactors[i], null);
            return new MatchPassive
            {
                Asset = who,
                StunInFactor = Factor(who, "ModifyControlDuration(Stun)", s => passive.ModifyControlDuration(s, ControlKind.Stun, null)),
                SlowInDurationFactor = Factor(who, "ModifyControlDuration(Slow)", s => passive.ModifyControlDuration(s, ControlKind.Slow, null)),
                OutgoingControlFactor = passive.OutgoingControlMultiplier,
                KnockbackFactor = Factor(who, "ModifyKnockback", s => passive.ModifyKnockback(s, null)),
                StealFactor = Factor(who, "ModifyStealAmount", s => passive.ModifyStealAmount(s, null)),
                MaxStealMultiplier = passive.MaxStealMultiplier,
                MatchEndBonusFood = passive.MatchEndBonusFood(null),
                Loot = passive.LootMark,
                HitSteal = passive.HitSteal,
                SlowFactorsEmitted = (float[])emittedSlowFactors.Clone(),
                SlowFactorsReceived = received,
            };
        }

        private static float Factor(string passive, string hook, Func<float, float> f)
        {
            float one = f(1f);
            float two = f(2f);
            if (Mathf.Abs(two - 2f * one) > 1e-4f * Mathf.Max(1f, Mathf.Abs(one)))
                throw new InvalidOperationException(
                    $"{passive}: {hook} is not linear (1 gives {one}, 2 gives {two}), so it cannot be read as one factor. The match simulator needs a linear hook.");
            return one;
        }

        private static MatchAbility ReadAbility(string label, AbilityBaseSO ability, PassiveAbilitySO passive, OracleKit farm)
        {
            if (ability == null) throw new InvalidOperationException($"{label}: the kit has an empty slot.");
            var job = ability.Job;
            switch (job)
            {
                case AbilityJob.Peck: case AbilityJob.Steal: case AbilityJob.Stun: case AbilityJob.Shove: case AbilityJob.Slow:
                case AbilityJob.Speed: case AbilityJob.Jump: case AbilityJob.Shield: case AbilityJob.Hide: case AbilityJob.DeathMark:
                case AbilityJob.SlowArea:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{label}: ability '{ability.name}' has job {job}, which the match simulator does not model (Bank and None have no simulated effect).");
            }

            var facts = ability.OracleFacts;
            bool isPeck = job == AbilityJob.Peck;
            var result = new MatchAbility
            {
                Job = job,
                Asset = ability.name,
                // The Peck's cooldown and cast are the class stats (PeckAbilitySO.ResolveCooldown / ResolveActiveDuration), already through the passive in the farm kit.
                Cooldown = isPeck ? farm.PeckCooldown : passive.ModifyCooldown(ability.Cooldown, ability, null),
                RoundStartCooldown = ability.RoundStartCooldown,
                Duration = isPeck ? farm.PeckCastSeconds : ability.Duration,
                Radius = ability.AimRadius,
                Control = ability.EmittedControl,
                Knockback = ability.EmittedKnockback,
                StealAmount = ability.NominalStealAmount,
                SpeedMultiplier = facts.SpeedMultiplier,
                HideOpacity = facts.HideOpacity,
                BlinkMetres = facts.BlinkMetres,
                ShieldWalkSeconds = facts.ShieldWalkSeconds,
                ShieldWalkSpeedMultiplier = facts.ShieldWalkSpeedMultiplier,
                ShieldEggSeconds = facts.ShieldEggSeconds,
                StealProof = ability.GrantsStealProof,
                EndsOnNextMove = ability.EndsOnNextMove,
                MarkArmSeconds = facts.MarkArmSeconds,
                MarkWindowSeconds = facts.MarkWindowSeconds,
                MarkKillRange = facts.MarkKillRange,
                MarkSuccessCooldown = facts.MarkSuccessCooldown,
                MarkFailCooldown = facts.MarkFailCooldown,
                MarkRemovalSeconds = facts.MarkRemovalSeconds,
                MarkRoundStartCooldown = facts.MarkRoundStartCooldown,
                MarkLeaderPickWeight = facts.MarkLeaderPickWeight,
                Role = ability.ResolveBotRole(),
                Reach = BotTactics.EffectiveReach(ability),
                NeedsEnemyInRange = ability.RequiresEnemyInRange,
                Category = ability.Category,
                IsStealer = BotTactics.IsStealer(ability),
                IsStealShield = BotTactics.IsStealShield(ability),
            };
            if (ability.TryGetAreaEffect(out var area))
            {
                result.AreaAura = area.Kind == AreaEffectKind.Aura;
                result.AreaRadius = area.Radius;
                result.AreaSlowFactor = area.SlowFactor;
            }
            return result;
        }

        /// <summary>Every distinct slow strength the kits emit, instant or as an aura, smallest first.</summary>
        private static float[] EmittedSlowFactors(AbilityBaseSO[][] kits)
        {
            var found = new SortedSet<float>();
            foreach (var kit in kits)
                foreach (var ability in kit)
                {
                    if (ability == null) continue;
                    var control = ability.EmittedControl;
                    if (control.Kind == ControlKind.Slow) found.Add(control.SlowFactor);
                    if (ability.TryGetAreaEffect(out var area) && area.Kind == AreaEffectKind.Aura && area.SlowFactor < 1f) found.Add(area.SlowFactor);
                }
            return found.ToArray();
        }
    }
}
