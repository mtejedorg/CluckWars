using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A15 (architecture audit 2026-11-12): code outside <c>Scripts/Abilities</c> asks an ability what it does
    /// (<see cref="AbilityBaseSO.Job"/>, <see cref="AbilityBaseSO.GrantsStealProof"/>, ...), never which class it is.
    /// The old type checks are encoded here once: every trait must answer, for every ability asset and every concrete
    /// ability class, exactly what the <c>is XxxAbilitySO</c> it replaced answered.
    /// </summary>
    public sealed class AbilityTraitTests
    {
        /// <summary>Every shipped asset (the registered pool, the 19 retired ones, the passives) plus a blank instance of
        /// every concrete class, so a class with no asset is covered too.</summary>
        private static List<AbilityBaseSO> Everything(List<UnityEngine.Object> scratch)
        {
            var all = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            Assert.GreaterOrEqual(all.Count, 30, "the asset sweep went blind");
            foreach (var type in typeof(AbilityBaseSO).Assembly.GetTypes()
                         .Where(t => !t.IsAbstract && typeof(AbilityBaseSO).IsAssignableFrom(t)))
            {
                var blank = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                scratch.Add(blank);
                all.Add(blank);
            }
            return all;
        }

        private static void WithEverything(Action<AbilityBaseSO> check)
        {
            var scratch = new List<UnityEngine.Object>();
            try { foreach (var a in Everything(scratch)) check(a); }
            finally { foreach (var o in scratch) UnityEngine.Object.DestroyImmediate(o); }
        }

        /// <summary>The type check each job replaced, written once.</summary>
        private static readonly (AbilityJob Job, Type Old)[] JobWas =
        {
            (AbilityJob.Peck, typeof(PeckAbilitySO)),
            (AbilityJob.Steal, typeof(SnatchAbilitySO)),
            (AbilityJob.Stun, typeof(WingSlamAbilitySO)),
            (AbilityJob.Shove, typeof(CluckShockAbilitySO)),
            (AbilityJob.Slow, typeof(FeatherAuraAbilitySO)),
            (AbilityJob.Speed, typeof(SpeedBurstAbilitySO)),
            (AbilityJob.Jump, typeof(ShadowstepAbilitySO)),
            (AbilityJob.Shield, typeof(TurtleModeAbilitySO)),
            (AbilityJob.DeathMark, typeof(MarkKillAbilitySO)),
        };

        [Test]
        public void Job_AnswersExactlyAsTheTypeCheckItReplaced_ForEveryAbility()
        {
            WithEverything(a =>
            {
                foreach (var (job, old) in JobWas)
                    Assert.AreEqual(old.IsInstanceOfType(a), a.Is(job), $"{a.name} ({a.GetType().Name}): Is({job}) vs 'is {old.Name}'");
            });
            Assert.IsFalse(((AbilityBaseSO)null).Is(AbilityJob.Peck), "null is nothing, as 'null is PeckAbilitySO' was false");
            Assert.IsFalse(((AbilityBaseSO)null).Is(AbilityJob.None));
        }

        [Test]
        public void TheOtherTraits_AnswerExactlyAsTheirTypeChecksDid()
        {
            WithEverything(a =>
            {
                string who = $"{a.name} ({a.GetType().Name})";
                // BotTactics.IsStealShield
                Assert.AreEqual(a is EggShellAbilitySO || a is TurtleModeAbilitySO, a.GrantsStealProof, who + " GrantsStealProof");
                Assert.AreEqual(a.GrantsStealProof, BotTactics.IsStealShield(a), who);
                // BotTactics.IsStealer
                Assert.AreEqual(a.NominalStealAmount > 0f || a is MarkKillAbilitySO, BotTactics.IsStealer(a), who + " IsStealer");
                // HideLocalCue
                Assert.AreEqual(a is InvisibilityAbilitySO || a is SmokeRoostAbilitySO, a.IsStealth, who + " IsStealth");
                // the Shield's egg seconds (touch hex, shell visuals, status badge)
                Assert.AreEqual(a is TurtleModeAbilitySO t ? t.EggSeconds : 0f, a.FollowUpPhaseSeconds, 0f, who + " FollowUpPhaseSeconds");
                // the caster's own stun countdown read WingSlamAbilitySO.StunDuration
                if (a is WingSlamAbilitySO w) Assert.AreEqual(w.StunDuration, a.EmittedControl.Seconds, 0f, who);
                // the slow aura ring read FeatherAuraAbilitySO.AuraRadius
                if (a is FeatherAuraAbilitySO f) Assert.AreEqual(f.AuraRadius, SlowAuraRules.Radius(a), 0f, who);
                // StunBeatRules.WhiffArcsApply
                Assert.AreEqual(a is WingSlamAbilitySO, StunBeatRules.WhiffArcsApply(a), who + " WhiffArcsApply");
                // icons were keyed by the class name
                Assert.AreEqual(a.GetType().Name, a.IconId, who + " IconId");
                bool mapped = AbilityIconStyle.Entries.TryGetValue(a.GetType().Name, out string cls);
                Assert.AreEqual(mapped ? cls : null, AbilityIconStyle.ClassFor(a), who + " icon class");
            });
            Assert.IsFalse(BotTactics.IsStealShield(null));
            Assert.IsFalse(BotTactics.IsStealer(null));
        }

        [Test]
        public void TheShippedPool_HasOneAbilityPerJob()
        {
            var pool = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath).All
                .Where(a => a != null && !(a is PassiveAbilitySO)).ToList();
            foreach (AbilityJob job in Enum.GetValues(typeof(AbilityJob)))
            {
                if (job == AbilityJob.None) continue;
                var doing = pool.Where(a => a.Job == job).Select(a => a.name).ToList();
                // Bank is Quick Drop's job; Quick Drop is retired (it became the Featherfoot passive on 2026-11-05).
                Assert.AreEqual(job == AbilityJob.Bank ? 0 : 1, doing.Count, $"{job}: {string.Join(", ", doing)}");
            }
            Assert.IsEmpty(pool.Where(a => a.Job == AbilityJob.None).Select(a => a.name).ToList(),
                "a registered ability with no job: every job-keyed beat, bot rule and kit rule would skip it");
        }

        [Test]
        public void AbilityJob_ValuesArePinned()
        {
            var expected = new (AbilityJob, int)[]
            {
                (AbilityJob.None, 0), (AbilityJob.Peck, 1), (AbilityJob.Steal, 2), (AbilityJob.Stun, 3), (AbilityJob.Shove, 4),
                (AbilityJob.Slow, 5), (AbilityJob.Speed, 6), (AbilityJob.Jump, 7), (AbilityJob.Shield, 8), (AbilityJob.Hide, 9),
                (AbilityJob.Bank, 10), (AbilityJob.DeathMark, 11),
            };
            foreach (var (job, value) in expected) Assert.AreEqual(value, (int)job, job.ToString());
            Assert.AreEqual(expected.Length, Enum.GetValues(typeof(AbilityJob)).Length, "append only: pin the new value here");
        }

        // ---- the guard --------------------------------------------------------------------------------------

        /// <summary>
        /// The type checks that remain outside <c>Scripts/Abilities</c>, each with its reason. Key: file, value: the
        /// type names it may still name in an <c>is</c> / <c>as</c> / <c>typeof</c> / generic argument.
        /// </summary>
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            // The execution half of Death Mark: it reads six tuning fields of its own ability (kill range, arm,
            // window, removal, the two cooldowns). It is that ability's NetworkBehaviour, not a consumer of it.
            ["Assets/_Game/Scripts/Gameplay/AssassinExecute.cs"] = new[] { "MarkKillAbilitySO" },
            // Dev auto-test: the -cw_autozone / decoy scenarios pick two specific retired abilities to exercise, by
            // class, on purpose (they have no job and no player-facing consumer to share a trait with).
            ["Assets/_Game/Scripts/Diagnostics/AutoTestConfig.cs"] = new[] { "RootEggAbilitySO", "DoppelgangerAbilitySO" },
        };

        // PassiveAbilitySO is the abstract family of the class specializations, with its own API (ModifyStealAmount,
        // OutgoingControlMultiplier, Subclass): "is this a passive" is a question about the family, not about one ability.
        private static readonly string[] AbstractFamilies = { "AbilityBaseSO", "PassiveAbilitySO" };

        private static readonly Regex TypeCheck = new Regex(
            @"\b(?:is|as)\s+(?:not\s+)?(?:\w+\.)*(\w+AbilitySO)\b|typeof\(\s*(?:\w+\.)*(\w+AbilitySO)\s*\)|<\s*(?:\w+\.)*(\w+AbilitySO)\s*>\s*\(");

        [Test]
        public void NoFileOutsideTheAbilityFolder_AsksWhichClassAnAbilityIs()
        {
            var problems = new List<string>();
            int scanned = 0;
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles())
            {
                if (path.Contains("/Scripts/Abilities/")) continue;
                scanned++;
                string code = RuntimeSourceGuardTests.Code(path);
                foreach (Match m in TypeCheck.Matches(code))
                {
                    string type = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                    if (AbstractFamilies.Contains(type)) continue;
                    if (Allowed.TryGetValue(path, out var ok) && ok.Contains(type)) continue;
                    problems.Add($"{path}: '{m.Value.Trim()}'");
                }
                if (Regex.IsMatch(code, @"\b(ability|equipped|a|ab|slot\w*)\.GetType\(\)\.Name"))
                    problems.Add($"{path}: reads an ability's class name (use IconId, or a declared trait)");
            }
            Assert.Greater(scanned, 150);
            Assert.IsEmpty(problems,
                "Ask the ability what it does (AbilityBaseSO.Job / GrantsStealProof / IsStealth / EmittedControl / ...), " +
                "not which class it is (docs/CONVENTIONS.md, 'Abilities declare traits'):\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheGuard_SeesATypeCheck_AndTheAllowListIsStillNeeded()
        {
            Assert.IsTrue(TypeCheck.IsMatch("if (a is PeckAbilitySO) return;"));
            Assert.IsTrue(TypeCheck.IsMatch("var s = x as WingSlamAbilitySO;"));
            Assert.IsTrue(TypeCheck.IsMatch("if (a is not TurtleModeAbilitySO) return;"));
            Assert.IsTrue(TypeCheck.IsMatch("ActiveSlotOf<SpeedBurstAbilitySO>()"));
            Assert.IsTrue(TypeCheck.IsMatch("typeof(MarkKillAbilitySO)"));
            Assert.IsTrue(TypeCheck.IsMatch("picked.RemoveAll(a => a is Abilities.PeckAbilitySO)"), "a namespace-qualified name is still a type check");
            foreach (var kv in Allowed)
            {
                string code = RuntimeSourceGuardTests.Code(kv.Key);
                foreach (string type in kv.Value)
                    Assert.IsTrue(TypeCheck.Matches(code).Cast<Match>().Any(m => m.Value.Contains(type)),
                        $"{kv.Key} no longer names {type}: remove it from the allow-list");
            }
        }
    }
}
