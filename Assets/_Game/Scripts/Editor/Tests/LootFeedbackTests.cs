using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The loot mark's feedback (BL-219, slice B): who sees it (the Thief and the marked rival, nobody else), the Steal hex's dot against the Steal cooldown and
    /// the passive's lead time (following a <c>BalanceBook</c> preview delta), the reward pop's amount (from the event), the blocked and hidden states, and the
    /// source guards for what a live scene is needed to run. No test restates an asset value: every number is read from the shipped asset it checks.
    /// </summary>
    public sealed class LootFeedbackTests
    {
        private sealed class SilentLog : CluckWars.Logging.ILogService
        {
            public CluckWars.Logging.LogLevel MinLevel { get; set; }
            public bool IsEnabled(CluckWars.Logging.LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, System.Exception exception = null) { }
        }

        private const string Scripts = "Assets/_Game/Scripts/";
        private const string ThiefPath = "Assets/_Game/Data/Abilities/Passives/Thief.asset";

        // ---- who sees it -----------------------------------------------------------------------------------------------------

        private const long Me = 1, Thief = 2, Rival = 3, Bystander = 4;

        private static LootSeat Seat(long id, bool local = false, bool thief = false, bool live = false, long marked = 0, bool removed = false) =>
            new LootSeat { Id = id, IsLocal = local, ThiefActive = thief, MarkLive = live, MarkedId = marked, Removed = removed };

        [Test]
        public void TheThief_SeesItsOwnMark_AndOnlyThat()
        {
            var seats = new[] { Seat(Me, local: true, thief: true, live: true, marked: Rival), Seat(Rival), Seat(Bystander) };
            var r = LootPresentationRules.Resolve(seats);
            Assert.IsTrue(r.AsThief);
            Assert.AreEqual(1, r.Target, "the marked rival, by index");
            Assert.IsFalse(r.AsMarked, "a Thief is not marked by its own mark");
        }

        [Test]
        public void TheMarkedRival_SeesItself_MarkedBy_TheThief()
        {
            var seats = new[] { Seat(Thief, thief: true, live: true, marked: Me), Seat(Me, local: true), Seat(Bystander) };
            var r = LootPresentationRules.Resolve(seats);
            Assert.IsTrue(r.AsMarked);
            Assert.AreEqual(0, r.Thief);
            Assert.IsFalse(r.AsThief);
        }

        [Test]
        public void ABystander_SeesNothing_NotEvenAMarkOnAThirdBird()
        {
            // A Thief marking the rival, the bystander is a fourth player; and a Thief with a mark on nobody.
            var third = new[] { Seat(Thief, thief: true, live: true, marked: Rival), Seat(Rival), Seat(Bystander, local: true) };
            var r = LootPresentationRules.Resolve(third);
            Assert.IsFalse(r.AsThief);
            Assert.IsFalse(r.AsMarked);

            var nobody = new[] { Seat(Thief, thief: true, live: true, marked: 0), Seat(Bystander, local: true) };
            r = LootPresentationRules.Resolve(nobody);
            Assert.IsFalse(r.AsThief);
            Assert.IsFalse(r.AsMarked);
        }

        [Test]
        public void ANonThiefWithAStaleMark_IsNeverAThief_AndAWaitingMarkShowsNothing()
        {
            var notThief = new[] { Seat(Me, local: true, thief: false, live: true, marked: Rival), Seat(Rival) };
            Assert.IsFalse(LootPresentationRules.Resolve(notThief).AsThief);

            var waiting = new[] { Seat(Me, local: true, thief: true, live: false, marked: Rival), Seat(Rival) };
            Assert.IsFalse(LootPresentationRules.Resolve(waiting).AsThief, "no live mark: the hex says 'coming', nothing is drawn on a bird");

            var gone = new[] { Seat(Me, local: true, thief: true, live: true, marked: Rival), Seat(Rival, removed: true) };
            Assert.IsFalse(LootPresentationRules.Resolve(gone).AsThief, "a removed rival carries no mark");

            var removedMe = new[] { Seat(Thief, thief: true, live: true, marked: Me), Seat(Me, local: true, removed: true) };
            Assert.IsFalse(LootPresentationRules.Resolve(removedMe).AsMarked);
        }

        [Test]
        public void TwoThievesMarkingEachOther_EachSeesItsOwnMarkAndTheOneOnItself()
        {
            var seats = new[] { Seat(Me, local: true, thief: true, live: true, marked: Thief), Seat(Thief, thief: true, live: true, marked: Me) };
            var r = LootPresentationRules.Resolve(seats);
            Assert.IsTrue(r.AsThief);
            Assert.IsTrue(r.AsMarked);
        }

        [Test]
        public void NoLocalSeat_IsNoRole()
        {
            var r = LootPresentationRules.Resolve(new[] { Seat(Thief, thief: true, live: true, marked: Rival), Seat(Rival) });
            Assert.IsFalse(r.AsThief);
            Assert.IsFalse(r.AsMarked);
            Assert.AreEqual(-1, r.Local);
            Assert.IsFalse(LootPresentationRules.Resolve(null).AsThief);
        }

        [Test]
        public void EveryPresenter_DrawsOnlyFromTheGatedOutputs_SourceGuard()
        {
            string feedback = Code(Scripts + "Visuals/LootFeedback.cs");
            StringAssert.Contains("LootPresentationRules.Resolve(", feedback, "one gate");
            StringAssert.Contains("_bound.LocalEvents.LootTaken +=", feedback, "the reward pop listens to the local chicken's own event, never another Thief's");
            foreach (var path in new[] { "UI/MarkHudController.Loot.cs", "Input/TouchControlsController.Hud.cs" })
            {
                string src = Code(Scripts + path);
                StringAssert.Contains("LootFeedback.Current", src, path + " reads the gated outputs");
                StringAssert.DoesNotContain("ThiefLoot", src, path + " never reads a chicken's loot state itself: that would skip the gate");
                StringAssert.DoesNotContain(".Loot.", src, path);
            }
            // Local presentation only: nothing replicated, nothing sent, no counter watched (ARCH-EVT-03).
            foreach (var path in new[] { "Visuals/LootFeedback.cs", "Visuals/LootPresentationRules.cs", "Visuals/LootHexRules.cs", "UI/MarkHudController.Loot.cs", "UI/CoinBagGlyph.cs" })
            {
                string src = Code(Scripts + path);
                StringAssert.DoesNotContain("[Rpc", src, path);
                StringAssert.DoesNotContain("[Networked", src, path);
                StringAssert.DoesNotContain("LootTakenEventId", src, path);
                StringAssert.DoesNotContain("Debug.Log", src, path);
            }
        }

        // ---- the Steal hex's dot ------------------------------------------------------------------------------------------------

        private static ThiefPassiveSO ShippedThief()
        {
            var thief = UnityEditor.AssetDatabase.LoadAssetAtPath<ThiefPassiveSO>(ThiefPath);
            Assert.IsNotNull(thief, ThiefPath + " is missing");
            return thief;
        }

        [Test]
        public void TheHexDot_IsEmptyWhileTheMarkIsComing_AndSearchingFromTheLeadTimeBeforeStealIsReady()
        {
            float lead = ShippedThief().LootMark.LeadSeconds;
            Assert.Greater(lead, 0f, "the passive carries a lead");

            var coming = LootHexRules.ViewFor(true, LootState.Waiting, lead + 1f, 0.4f, lead, false);
            Assert.AreEqual(LootDotState.Coming, coming.Dot);
            Assert.AreEqual(0.4f, coming.Fill01, 1e-5f, "the dot fills with the Steal cooldown");

            // Rule v2 (2026-11-26): the draw can find nobody, so a due draw with no mark up is a hollow ring, never the solid gold of a mark that exists.
            Assert.AreEqual(LootDotState.Searching, LootHexRules.ViewFor(true, LootState.Waiting, lead, 0.9f, lead, false).Dot, "hollow exactly the lead before Steal is ready");
            Assert.AreEqual(LootDotState.Searching, LootHexRules.ViewFor(true, LootState.Waiting, 0f, 1f, lead, false).Dot, "Steal ready, no mark drawn yet: searching, not gold");
            Assert.AreEqual(LootDotState.Live, LootHexRules.ViewFor(true, LootState.Live, lead, 0.9f, lead, false).Dot, "a mark up at the same moment is solid gold");
            Assert.AreEqual(1f, LootHexRules.ViewFor(true, LootState.Waiting, 0f, 1f, lead, false).Fill01, "the ring is full size");
            Assert.IsFalse(LootHexRules.ViewFor(true, LootState.Waiting, 0f, 1f, lead, true).Pulse, "nothing to fire at while searching");
            Assert.AreEqual(3, (int)LootDotState.Searching, "appended after Live: no ordinal moved");
            Assert.AreEqual(LootDotState.Coming, LootHexRules.ViewFor(true, LootState.Waiting, lead + 0.01f, 0.9f, lead, false).Dot);
        }

        [Test]
        public void TheHexDot_MovesWithAServedLeadTime_ThroughBalanceBookPreview()
        {
            var thief = ShippedThief();
            float shipped = thief.LootMark.LeadSeconds;
            var range = typeof(ThiefPassiveSO).GetField(nameof(ThiefPassiveSO.LootLeadSeconds))
                .GetCustomAttributes(typeof(UnityEngine.RangeAttribute), false).Cast<UnityEngine.RangeAttribute>().Single();
            float served = shipped < range.max * 0.5f ? shipped * 2f : shipped * 0.5f;
            Assert.AreNotEqual(shipped, served);
            string json = "{\"schema\":1,\"assets\":{\"" + thief.name + "\":{\"LootLeadSeconds\":" + served.ToString("R", CultureInfo.InvariantCulture) + "}}}";
            using var book = new BalanceBook(new SilentLog(),
                TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath));
            var verdict = book.Evaluate(json, "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
            var changed = book.Preview(thief, verdict);
            try
            {
                float lead = changed.LootMark.LeadSeconds;
                Assert.AreEqual(served, lead, 1e-4f, "the preview carries the served lead");
                float between = (shipped + served) * 0.5f;
                var withShipped = LootHexRules.ViewFor(true, LootState.Waiting, between, 0.5f, shipped, false).Dot;
                var withServed = LootHexRules.ViewFor(true, LootState.Waiting, between, 0.5f, lead, false).Dot;
                Assert.AreNotEqual(withShipped, withServed, "a cooldown between the two leads is searching under the longer lead and still coming under the shorter");
                Assert.AreEqual(served > shipped ? LootDotState.Searching : LootDotState.Coming, withServed);
            }
            finally { Object.DestroyImmediate(changed); }
        }

        [Test]
        public void TheHexDot_IsGoldWhileAMarkIsUp_PulsesOnlyWithinReach_AndIsOffForEveryoneElse()
        {
            float lead = ShippedThief().LootMark.LeadSeconds;
            foreach (var up in new[] { LootState.Live, LootState.Overdue })
            {
                var far = LootHexRules.ViewFor(true, up, 7f, 0.1f, lead, false);
                Assert.AreEqual(LootDotState.Live, far.Dot, up + ": gold whatever the cooldown says");
                Assert.IsFalse(far.Pulse);
                Assert.IsTrue(LootHexRules.ViewFor(true, up, 7f, 0.1f, lead, true).Pulse, up + ": pulse when the marked bird is in reach");
            }
            Assert.IsFalse(LootHexRules.ViewFor(true, LootState.Waiting, 0f, 1f, lead, true).Pulse, "no mark on a bird, nothing to fire at");
            Assert.AreEqual(LootDotState.Off, LootHexRules.ViewFor(true, LootState.Off, 0f, 1f, lead, true).Dot, "before GO");
            Assert.AreEqual(LootDotState.Off, LootHexRules.ViewFor(false, LootState.Live, 0f, 1f, lead, true).Dot, "a kit without the loot mark");
        }

        [Test]
        public void TheSteadyCooldownFill_IsClamped()
        {
            Assert.AreEqual(1f, LootHexRules.ViewFor(true, LootState.Waiting, 99f, 7f, 1f, false).Fill01);
            Assert.AreEqual(0f, LootHexRules.ViewFor(true, LootState.Waiting, 99f, -3f, 1f, false).Fill01);
        }

        // ---- reach, blocked and hidden --------------------------------------------------------------------------------------------

        private static float ShippedStealReach()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var steal = registry.All.Single(a => a != null && a.Job == AbilityJob.Steal);
            Assert.Greater(steal.IndicatorRange, 0f);
            return steal.IndicatorRange;
        }

        [Test]
        public void TheBrackets_StandOnlyWithinStealsReach_ReadFromTheAbility()
        {
            float reach = ShippedStealReach();
            Assert.IsTrue(LootPresentationRules.InReach(reach, reach));
            Assert.IsTrue(LootPresentationRules.InReach(0f, reach));
            Assert.IsFalse(LootPresentationRules.InReach(reach + 0.01f, reach));
            Assert.IsFalse(LootPresentationRules.InReach(0f, 0f), "a kit with no known reach never claims one");
            Assert.IsTrue(LootPresentationRules.BracketsShown(true, true));
            Assert.IsFalse(LootPresentationRules.BracketsShown(true, false));
            Assert.IsFalse(LootPresentationRules.BracketsShown(false, true), "a hidden rival's brackets would give it away");
            StringAssert.Contains("a.IndicatorRange", Code(Scripts + "Visuals/LootFeedback.cs"), "the reach is the Steal ability's, not a literal");
        }

        [Test]
        public void ABlockedMark_ShowsTheWallOrTheShell_AndAHiddenRivalHidesTheBag()
        {
            Assert.AreEqual(LootBlocker.None, LootPresentationRules.BlockerFor(false, false));
            Assert.AreEqual(LootBlocker.Walled, LootPresentationRules.BlockerFor(true, false));
            Assert.AreEqual(LootBlocker.Shelled, LootPresentationRules.BlockerFor(false, true));
            Assert.AreEqual(LootBlocker.Shelled, LootPresentationRules.BlockerFor(true, true), "the egg wins, as the Hunt's badge does");

            Assert.IsTrue(LootPresentationRules.MarkerShown(true, false));
            Assert.IsFalse(LootPresentationRules.MarkerShown(true, true), "Hide hides the mark from the Thief as it hides the bird");
            Assert.IsFalse(LootPresentationRules.MarkerShown(false, false));
            string feedback = Code(Scripts + "Visuals/LootFeedback.cs");
            StringAssert.Contains("HiddenVisibilityRules.IsHiddenFor(_target)", feedback);
            StringAssert.Contains("PlayerBase.WallBetween(", feedback);
            StringAssert.Contains("StealRules.CanStealFrom(", feedback);
        }

        // ---- the pops, the hop and the fade --------------------------------------------------------------------------------------------

        [Test]
        public void TheRewardPop_NamesTheBountyFromTheEvent_NotFromTheAsset()
        {
            Assert.IsTrue(LootPresentationRules.RewardShown(7f));
            Assert.IsTrue(LootPresentationRules.RewardShown(0.6f), "rounds to a whole food");
            Assert.IsFalse(LootPresentationRules.RewardShown(0f), "a bounty that did not fit says nothing");
            Assert.IsFalse(LootPresentationRules.RewardShown(0.4f));

            string src = Code(Scripts + "Visuals/LootFeedback.cs");
            string handler = Body(src, "private void OnLootTaken(");
            StringAssert.Contains("Mathf.RoundToInt(e.Bounty)", handler, "the amount is the event's");
            foreach (var fromAsset in new[] { "LootBounty", "Facts", "LootMark", "passive" })
                StringAssert.DoesNotContain(fromAsset, handler, "the pop never reads the asset");
            StringAssert.Contains("UiKeys.HudPopLootTake", handler);
        }

        [Test]
        public void TheGrabPop_ShowsOnlyWhatTheRewardPopDoesNotSay()
        {
            Assert.AreEqual(5f, LootPresentationRules.GrabAfterBounty(15f, 10f), 1e-5f, "take 5 + bounty 10: the grab pop says +5 and LOOT says +10");
            Assert.AreEqual(0f, LootPresentationRules.GrabAfterBounty(10f, 10f), "an empty beak: the LOOT pop alone");
            Assert.AreEqual(0f, LootPresentationRules.GrabAfterBounty(4f, 10f), "never negative");
            Assert.AreEqual(8f, LootPresentationRules.GrabAfterBounty(8f, 0f), "any other gain is untouched");
            StringAssert.Contains("LootPresentationRules.GrabAfterBounty(", Code(Scripts + "Visuals/HitFeedback.cs"));
        }

        [Test]
        public void TheHop_AndTheBadgeFade_FollowTheirTimesAndStandDownUnderReducedMotion()
        {
            Assert.AreEqual(1f, LootPresentationRules.HopPop(FeedbackTuning.LootHopSeconds, false), 1e-5f);
            Assert.AreEqual(FeedbackTuning.LootHopFrom, LootPresentationRules.HopPop(0f, false), 1e-5f);
            float mid = LootPresentationRules.HopPop(FeedbackTuning.LootHopSeconds * 0.5f, false);
            Assert.Greater(mid, FeedbackTuning.LootHopFrom);
            Assert.Less(mid, 1f);
            Assert.AreEqual(1f, LootPresentationRules.HopPop(0f, true), "Reduced Motion: no hop");
            Assert.AreEqual(1f, LootPresentationRules.HopPop(float.PositiveInfinity, false), "at rest");

            Assert.AreEqual(1f, LootPresentationRules.BadgeAlpha(0f, true, 0.016f), "full the frame it lands");
            float a = 1f;
            for (float t = 0f; t < FeedbackTuning.LootBadgeFadeSeconds + 0.1f; t += 0.01f) a = LootPresentationRules.BadgeAlpha(a, false, 0.01f);
            Assert.AreEqual(0f, a, 1e-5f, "gone after the fade time");
        }

        [Test]
        public void TheMarkedRivalsPopAndBuzz_AreSelfPops_AndTheThiefsRefusalIsTheExistingSafeWord()
        {
            string src = Code(Scripts + "Visuals/LootFeedback.cs");
            string landed = Body(src, "private void WarnLanded(");
            StringAssert.Contains("FloatingCombatText.SpawnSelfPop(local, UiKeys.HudPopMarked", landed, "a self-pop: a rival's bird never draws it");
            StringAssert.Contains("HapticKind.HuntLive", landed, "a short buzz");
            string refused = Body(src, "private void OnMarkedStealRefused(");
            StringAssert.Contains("UiKeys.HudPopSafe", refused);
            StringAssert.Contains("HapticKind.HuntPounce", Body(src, "private void OnLootTaken("), "the take buzzes stronger than the warning");
        }

        // ---- the brackets, the words, the files -----------------------------------------------------------------------------------------

        [Test]
        public void TheLootBrackets_ShareTheHuntsMesh_ButNeverItsStatics()
        {
            var root = new GameObject("LootBracketsTestRoot");
            try
            {
                var brackets = HuntBrackets.Create(root.transform, null, "LootBrackets", false);
                Assert.IsFalse(brackets.IsShown);
                brackets.ApplyColour(FeedbackTuning.LootGoldColor, Vector3.zero, 0.5f, 1f);
                Assert.IsTrue(brackets.IsShown, "drawn");
                Assert.IsFalse(HuntBrackets.ShownNow, "the Death Mark's own readout is untouched");
                brackets.Hide();
                Assert.IsFalse(brackets.IsShown);
            }
            finally { Object.DestroyImmediate(root); }
            Assert.IsFalse(HuntBrackets.ShownNow);
        }

        [Test]
        public void TheLootWords_AreInTheCatalogue_WithTheirPlaceholders()
        {
            Assert.AreEqual("MARKED!", UiText.Get(UiKeys.HudPopMarked));
            Assert.AreEqual("LOOT", UiText.Get(UiKeys.HudBadgeLoot));
            Assert.AreEqual("LOOT +7", UiText.Format(UiKeys.HudPopLootTake, ("n", 7)));
        }

        [Test]
        public void TheLootGold_IsApartFromTheYolkTheStunAndThePlayerColours()
        {
            var palette = FeedbackPaletteSO.BuiltInDefaults();
            var gold = palette.LootGoldColor;
            foreach (var (name, other) in new[]
            {
                ("yolk", palette.YolkColor), ("stun", palette.StunColor), ("pounce yellow", palette.MarkPounceColor),
                ("P1", PlayerPalette.P1), ("P2", PlayerPalette.P2), ("P3", PlayerPalette.P3), ("P4", PlayerPalette.P4),
            })
                Assert.Greater(Dist(gold, other), 0.12f, "the bag's gold reads apart from " + name);
            var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<FeedbackPaletteSO>("Assets/_Game/Data/FeedbackPalette.asset");
            Assert.AreEqual(gold, shipped.LootGoldColor, "the shipped palette carries the same gold");
            Assert.AreEqual(palette.LootEmptyColor, shipped.LootEmptyColor);
        }

        private static float Dist(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        [Test]
        public void EveryElementTheLootHudBinds_IsInTheUxml_AndEveryClassItEmitsHasARule()
        {
            string uxml = File.ReadAllText("Assets/UI/MarkHud.uxml");
            string controller = Code(Scripts + "UI/MarkHudController.Loot.cs");
            var names = Regex.Matches(File.ReadAllText(Scripts + "UI/MarkHudController.Loot.cs"), "root\\.Q<[A-Za-z]+>\\(\"(Loot[A-Za-z]+)\"\\)").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.GreaterOrEqual(names.Count, 12);
            foreach (var n in names) StringAssert.Contains($"name=\"{n}\"", uxml, n + " is bound by the controller");

            string markUss = File.ReadAllText("Assets/UI/Styles/MarkHud.uss");
            foreach (var cls in new[] { "cw-loot-bag", "cw-loot-badge", "cw-loot-badge-bag", "cw-loot-badge-label", "cw-loot-arrowbag", "cw-loot-arrowbag-glyph" })
            {
                StringAssert.Contains("." + cls, markUss, cls);
                StringAssert.Contains(cls, uxml, cls + " is used by the UXML");
            }
            string touchUss = File.ReadAllText("Assets/UI/Styles/TouchControls.uss");
            Assert.IsTrue(Regex.IsMatch(touchUss, "\\." + Regex.Escape(LootHexRules.LootClass) + "\\s+\\.cw-hex-huntdot\\s*\\{"), "the class the hex emits has a rule");
            StringAssert.DoesNotContain("new Color(", controller, "the colours are the palette's");
            StringAssert.DoesNotContain("new Color(", Code(Scripts + "Visuals/LootFeedback.cs"));
        }

        [Test]
        public void TheClockPrize_HasNoFeedbackOfItsOwn()
        {
            foreach (var path in new[] { "Visuals/LootFeedback.cs", "UI/MarkHudController.Loot.cs", "Visuals/LootPresentationRules.cs" })
                StringAssert.DoesNotContain("ClockPrize", Code(Scripts + path), path + ": the owner left the end-of-clock prize without feedback for now");
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------------------

        private static string Code(string path) => RuntimeSourceGuardTests.Code(path);

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
