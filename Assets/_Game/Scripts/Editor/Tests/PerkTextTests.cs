using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the templated perk lines: the words a player reads for each specialization must
    /// carry the numbers the passive's own fields hold. Expected text is built here from the
    /// SPEC's English templates and numbers computed independently from the asset fields (in
    /// double arithmetic, away-from-zero rounding), never read back from the code under test.
    /// </summary>
    public sealed class PerkTextTests
    {
        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        private static int Pct(double fraction) => (int)Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero);

        /// <summary>(line, detail) the spec's wording table gives for this passive's CURRENT field values.</summary>
        private static (string line, string detail) Expected(PassiveAbilitySO p, MatchConfigSO cfg)
        {
            switch (p)
            {
                case RelentlessPassiveSO r:
                    return ($"Moves recharge {Pct(1.0 - r.CooldownMultiplier)}% faster.",
                            "Applies to every move except Peck.");
                case BullyPassiveSO b:
                    string bully = $"Steal {Pct(b.StealMultiplier - 1.0)}% more, carry {(int)Math.Round(b.BonusCapacity, MidpointRounding.AwayFromZero)} more.";
                    return (bully, bully);
                case ThiefPassiveSO t:
                    string thief = $"Every steal grabs {Pct(t.StealMultiplier - 1.0)}% more.";
                    return (thief, thief);
                case SlipperyPassiveSO s:
                    return ($"Stuns, getting stuck and timed slows end {Pct(1.0 - s.DurationMultiplier)}% sooner.",
                            "Zone, aura, pile and bump slows still apply in full.");
                case BulwarkPassiveSO w:
                    return ("Barely budges when shoved.",
                            $"Knockback cut by {Pct(1.0 - w.KnockbackMultiplier)}%. Stuns, getting stuck and timed slows end {Pct(1.0 - w.DurationMultiplier)}% sooner.");
                case HoarderPassiveSO h:
                    int cap = (int)Math.Round(h.MinimumCapacity, MidpointRounding.AwayFromZero);
                    return ($"Carry {cap} food in one trip.", $"Carries at least {cap} food.");
                case FeatherfootPassiveSO _:
                    return ("Piles and bumps never slow you.", "Food piles and bumping into rivals never slow you down.");
                case SpoilerPassiveSO sp:
                    return ($"+{sp.BonusFood} food if time runs out.",
                            $"If nobody reaches {cfg.FoodTargetToWin} before time's up, you bank {sp.BonusFood} bonus food first.");
                default:
                    Assert.Fail($"{p.name} ({p.GetType().Name}) has no perk-text expectation. A new passive needs " +
                                "a perk.*.line template in UiText.csv, PerkLineKey/PerkArgs on the class, and a row here.");
                    return (null, null);
            }
        }

        private static PassiveAbilitySO[] ShippedPassives(out MatchConfigSO cfg)
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var all = registry.Passives.ToArray();
            Assert.AreEqual(8, all.Length, "Expected the 8 shipped specializations in the registry.");
            return all;
        }

        /// <summary>
        /// ModifyControlDuration only shortens TIMED effects (stuns, roots, Dust Kick's slow).
        /// Presence slows (zones, Feather Aura, the pile and collision slows) have no duration and
        /// get no reduction (balance proposal 2026-10 §1.1), so "Slows ... end N% sooner" promised
        /// coverage of one slow in five. Any slow claim must say "timed slows".
        /// </summary>
        private static void AssertNoBlanketSlowClaim(string text, string who)
        {
            string lower = text.ToLowerInvariant();
            Assert.IsFalse(lower.StartsWith("slows") || lower.Contains(" slows, stuns") || lower.Contains("covers slows"),
                $"{who}: '{text}' claims every slow is shortened; only timed slows are.");
            if (lower.Contains("slows") && lower.Contains("sooner"))
                StringAssert.Contains("timed slows", lower, $"{who}: a 'sooner' claim about slows must say 'timed slows'.");
        }

        [Test]
        public void ControlDurationPerks_DoNotOverstateSlowCoverage()
        {
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is SlipperyPassiveSO) && !(p is BulwarkPassiveSO)) continue;
                AssertNoBlanketSlowClaim(p.PerkLine(cfg), p.name + " line");
                AssertNoBlanketSlowClaim(p.PerkDetail(cfg), p.name + " detail");
                AssertNoBlanketSlowClaim(p.Description, p.name + " description");
            }
        }

        [Test]
        public void EveryShippedPassive_HasAPerkLine_WithNoUnresolvedPlaceholder()
        {
            foreach (var p in ShippedPassives(out var cfg))
            {
                string line = p.PerkLine(cfg), detail = p.PerkDetail(cfg);
                foreach (var text in new[] { line, detail })
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{p.name} has an empty perk text.");
                    Assert.IsFalse(text.Contains("{") || text.Contains("}"), $"{p.name} left a placeholder in: {text}");
                    Assert.IsFalse(text.Contains("#"), $"{p.name} rendered a missing-key/arg marker: {text}");
                }
                Assert.IsNotNull(p.PerkLineKey, $"{p.name} has no PerkLineKey, so the menu would fall back to its Description.");
            }
            Assert.IsEmpty(UiText.Problems, string.Join("\n", UiText.Problems));
        }

        [Test]
        public void PerkLines_CarryTheNumbersTheAssetFieldsHold()
        {
            foreach (var p in ShippedPassives(out var cfg))
            {
                var (line, detail) = Expected(p, cfg);
                Assert.AreEqual(line, p.PerkLine(cfg), $"{p.name}: perk line disagrees with its fields.");
                Assert.AreEqual(detail, p.PerkDetail(cfg), $"{p.name}: perk detail disagrees with its fields.");
            }
        }

        [Test]
        public void ChangingAField_ChangesTheText_WithoutTouchingAnyWording()
        {
            // The point of templating: a balance edit can never leave stale prose behind.
            var relentless = ScriptableObject.CreateInstance<RelentlessPassiveSO>();
            var bulwark = ScriptableObject.CreateInstance<BulwarkPassiveSO>();
            var hoarder = ScriptableObject.CreateInstance<HoarderPassiveSO>();
            try
            {
                relentless.CooldownMultiplier = 0.5f;
                bulwark.DurationMultiplier = 0.5f;
                bulwark.KnockbackMultiplier = 0.1f;
                hoarder.MinimumCapacity = 55f;

                Assert.AreEqual("Moves recharge 50% faster.", relentless.PerkLine());
                Assert.AreEqual("Knockback cut by 90%. Stuns, getting stuck and timed slows end 50% sooner.", bulwark.PerkDetail());
                Assert.AreEqual("Carry 55 food in one trip.", hoarder.PerkLine(),
                    "The Hoarder line must carry {cap} from MinimumCapacity, not a fixed phrase.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(relentless);
                UnityEngine.Object.DestroyImmediate(bulwark);
                UnityEngine.Object.DestroyImmediate(hoarder);
            }
        }

        [Test]
        public void SpoilerAsset_Description_IsTheDetailTemplate_WithTheCurrentNumbers()
        {
            // Round-3 finding 21: the asset said "nobody wins" while the template said "nobody reaches {goal}".
            // Until the descriptions are generated (chunk 7c), this pins the stored text to the template.
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is SpoilerPassiveSO sp)) continue;
                Assert.AreEqual($"If nobody reaches {cfg.FoodTargetToWin} before time's up, you bank {sp.BonusFood} bonus food first.",
                    sp.Description);
                return;
            }
            Assert.Fail("No Spoiler in the ability registry.");
        }

        [Test]
        public void RelentlessAsset_Description_IsTheLinePlusTheDetail_WithTheCurrentNumbers()
        {
            // Round-4 finding 18: the asset said "Every move except Peck recharges 25% faster." while the menu line said
            // "Moves recharge 25% faster." Pinned like Spoiler: the stored text is the two templates, joined.
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is RelentlessPassiveSO r)) continue;
                Assert.AreEqual($"Moves recharge {Pct(1.0 - r.CooldownMultiplier)}% faster. Applies to every move except Peck.", r.Description);
                Assert.AreEqual(r.PerkLine(cfg) + " " + r.PerkDetail(cfg), r.Description);
                return;
            }
            Assert.Fail("No Relentless in the ability registry.");
        }

        [Test]
        public void SpoilerDetail_WithoutAMatchConfig_IsLoudNotBlank()
        {
            var spoiler = ScriptableObject.CreateInstance<SpoilerPassiveSO>();
            try
            {
                StringAssert.Contains("#goal#", spoiler.PerkDetail(null),
                    "With no MatchConfig, {goal} must render as a visible marker, not vanish.");
                Assert.IsNotEmpty(UiText.Problems, "The missing {goal} argument must be reported.");
            }
            finally { UnityEngine.Object.DestroyImmediate(spoiler); }
        }

        [Test]
        public void PassiveDescriptions_ThatQuoteNumbers_QuoteTheCurrentOnes()
        {
            // Relentless, Bulwark and Slippery descriptions were inaccurate ("Combat abilities",
            // "shrugs off", "far faster"); Bully, Thief, Hoarder and Spoiler were vague ("noticeably
            // more", "bonus food"). They now state the real numbers. Pin those numbers to the fields
            // so a later balance edit cannot leave the asset's own description stale.
            foreach (var p in ShippedPassives(out _))
            {
                switch (p)
                {
                    case RelentlessPassiveSO r:
                        StringAssert.Contains($"{Pct(1.0 - r.CooldownMultiplier)}%", r.Description);
                        StringAssert.Contains("except Peck", r.Description);
                        break;
                    case SlipperyPassiveSO s:
                        StringAssert.Contains($"{Pct(1.0 - s.DurationMultiplier)}%", s.Description);
                        AssertNoBlanketSlowClaim(s.Description, s.name);
                        break;
                    case BulwarkPassiveSO w:
                        StringAssert.Contains($"{Pct(1.0 - w.KnockbackMultiplier)}%", w.Description);
                        StringAssert.Contains($"{Pct(1.0 - w.DurationMultiplier)}%", w.Description);
                        AssertNoBlanketSlowClaim(w.Description, w.name);
                        break;
                    case BullyPassiveSO b:
                        StringAssert.Contains($"{Pct(b.StealMultiplier - 1.0)}%", b.Description);
                        StringAssert.Contains($"carry {(int)Math.Round(b.BonusCapacity, MidpointRounding.AwayFromZero)} more", b.Description);
                        break;
                    case ThiefPassiveSO t:
                        StringAssert.Contains($"{Pct(t.StealMultiplier - 1.0)}%", t.Description);
                        break;
                    case HoarderPassiveSO h:
                        StringAssert.Contains($"{(int)Math.Round(h.MinimumCapacity, MidpointRounding.AwayFromZero)} food", h.Description);
                        break;
                    case SpoilerPassiveSO sp:
                        StringAssert.Contains($"{sp.BonusFood} bonus food", sp.Description);
                        break;
                    case FeatherfootPassiveSO f:
                        StringAssert.Contains("never slow", f.Description);
                        // A6: the text must name both slows the passive ignores.
                        Assert.IsTrue(f.IgnoresPileSlow(null) && f.IgnoresCollisionSlow(null),
                            "Featherfoot ignores the pile slow and the collision slow (A6).");
                        StringAssert.Contains("piles", f.Description.ToLowerInvariant());
                        StringAssert.Contains("bumping", f.Description.ToLowerInvariant());
                        break;
                }
            }
        }
    }
}
