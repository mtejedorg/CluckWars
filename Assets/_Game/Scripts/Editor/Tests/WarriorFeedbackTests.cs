using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Warrior kits' feedback (BL-227, design 2026-11-25 section 16 slice G9): the pure decisions (bar fills, pips, the split of a food gain between a kit
    /// pop and the plain pop), the words and their placeholders, the palette entries, the UXML and USS the HUD binds, and source guards for what only a live
    /// match runs (own view only, no network state, the victim's existing food-lost path). Every number is read from the shipped passive it checks; none is restated.
    /// </summary>
    public sealed class WarriorFeedbackTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string EnforcerPath = "Assets/_Game/Data/Abilities/Passives/Enforcer.asset";
        private const string BullyPath = "Assets/_Game/Data/Abilities/Passives/Bully.asset";

        private static string Code(string path) => RuntimeSourceGuardTests.Code(path);

        // ---- the pure rules --------------------------------------------------------------------------------------------------------

        [Test]
        public void Fill01_IsTheShareLeft_AndNeverLeavesZeroToOne()
        {
            Assert.AreEqual(0.5f, WarriorFeedbackRules.Fill01(3f, 6f), 1e-6f);
            Assert.AreEqual(1f, WarriorFeedbackRules.Fill01(9f, 6f), "more than the window clamps to full");
            Assert.AreEqual(0f, WarriorFeedbackRules.Fill01(-1f, 6f));
            Assert.AreEqual(0f, WarriorFeedbackRules.Fill01(3f, 0f), "no window, no bar");
        }

        [Test]
        public void FreshFill_IsFullWhileCasting_ThenDrainsOverTheShippedWindow()
        {
            var bite = AssetDatabase.LoadAssetAtPath<PassiveAbilitySO>(EnforcerPath).Bite;
            Assert.IsTrue(bite.Active, "the shipped Enforcer has a fresh window");
            Assert.AreEqual(1f, WarriorFeedbackRules.FreshFill01(true, float.PositiveInfinity, bite.FreshSeconds), "mid cast the window has not started");
            Assert.AreEqual(1f, WarriorFeedbackRules.FreshFill01(false, 0f, bite.FreshSeconds), 1e-6f);
            Assert.AreEqual(0.5f, WarriorFeedbackRules.FreshFill01(false, bite.FreshSeconds * 0.5f, bite.FreshSeconds), 1e-6f);
            Assert.AreEqual(0f, WarriorFeedbackRules.FreshFill01(false, bite.FreshSeconds, bite.FreshSeconds), 1e-6f);
            Assert.AreEqual(0f, WarriorFeedbackRules.FreshFill01(false, float.PositiveInfinity, bite.FreshSeconds), "never pecked: no window");
        }

        [Test]
        public void Pips_DrawTheShippedCap_AndFillTheHeldCount()
        {
            var debt = AssetDatabase.LoadAssetAtPath<PassiveAbilitySO>(EnforcerPath).Debt;
            Assert.IsTrue(debt.IsEnforcer);
            Assert.LessOrEqual(debt.Cap, FeedbackTuning.WarriorPipSlotsMax, "the HUD has room to draw the whole cap");
            int slots = WarriorFeedbackRules.PipSlots(debt.Cap, FeedbackTuning.WarriorPipSlotsMax);
            Assert.AreEqual(debt.Cap, slots);
            for (int held = 0; held <= debt.Cap; held++) Assert.AreEqual(held, WarriorFeedbackRules.PipsFilled(held, slots));
            Assert.AreEqual(slots, WarriorFeedbackRules.PipsFilled(slots + 5, slots), "a count above the cap never overdraws");
            Assert.AreEqual(FeedbackTuning.WarriorPipSlotsMax, WarriorFeedbackRules.PipSlots(99, FeedbackTuning.WarriorPipSlotsMax), "a huge cap shows what fits");
            Assert.AreEqual(0, WarriorFeedbackRules.PipSlots(-1, FeedbackTuning.WarriorPipSlotsMax));
        }

        [Test]
        public void TheStealHexNumber_IsTheDebtRulesAmount_ClampedAsTheCargoClamps()
        {
            var passive = AssetDatabase.LoadAssetAtPath<PassiveAbilitySO>(EnforcerPath);
            var debt = passive.Debt;
            float nominal = 5f;   // any nominal: the test is about the bonus per debt, which the asset carries
            for (int held = 0; held <= debt.Cap; held++)
            {
                float asked = DebtRules.StealAmount(nominal, 1f, debt.Steal, held);
                Assert.AreEqual(Mathf.RoundToInt(nominal + debt.Steal * held), WarriorFeedbackRules.HexAmount(WarriorFeedbackRules.ClampedSteal(asked, 99f, 99f)));
            }
            Assert.AreEqual(3, WarriorFeedbackRules.HexAmount(WarriorFeedbackRules.ClampedSteal(9f, 3f, 99f)), "the victim holds less than asked");
            Assert.AreEqual(2, WarriorFeedbackRules.HexAmount(WarriorFeedbackRules.ClampedSteal(9f, 99f, 2f)), "the beak has less room than asked");
            Assert.AreEqual(0, WarriorFeedbackRules.HexAmount(WarriorFeedbackRules.ClampedSteal(9f, 0f, 99f)), "an empty rival shows no number");
        }

        [Test]
        public void AFoodGain_IsSplit_BetweenTheKitPopAndThePlainPop_NeverDrawnTwice()
        {
            Assert.AreEqual(0f, WarriorFeedbackRules.GainAfterPopped(2f, 2f), "a hit-steal's whole take was named by the TAKE pop");
            Assert.AreEqual(5f, WarriorFeedbackRules.GainAfterPopped(11f, 6f), "a cash: the Steal's own amount is left for the plain pop");
            Assert.AreEqual(4f, WarriorFeedbackRules.GainAfterPopped(4f, 0f), "nothing named: the gain is untouched");
            Assert.AreEqual(4f, WarriorFeedbackRules.GainAfterPopped(4f, -3f));
            Assert.IsTrue(WarriorFeedbackRules.PopShown(0.6f));
            Assert.IsFalse(WarriorFeedbackRules.PopShown(0.4f), "an amount that rounds to 0 gets no pop");
            Assert.IsTrue(WarriorFeedbackRules.PendingFresh(10, 8, FeedbackTuning.WarriorPendingFoodFrames));
            Assert.IsFalse(WarriorFeedbackRules.PendingFresh(10, 7, FeedbackTuning.WarriorPendingFoodFrames), "a stale amount is not subtracted from a later gain");
        }

        // ---- words and palette -----------------------------------------------------------------------------------------------------

        [Test]
        public void TheWords_AreInTheCatalogue_WithTheirPlaceholders_AndNeverSayExtortion()
        {
            Assert.AreEqual("TAKE +4", UiText.Format(UiKeys.HudPopHitTake, ("n", 4)));
            Assert.AreEqual("OWES x2", UiText.Format(UiKeys.HudPopDebt, ("n", 2)));
            Assert.AreEqual("FRESH!", UiText.Get(UiKeys.HudPopFresh));
            Assert.AreEqual("PAY UP +6", UiText.Format(UiKeys.HudPopDebtCash, ("n", 6)));
            Assert.AreEqual("FRESH", UiText.Get(UiKeys.HudBadgeFresh));
            Assert.AreEqual("YOU OWE x2", UiText.Format(UiKeys.HudBadgeOwe, ("n", 2)));
            Assert.AreEqual("+9", UiText.Format(UiKeys.HudHexStealAmount, ("n", 9)));

            var csv = File.ReadAllLines("Assets/_Game/Resources/Text/UiText.csv");
            foreach (var key in new[]
            {
                UiKeys.HudPopHitTake, UiKeys.HudPopDebt, UiKeys.HudPopFresh, UiKeys.HudPopDebtCash, UiKeys.HudBadgeFresh, UiKeys.HudBadgeOwe, UiKeys.HudHexStealAmount,
            })
            {
                var rows = csv.Where(l => l.StartsWith(key + ",")).ToArray();
                Assert.AreEqual(1, rows.Length, key + " is exactly one CSV row");
                StringAssert.DoesNotContain("extort", rows[0].ToLowerInvariant(), key);
            }
        }

        [Test]
        public void ThePaletteColours_AreInTheShippedAsset_AndReadApartFromTheOtherMarks()
        {
            var palette = FeedbackPaletteSO.BuiltInDefaults();
            var shipped = AssetDatabase.LoadAssetAtPath<FeedbackPaletteSO>("Assets/_Game/Data/FeedbackPalette.asset");
            Assert.AreEqual(palette.WarriorFreshColor, shipped.WarriorFreshColor, "the shipped palette was re-authored with the fresh colour (CODE-ASSET-13)");
            Assert.AreEqual(palette.WarriorDebtColor, shipped.WarriorDebtColor);
            Assert.AreEqual(palette.WarriorLockColor, shipped.WarriorLockColor);

            foreach (var (name, mine) in new[] { ("fresh", palette.WarriorFreshColor), ("debt", palette.WarriorDebtColor), ("lock", palette.WarriorLockColor) })
                foreach (var (other, colour) in new[]
                {
                    ("loot gold", palette.LootGoldColor), ("yolk", palette.YolkColor), ("stun", palette.StunColor), ("hunt red", palette.MarkLiveColor),
                    ("P1", PlayerPalette.P1), ("P2", PlayerPalette.P2), ("P3", PlayerPalette.P3), ("P4", PlayerPalette.P4),
                })
                    Assert.Greater(Dist(mine, colour), 0.10f, $"the {name} colour reads apart from {other}");
            Assert.Greater(Dist(palette.WarriorFreshColor, palette.WarriorDebtColor), 0.3f);
            Assert.Greater(Dist(palette.WarriorDebtColor, palette.WarriorLockColor), 0.3f);
        }

        private static float Dist(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        // ---- the UXML and USS ------------------------------------------------------------------------------------------------------

        [Test]
        public void EveryElementTheWarriorHudBinds_IsInTheUxml_AndEveryClassItUsesHasARule()
        {
            string uxml = File.ReadAllText("Assets/UI/MarkHud.uxml");
            string uss = File.ReadAllText("Assets/UI/Styles/MarkHud.uss");
            string src = File.ReadAllText(Scripts + "UI/MarkHudController.Warrior.cs");

            for (int i = 0; i < 4; i++) StringAssert.Contains($"name=\"WarriorTag{i}\"", uxml, "a tag slot per possible rival (plus one)");
            Assert.AreEqual("4", Regex.Match(src, "WarriorTagSlots = (\\d+);").Groups[1].Value, "the controller binds the four tag slots the UXML has");
            foreach (var n in Regex.Matches(src, "root\\.Q<[A-Za-z]+>\\(\"(Warrior[A-Za-z]+)\"\\)").Cast<Match>().Select(m => m.Groups[1].Value))
                StringAssert.Contains($"name=\"{n}\"", uxml, n + " is bound by the controller");

            foreach (var cls in new[]
            {
                "cw-wtag", "cw-wtag-fresh", "cw-wtag-fresh-label", "cw-wtag-debt", "cw-wtag-lock", "cw-wpips", "cw-wpip", "cw-wbar-track", "cw-wbar-fill",
                "cw-wbar-fill--fresh", "cw-wbar-fill--debt", "cw-wbar-fill--lock", "cw-wowe", "cw-wowe-label",
            })
            {
                StringAssert.Contains("." + cls, uss, cls + " has a rule");
                StringAssert.Contains(cls, uxml, cls + " is used by the UXML");
            }
            // Pips: every tag and the owe cue hold at least the slots the tuning says the HUD draws.
            Assert.GreaterOrEqual(Regex.Matches(uxml, "class=\"cw-wpip\"").Count, 5 * FeedbackTuning.WarriorPipSlotsMax, "four tags and the owe cue, six pips each");
            string mine = uss.Substring(uss.IndexOf("Warrior kit feedback", System.StringComparison.Ordinal));
            foreach (Match m in Regex.Matches(mine, "font-size:\\s*(\\d+)px"))
                Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), 28, "the HUD font floor");

            // The owe cue is last in the file, so it draws over every rival tag (CODE-UI-09).
            Assert.Greater(uxml.IndexOf("name=\"WarriorOwe\"", System.StringComparison.Ordinal), uxml.IndexOf("name=\"WarriorTag3\"", System.StringComparison.Ordinal));
            Assert.Greater(uxml.IndexOf("name=\"WarriorOwe\"", System.StringComparison.Ordinal), uxml.IndexOf("name=\"LootBadge\"", System.StringComparison.Ordinal));
        }

        [Test]
        public void TheTags_SitBesideThePlate_AndAnOffScreenRivalGetsNone()
        {
            string hud = Code(Scripts + "UI/MarkHudController.Warrior.cs");
            string tag = Body(hud, "private void UpdateWarriorTag(");
            Assert.Less(tag.IndexOf("OnPanel(at.center, panel)", System.StringComparison.Ordinal), tag.IndexOf("SetShown(t.Root, true)", System.StringComparison.Ordinal),
                "a plate off the panel hides the tag before anything is drawn (it used to pin at the edge)");
            StringAssert.Contains("at.xMax + FeedbackTuning.WarriorTagGapPx", tag, "to the right of the plate");
            StringAssert.Contains("at.xMin - FeedbackTuning.WarriorTagGapPx - w", tag, "to the left when the panel ends");
            StringAssert.DoesNotContain("yMin - h", tag, "never above the plate: that stack is the status badges' and the pops'");
            string owe = Body(hud, "private void UpdateOwe(");
            StringAssert.Contains("you.xMax + FeedbackTuning.WarriorTagGapPx", owe);
        }

        [Test]
        public void TheUxml_IsWellFormed()
        {
            Assert.DoesNotThrow(() => new System.Xml.XmlDocument().Load("Assets/UI/MarkHud.uxml"));
        }

        // ---- source guards ---------------------------------------------------------------------------------------------------------

        [Test]
        public void TheFeedback_IsLocalPresentation_NoNetworkStateNoRpc_NoLiteralColours()
        {
            foreach (var path in new[] { "Visuals/WarriorFeedback.cs", "Visuals/WarriorFeedbackRules.cs", "UI/MarkHudController.Warrior.cs" })
            {
                string code = Code(Scripts + path);
                StringAssert.DoesNotContain("[Networked", code, path);
                StringAssert.DoesNotContain("[Rpc", code, path);
                StringAssert.DoesNotContain("RPC_", code, path);
                StringAssert.DoesNotContain("new Color(", code, path + ": the colours are the palette's");
                StringAssert.DoesNotContain("Debug.Log", code, path);
                StringAssert.DoesNotContain("new Label(", code, path + ": text is the UXML's, set from the catalogue");
            }
        }

        [Test]
        public void TheFeedback_ReadsTheKitsHooks_AndBindsAndUnbindsTheFourEvents_OnTheLocalChickenAlone()
        {
            string code = Code(Scripts + "Visuals/WarriorFeedback.cs");
            foreach (var read in new[] { "IsFreshFor(", "LockLeftOn(", "CountOn(", "DecayLeftOn(", "StealAmountOn(", "HasInputAuthority" })
                StringAssert.Contains(read, code, read);
            foreach (var ev in new[] { "HitStealTaken", "FreshHit", "DebtGained", "DebtCashed" })
            {
                StringAssert.Contains($"e.{ev} += ", code, ev + " bound");
                StringAssert.Contains($"e.{ev} -= ", code, ev + " unbound");
            }
            StringAssert.Contains("_bound.LocalEvents", code, "the events are the local chicken's own: no rival's pop is ever drawn");
            // The tags are built only for a kit that has them: a plain chicken and a bystander compute none.
            StringAssert.Contains("if (enforcer || bully)", code);
            StringAssert.Contains("if (!running || !Usable(local)) return;", code);
            // Hidden rivals show nothing, as they show no bird.
            StringAssert.Contains("HiddenVisibilityRules.IsHiddenFor(c)", code);
        }

        [Test]
        public void TheFoodPops_SplitTheCargoGain_ThroughTheOnePlainPop_AndTheSystemIsEnsuredByTheIndicator()
        {
            string hit = Code(Scripts + "Visuals/HitFeedback.cs");
            string body = Body(hit, "private void OnCargoChanged(CargoChangeEvent e)");
            StringAssert.Contains("WarriorFeedbackRules.GainAfterPopped(delta, WarriorFeedback.TakePendingFood(_controller))", body);
            Assert.Less(body.IndexOf("TakePendingFood", System.StringComparison.Ordinal), body.IndexOf("SpawnCargoPop", System.StringComparison.Ordinal),
                "the kit's share is taken off before the plain pop is drawn");

            StringAssert.Contains("WarriorFeedback.EnsureForMatch();", Code(Scripts + "Visuals/RivalIndicator.cs"));
            string hud = Code(Scripts + "UI/MarkHudController.cs");
            foreach (var call in new[] { "BindWarrior(root);", "UpdateWarrior(cam, panel);", "HideWarrior();" })
                StringAssert.Contains(call, hud, call);
        }

        [Test]
        public void TheStealHex_OwnsTheCentreOnlyWhileReady_AndHandsItBack()
        {
            string hud = Code(Scripts + "Input/TouchControlsController.Hud.cs");
            string chain = Body(hud, "private void RefreshCenterMark(");
            int egg = chain.IndexOf("RefreshEggDigit(", System.StringComparison.Ordinal);
            int steal = chain.IndexOf("RefreshStealAmountDigit(", System.StringComparison.Ordinal);
            Assert.Greater(steal, egg, "the amount comes after the states that own the label");
            string digit = Body(hud, "private bool RefreshStealAmountDigit(");
            StringAssert.Contains("mark == HexCenterMark.None", digit, "a cooldown or a refusal keeps the centre");
            StringAssert.Contains("equipped.Is(AbilityJob.Steal)", digit);
            StringAssert.Contains("_localOverlay.IsHot(slot)", digit, "in reach is the game's own verdict, the one behind the hex's in-range pip");
            StringAssert.Contains("WarriorFeedback.Current.StealAmount", digit, "the number is the data's, never typed");
            StringAssert.Contains("UiKeys.HudHexStealAmount", digit);
            StringAssert.Contains("StyleKeyword.Null", digit, "the stylesheet colour comes back with the label");
            Assert.IsFalse(Regex.IsMatch(digit, "\\.text\\s*=\\s*\"[^\"]*\""), "no literal text");
        }

        [Test]
        public void TheBullyVictim_AlreadyGetsTheFoodLostFeedback_ThroughTheSamePathAsASteal()
        {
            // A hit-steal moves food only through the cargo's ordinary steal, whose victim half bumps StealEventId and LastThiefId: the victim's own
            // Stolen listener (attacker direction, flying kernels) and its cargo delta (ROB -n, the cargo-loss hit beat) draw it like any Steal.
            string hits = Code(Scripts + "Gameplay/ChickenHitSteal.cs");
            StringAssert.Contains("_cargo.StealFrom(victim, take)", hits, "the hit-steal asks through the cargo's steal");
            string cargo = Code(Scripts + "Gameplay/ChickenCargo.cs");
            string drain = Body(cargo, "private float DrainForThief(");
            StringAssert.Contains("LastThiefId = thief.Id;", drain);
            StringAssert.Contains("StealEventId++;", drain);

            string hit = Code(Scripts + "Visuals/HitFeedback.cs");
            StringAssert.Contains("_events.Stolen    += OnStolen;", hit);
            StringAssert.Contains("_events.CargoChanged       += OnCargoChanged;", hit);
            string cargoChanged = Body(hit, "private void OnCargoChanged(CargoChangeEvent e)");
            StringAssert.Contains("NotifyCargoLoss(-delta)", cargoChanged, "a loss is the victim's hit beat");
            StringAssert.Contains("SpawnCargoPop(", cargoChanged);
        }

        [Test]
        public void CwWarriorShots_Parses_AndKeepsEveryShotName_SourceGuard()
        {
            Assert.IsTrue(CluckWars.Diagnostics.AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_warriorshots" }).WarriorShots);
            Assert.IsFalse(CluckWars.Diagnostics.AutoTestConfig.FromCommandLine(new[] { "x.exe" }).WarriorShots);
            CollectionAssert.Contains(CluckWars.Diagnostics.AutoTestConfig.Keys, CluckWars.Diagnostics.AutoTestConfig.WarriorShotsKey);
            string src = File.ReadAllText(Scripts + "Diagnostics/AutoTestDriver.Warrior.cs");
            foreach (var shot in new[]
            {
                "warrior-fresh", "warrior-pips", "warrior-hex-amount", "warrior-lock", "warrior-owe",
                "warrior-pop-take", "warrior-pop-debt", "warrior-pop-fresh", "warrior-pop-cash",
            })
                StringAssert.Contains($"\"{shot}\"", src, shot);
            StringAssert.Contains("DevTools.Available", src, "dev tools only");
            StringAssert.Contains("AutoTest warriorshots dev-hook", src, "the one hook is logged");
            StringAssert.Contains("c.Debts.HasStateAuthority", src, "the hook only runs where this peer owns the Enforcer");
        }

        /// <summary>The text of the method whose signature starts with <paramref name="signature"/>, through its matching close brace.</summary>
        private static string Body(string src, string signature)
        {
            int start = src.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, signature + " not found");
            int open = src.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
            }
            Assert.Fail(signature + " has no closing brace");
            return null;
        }
    }
}
