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
                    return ($"Your stuns and slows last {Pct(r.OutgoingDurationMultiplier - 1.0)}% longer.",
                            $"Your stuns and slows last {Pct(r.OutgoingDurationMultiplier - 1.0)}% longer. " +
                            $"Stuns, getting stuck and timed slows on you end {Pct(1.0 - r.DurationMultiplier)}% sooner.");
                case BullyPassiveSO b:
                    return ($"Moves recharge {Pct(1.0 - b.CooldownMultiplier)}% faster.",
                            "Applies to every move except Peck.");
                case ThiefPassiveSO t:
                    int tCap = (int)Math.Round(t.MinimumCapacity, MidpointRounding.AwayFromZero);
                    return ($"Rob the bird with your loot bag for {t.LootBounty} extra food. {t.ClockPrize} extra food if time runs out. Carry {tCap} food.",
                            $"One rival carries your loot bag, and only you and that bird can see it. Rob that bird: you grab the food it holds, and {t.LootBounty} new food drops into your beak, as far as it fits. " +
                            $"A new bag shows up just before your Steal is ready again. If time runs out before anyone reaches {cfg.FoodTargetToWin}, you get {t.ClockPrize} extra food at home. You carry up to {tCap} food.");
                case SlipperyPassiveSO s:
                    return ($"Ability slows on you are {Pct(1.0 - s.Resist)}% weaker and end {Pct(1.0 - s.Resist)}% sooner.",
                            "Stuns still land in full. Pile and bump slows still apply.");
                case BulwarkPassiveSO w:
                    return ("Barely budges when shoved.",
                            $"Knockback cut by {Pct(1.0 - w.KnockbackMultiplier)}%. Stuns, getting stuck and timed slows end {Pct(1.0 - w.DurationMultiplier)}% sooner.");
                case HoarderPassiveSO h:
                    int cap = (int)Math.Round(h.MinimumCapacity, MidpointRounding.AwayFromZero);
                    return ($"Carry {cap} food in one trip.", $"Carries at least {cap} food.");
                case FeatherfootPassiveSO f:
                    return ($"Bank {Pct(f.DepositRateMultiplier - 1.0)}% faster. Piles and bumps never slow you.",
                            $"You bank food {Pct(f.DepositRateMultiplier - 1.0)}% faster, and food piles and bumping into rivals never slow you down.");
                case SpoilerPassiveSO sp:
                    int spCap = (int)Math.Round(sp.MinimumCapacity, MidpointRounding.AwayFromZero);
                    // The pull is the Hunt's own number, read here from the Hunt asset the registry holds.
                    var hunt = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath).All.OfType<MarkKillAbilitySO>().Single();
                    string pull = hunt.LeaderPickWeight.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                    return ($"Your Hunt goes after the bird with the most food {pull} times more often. Carry {spCap} food.",
                            $"The bird with the most food at home is {pull} times as likely to be hunted. A kill fills your beak with food: you carry up to {spCap} food.");
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
        /// coverage of one slow in five. Any slow claim must say "timed slows" — or "ability slows"
        /// for Slippery, which since 2026-11-05 also cuts zone / aura slows short (its zone window)
        /// but never the pile or bump slow.
        /// </summary>
        private static void AssertNoBlanketSlowClaim(string text, string who)
        {
            string lower = text.ToLowerInvariant();
            Assert.IsFalse(lower.StartsWith("slows") || lower.Contains(" slows, stuns") || lower.Contains("covers slows"),
                $"{who}: '{text}' claims every slow is shortened; only timed slows are.");
            if (lower.Contains("slows") && lower.Contains("sooner"))
                Assert.IsTrue(lower.Contains("timed slows") || lower.Contains("ability slows"),
                    $"{who}: a 'sooner' claim about slows must say 'timed slows' or 'ability slows'.");
        }

        [Test]
        public void ControlDurationPerks_DoNotOverstateSlowCoverage()
        {
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is SlipperyPassiveSO) && !(p is BulwarkPassiveSO) && !(p is RelentlessPassiveSO)) continue;
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
            var bully = ScriptableObject.CreateInstance<BullyPassiveSO>();
            var bulwark = ScriptableObject.CreateInstance<BulwarkPassiveSO>();
            var hoarder = ScriptableObject.CreateInstance<HoarderPassiveSO>();
            try
            {
                bully.CooldownMultiplier = 0.5f;
                relentless.OutgoingDurationMultiplier = 1.5f;
                relentless.DurationMultiplier = 0.6f;
                bulwark.DurationMultiplier = 0.5f;
                bulwark.KnockbackMultiplier = 0.1f;
                hoarder.MinimumCapacity = 55f;

                Assert.AreEqual("Moves recharge 50% faster.", bully.PerkLine());
                Assert.AreEqual("Your stuns and slows last 50% longer. Stuns, getting stuck and timed slows on you end 40% sooner.",
                    relentless.PerkDetail());
                Assert.AreEqual("Knockback cut by 90%. Stuns, getting stuck and timed slows end 50% sooner.", bulwark.PerkDetail());
                Assert.AreEqual("Carry 55 food in one trip.", hoarder.PerkLine(),
                    "The Hoarder line must carry {cap} from MinimumCapacity, not a fixed phrase.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(relentless);
                UnityEngine.Object.DestroyImmediate(bully);
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
                Assert.AreEqual(Expected(sp, cfg).detail, sp.Description);
                return;
            }
            Assert.Fail("No Spoiler in the ability registry.");
        }

        [Test]
        public void ThiefAsset_Description_IsTheLineTemplate_WithTheCurrentNumbers()
        {
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is ThiefPassiveSO t)) continue;
                Assert.AreEqual(Expected(t, cfg).line, t.Description, "the stored text is the line template (the detail is too long for the ability strip: 140 characters)");
                return;
            }
            Assert.Fail("No Thief in the ability registry.");
        }

        [Test]
        public void BullyAsset_Description_IsTheLinePlusTheDetail_WithTheCurrentNumbers()
        {
            // Round-4 finding 18 (then on Relentless, whose cooldown cut moved to Bully on 2026-11-05): the asset and the
            // menu line must not drift. Pinned like Spoiler: the stored text is the two templates, joined.
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is BullyPassiveSO b)) continue;
                Assert.AreEqual($"Moves recharge {Pct(1.0 - b.CooldownMultiplier)}% faster. Applies to every move except Peck.", b.Description);
                Assert.AreEqual(b.PerkLine(cfg) + " " + b.PerkDetail(cfg), b.Description);
                return;
            }
            Assert.Fail("No Bully in the ability registry.");
        }

        [Test]
        public void RelentlessAsset_Description_IsTheDetail_WithTheCurrentNumbers()
        {
            foreach (var p in ShippedPassives(out var cfg))
            {
                if (!(p is RelentlessPassiveSO r)) continue;
                Assert.AreEqual($"Your stuns and slows last {Pct(r.OutgoingDurationMultiplier - 1.0)}% longer. " +
                                $"Stuns, getting stuck and timed slows on you end {Pct(1.0 - r.DurationMultiplier)}% sooner.", r.Description);
                Assert.AreEqual(r.PerkDetail(cfg), r.Description);
                return;
            }
            Assert.Fail("No Relentless in the ability registry.");
        }

        [Test]
        public void ThiefDetail_WithoutAMatchConfig_IsLoudNotBlank()
        {
            var thief = ScriptableObject.CreateInstance<ThiefPassiveSO>();
            try
            {
                StringAssert.Contains("#goal#", thief.PerkDetail(null),
                    "With no MatchConfig, {goal} must render as a visible marker, not vanish.");
                Assert.IsNotEmpty(UiText.Problems, "The missing {goal} argument must be reported.");
            }
            finally { UnityEngine.Object.DestroyImmediate(thief); }
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
                        StringAssert.Contains($"{Pct(r.OutgoingDurationMultiplier - 1.0)}% longer", r.Description);
                        StringAssert.Contains($"{Pct(1.0 - r.DurationMultiplier)}% sooner", r.Description);
                        AssertNoBlanketSlowClaim(r.Description, r.name);
                        break;
                    case SlipperyPassiveSO s:
                        StringAssert.Contains($"{Pct(1.0 - s.Resist)}%", s.Description);
                        AssertNoBlanketSlowClaim(s.Description, s.name);
                        Assert.AreEqual(s.PerkLine(null), s.Description, "the stored text is the line template");
                        break;
                    case BulwarkPassiveSO w:
                        StringAssert.Contains($"{Pct(1.0 - w.KnockbackMultiplier)}%", w.Description);
                        StringAssert.Contains($"{Pct(1.0 - w.DurationMultiplier)}%", w.Description);
                        AssertNoBlanketSlowClaim(w.Description, w.name);
                        break;
                    case BullyPassiveSO b:
                        StringAssert.Contains($"{Pct(1.0 - b.CooldownMultiplier)}%", b.Description);
                        StringAssert.Contains("except Peck", b.Description);
                        break;
                    case ThiefPassiveSO t:
                        StringAssert.Contains($"for {t.LootBounty} extra food", t.Description);
                        StringAssert.Contains($"{t.ClockPrize} extra food if time runs out", t.Description);
                        StringAssert.Contains($"Carry {(int)Math.Round(t.MinimumCapacity, MidpointRounding.AwayFromZero)} food", t.Description);
                        StringAssert.DoesNotContain("more", t.Description, "the 1.6x on every steal is gone: no text may still promise a bigger steal");
                        break;
                    case HoarderPassiveSO h:
                        StringAssert.Contains($"{(int)Math.Round(h.MinimumCapacity, MidpointRounding.AwayFromZero)} food", h.Description);
                        break;
                    case SpoilerPassiveSO sp:
                        StringAssert.DoesNotContain("bonus food", sp.Description, "the Spoiler's end-of-clock prize is gone: no text may still promise it");
                        StringAssert.Contains($"carry up to {(int)Math.Round(sp.MinimumCapacity, MidpointRounding.AwayFromZero)} food", sp.Description);
                        break;
                    case FeatherfootPassiveSO f:
                        StringAssert.Contains("never slow", f.Description);
                        StringAssert.Contains($"bank food {Pct(f.DepositRateMultiplier - 1.0)}% faster", f.Description);
                        Assert.AreEqual(f.PerkDetail(null), f.Description, "the stored text is the detail template");
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
