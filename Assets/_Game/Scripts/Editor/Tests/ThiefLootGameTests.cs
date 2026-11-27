using System;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NetworkedAttribute = Fusion.NetworkedAttribute;
using NetworkObject = Fusion.NetworkObject;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Thief's loot mark in the game (BL-217, slice A). The pure rules are covered where they live (<c>MatchLootTests</c> drives the same
    /// <see cref="LootPickRules"/> and <see cref="LootRules"/>); here: the marked steal's settlement rules driven for real
    /// (<see cref="MarkedStealBook{TKey}"/> is the object <c>ChickenCargo</c> holds), the one new rule (<see cref="LootRules.DrawDue"/>), the networked
    /// layout, the loot-taken event through the one observer, the telemetry lines, and source guards for what a live runner is needed to execute.
    /// No test restates an asset value: every number comes from a fixture or from the shipped asset it checks.
    /// </summary>
    public sealed class ThiefLootGameTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string CargoPath = Scripts + "Gameplay/ChickenCargo.cs";
        private const string LootPath = Scripts + "Gameplay/ThiefLoot.cs";
        private const string SnatchPath = Scripts + "Abilities/SnatchAbilitySO.cs";
        private const string BotPath = Scripts + "Gameplay/BotController.cs";
        private const float Window = FoodTransferRules.SettleWindowSeconds;

        private const int Victim = 7, Other = 8;
        private const float Capacity = 20f, Nominal = 5f, Bounty = 10f;

        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);

        // ---- the marked steal's settlement (the book ChickenCargo runs) -------------------------------------------

        private static MarkedStealBook<int> NewBook(out FoodRequests<int> requests)
        {
            requests = new FoodRequests<int>();
            return new MarkedStealBook<int>(requests);
        }

        [Test]
        public void ThePayIsTheTakeFirst_ThenTheBountyIntoTheRoomLeft()
        {
            var book = NewBook(out _);
            float cargo = 4f, victimHolds = 3f;
            Assert.IsTrue(book.TryOpen(Victim, Nominal, victimHolds, Bounty, Capacity, cargo, 0f, out float asked, out ushort id));
            Assert.AreEqual(victimHolds, asked, 1e-4f, "the take is held to what the victim carries");

            var s = book.Settle(Victim, id, asked, true, true, Capacity, cargo, 0.2f);
            Assert.AreEqual(MarkedOutcome.Reached, s.Outcome);
            Assert.AreEqual(victimHolds, s.Take, 1e-4f);
            Assert.AreEqual(Bounty, s.Bounty, 1e-4f, "room is 16, the take used 3, so the whole bounty fits");
            Assert.AreEqual(0f, s.BountyLost, 1e-4f);
        }

        [Test]
        public void WhatDoesNotFit_IsLost_AndTheTakeHasTheFirstCallOnTheRoom()
        {
            var book = NewBook(out _);
            float cargo = 14f;   // room 6
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 40f, Bounty, Capacity, cargo, 0f, out float asked, out ushort id));
            Assert.AreEqual(Nominal, asked, 1e-4f);

            var s = book.Settle(Victim, id, asked, true, true, Capacity, cargo, 0.1f);
            Assert.AreEqual(Nominal, s.Take, 1e-4f, "the steal comes first");
            Assert.AreEqual(Capacity - cargo - Nominal, s.Bounty, 1e-4f, "the bounty fills what is left of the 6");
            Assert.AreEqual(Bounty - s.Bounty, s.BountyLost, 1e-4f);
            Assert.LessOrEqual(cargo + s.Take + s.Bounty, Capacity + 1e-4f, "the hold never exceeds its capacity");
        }

        [Test]
        public void AnEmptyBeak_StillPaysTheBounty_ATakeOfZero()
        {
            var book = NewBook(out _);
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 0f, Bounty, Capacity, 0f, 0f, out float asked, out ushort id));
            Assert.AreEqual(0f, asked, "nothing to ask the victim for, but the victim's answer is still needed");
            var s = book.Settle(Victim, id, 0f, true, true, Capacity, 0f, 0.1f);
            Assert.AreEqual(MarkedOutcome.Reached, s.Outcome);
            Assert.AreEqual(0f, s.Take);
            Assert.AreEqual(Bounty, s.Bounty, 1e-4f);
        }

        [Test]
        public void AFullThief_GainsNothing_AndTheBountyIsLost_ButTheMarkIsStillSpent()
        {
            var book = NewBook(out _);
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, Capacity, 0f, out float asked, out ushort id));
            Assert.AreEqual(0f, asked);
            var s = book.Settle(Victim, id, 0f, true, true, Capacity, Capacity, 0.1f);
            Assert.AreEqual(MarkedOutcome.Reached, s.Outcome, "the mark is spent whatever fitted");
            Assert.AreEqual(0f, s.Take);
            Assert.AreEqual(0f, s.Bounty);
            Assert.AreEqual(Bounty, s.BountyLost, 1e-4f);
        }

        [Test]
        public void ARefusedSteal_PaysNothing_ReleasesItsRoom_AndLeavesTheMark()
        {
            var book = NewBook(out var requests);
            float cargo = 5f;
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, cargo, 0f, out float asked, out ushort id));
            Assert.AreEqual(Capacity - cargo - Nominal - Bounty, requests.FreeSpace(Capacity, cargo, 0.05f), 1e-4f, "take and bounty are held while the request is open");

            var s = book.Settle(Victim, id, 0f, false, true, Capacity, cargo, 0.1f);
            Assert.AreEqual(MarkedOutcome.Blocked, s.Outcome);
            Assert.AreEqual(0f, s.Take);
            Assert.AreEqual(0f, s.Bounty);
            Assert.AreEqual(Capacity - cargo, requests.FreeSpace(Capacity, cargo, 0.1f), 1e-4f, "the reserved room is back");
            Assert.IsFalse(book.Pending(0.1f));
        }

        [Test]
        public void AMarkThatEndedInFlight_PaysTheTakeButNoBounty_AndFreesTheBountysRoom()
        {
            var book = NewBook(out var requests);
            float cargo = 5f;
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, cargo, 0f, out float asked, out ushort id));
            var s = book.Settle(Victim, id, asked, true, false, Capacity, cargo, 0.1f);
            Assert.AreEqual(MarkedOutcome.Reached, s.Outcome);
            Assert.IsTrue(s.MarkEnded);
            Assert.AreEqual(asked, s.Take, 1e-4f, "the steal itself still lands: the owner's rule is only about the bounty");
            Assert.AreEqual(0f, s.Bounty, "marked rival removed: no pay");
            Assert.AreEqual(0f, s.BountyLost, "it was never owed, so it is not reported as lost either");
            Assert.AreEqual(Capacity - cargo, requests.FreeSpace(Capacity, cargo, 0.1f), 1e-4f, "no reservation is left behind, the bounty's included");
            Assert.IsFalse(book.Pending(0.1f));
        }

        [Test]
        public void TheSettlement_AsksTheMarkBeforePayingTheBounty()
        {
            string settle = Body(CargoPath, "public void RPC_SettleMarkedSteal(");
            StringAssert.Contains("_loot.IsMarking(victim)", settle, "the bounty is owed only while the mark is still on that victim");
            Assert.Less(settle.IndexOf("_loot.IsMarking(victim)", StringComparison.Ordinal), settle.IndexOf("_marked.Settle(", StringComparison.Ordinal));
        }

        [Test]
        public void ARefusalThatClaimsFood_IsStillWorthNothing()
        {
            var book = NewBook(out _);
            book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, 0f, out float asked, out ushort id);
            var s = book.Settle(Victim, id, asked, false, true, Capacity, 0f, 0.1f);
            Assert.AreEqual(0f, s.Take, "reached false: the stolen figure is ignored");
        }

        [Test]
        public void ASettlement_IsHonouredOnce_AgainstTheOpenRequestItNames()
        {
            var book = NewBook(out _);
            book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, 0f, out float asked, out ushort id);

            Assert.AreEqual(MarkedOutcome.Unknown, book.Settle(Other, id, asked, true, true, Capacity, 0f, 0.1f).Outcome, "another bird's report");
            Assert.AreEqual(MarkedOutcome.Unknown, book.Settle(Victim, (ushort)(id + 1), asked, true, true, Capacity, 0f, 0.1f).Outcome, "another request's id");
            Assert.IsTrue(book.Pending(0.1f), "a report that does not match leaves the request open");

            var first = book.Settle(Victim, id, asked, true, true, Capacity, 0f, 0.2f);
            Assert.AreEqual(MarkedOutcome.Reached, first.Outcome);
            var again = book.Settle(Victim, id, asked, true, true, Capacity, 0f, 0.3f);
            Assert.AreEqual(MarkedOutcome.Unknown, again.Outcome, "no double credit");
            Assert.AreEqual(0f, again.Take + again.Bounty);
        }

        [Test]
        public void ALateReport_IsRefused_AndTheFoodIsLost()
        {
            var book = NewBook(out _);
            book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, 0f, out float asked, out ushort id);
            var s = book.Settle(Victim, id, asked, true, true, Capacity, 0f, Window + 0.01f);
            Assert.AreEqual(MarkedOutcome.Unknown, s.Outcome);
            Assert.AreEqual(0f, s.Take + s.Bounty, "no pay after the window, as for every steal report");
        }

        [Test]
        public void AnUnsolicitedReport_PaysNothing()
        {
            var book = NewBook(out _);
            var s = book.Settle(Victim, 0, 5f, true, true, Capacity, 0f, 0.1f);
            Assert.AreEqual(MarkedOutcome.Unknown, s.Outcome, "no marked steal was asked: a forged report has nothing to match");
            Assert.AreEqual(0f, s.Take + s.Bounty);
        }

        [Test]
        public void AnInflatedReport_IsHeldToWhatWasAsked()
        {
            var book = NewBook(out _);
            book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, 0f, out float asked, out ushort id);
            var s = book.Settle(Victim, id, asked * 100f, true, true, Capacity, 0f, 0.1f);
            Assert.AreEqual(asked, s.Take, 1e-4f);
            Assert.AreEqual(Bounty, s.Bounty, 1e-4f, "the bounty is the offered figure, never the victim's number");
        }

        [Test]
        public void ASecondMarkedSteal_IsNotStartedWhileOneIsOpen_SoTwoCastsCannotBothBePaid()
        {
            var book = NewBook(out _);
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, 0f, out _, out ushort first));
            Assert.IsFalse(book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, 0.5f, out _, out _), "refused while the first is open");
            Assert.IsTrue(book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, 0f, Window + 0.5f, out _, out ushort later), "an unanswered one expires");
            Assert.AreNotEqual(first, later);
        }

        [Test]
        public void TheOpenRequest_HoldsTheRoomForTheBounty_AgainstTheOtherRivalsOfTheSameCast()
        {
            var book = NewBook(out var requests);
            float cargo = 0f;
            book.TryOpen(Victim, Nominal, 9f, Bounty, Capacity, cargo, 0f, out _, out _);
            // Another rival of the same cast asks for as much as it can: only what the marked steal did not reserve is left.
            Assert.IsTrue(requests.AskSteal(Other, 50f, Capacity, cargo, 0f, out float other, out _));
            Assert.AreEqual(Capacity - Nominal - Bounty, other, 1e-4f);
        }

        [Test]
        public void TheBountyAndTheTake_AreNeverMultipliedByTheBook()
        {
            // The passive's factor is applied to the nominal before the call (ResolveStealAmount); the bounty is flat.
            var book = NewBook(out _);
            book.TryOpen(Victim, Nominal * 1.6f, 99f, Bounty, 99f, 0f, 0f, out float asked, out ushort id);
            Assert.AreEqual(Nominal * 1.6f, asked, 1e-4f);
            Assert.AreEqual(Bounty, book.Settle(Victim, id, asked, true, true, 99f, 0f, 0.1f).Bounty, 1e-4f);
        }

        // ---- the one new pure rule ---------------------------------------------------------------------------------

        [TestCase(0f, 1f, true)]
        [TestCase(0.5f, 1f, true)]
        [TestCase(1f, 1f, true)]
        [TestCase(1.01f, 1f, false)]
        [TestCase(4f, 1f, false)]
        [TestCase(0f, 0f, true)]
        [TestCase(0.2f, 0f, false)]
        public void DrawDue_IsTheMomentDrawAtNames_ReadFromTheCooldownLeft(float cooldownLeft, float lead, bool due)
        {
            Assert.AreEqual(due, LootRules.DrawDue(cooldownLeft, lead));
            const float now = 12.5f;
            Assert.AreEqual(due, LootRules.DrawAt(now, now + cooldownLeft, lead) <= now + 1e-3f,
                "DrawDue and DrawAt are one rule: due exactly when the draw time has reached now");
        }

        // ---- the networked layout (ARCH-NET-13, ARCH-NET-14) -------------------------------------------------------

        [Test]
        public void LootState_HasASaneZero_ByteBacking_AndAPinnedOrdinal()
        {
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(LootState)));
            Assert.AreEqual(0, (int)LootState.Off, "the default of a networked enum is Off: nothing is marked");
            Assert.AreEqual(1, (int)LootState.Waiting);
            Assert.AreEqual(2, (int)LootState.Live);
            Assert.AreEqual(3, (int)LootState.Overdue);
        }

        [Test]
        public void ThiefLoot_NetworkedProperties_AreThePinnedSet_PrivateSet_InThatOrder()
        {
            var props = typeof(ThiefLoot).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(p => p.GetCustomAttribute<NetworkedAttribute>() != null)
                .OrderBy(p => p.MetadataToken)
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { "MarkedTarget", "State", "AgeTimer", "LootTakenEventId", "LastLootVictim", "LastLootTake", "LastLootBounty" },
                props.Select(p => p.Name).ToArray(),
                "a networked layout change is a protocol change: list it in the commit and docs/STATE.md, and check it live (ARCH-NET-14)");
            foreach (var p in props)
                Assert.IsTrue(p.SetMethod != null && p.SetMethod.IsPrivate, $"{p.Name} must be private-set: only the Thief's authority writes the mark");
            Assert.AreEqual(typeof(byte), props.Single(p => p.Name == "LootTakenEventId").PropertyType, "an event id is a [Networked] byte (ARCH-EVT-02)");
            Assert.AreEqual(typeof(LootState), props.Single(p => p.Name == "State").PropertyType);
        }

        [Test]
        public void TheChickenPrefab_CarriesThiefLoot_AndListsItAmongItsNetworkedBehaviours()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var loot = prefab.GetComponent<ThiefLoot>();
            Assert.IsNotNull(loot, "Chicken.prefab has no ThiefLoot: no Thief would ever draw a mark");
            var listed = new SerializedObject(prefab.GetComponent<NetworkObject>()).FindProperty("NetworkedBehaviours");
            bool found = false;
            for (int i = 0; i < listed.arraySize; i++)
                if (listed.GetArrayElementAtIndex(i).objectReferenceValue == loot) found = true;
            Assert.IsTrue(found, "ThiefLoot is on the prefab but not in NetworkObject.NetworkedBehaviours: Fusion would not replicate it");
        }

        // ---- who has a mark: the Thief's passive alone ----------------------------------------------------------------

        [Test]
        public void OnlyTheThiefPassive_CarriesLootFacts()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            foreach (var p in registry.Passives)
                Assert.AreEqual(p is ThiefPassiveSO, p.LootMark.IsThief, $"{p.name}: the loot mark belongs to the Thief's passive");
            foreach (var p in registry.Passives.Where(p => !(p is ThiefPassiveSO)))
                Assert.IsFalse(p.LootMark.Active, $"{p.name}: a Steal cast by any other kit must be completely unaffected");
        }

        [Test]
        public void NoLootValue_LivesOnTheStealAbility()
        {
            string snatch = RuntimeSourceGuardTests.Code(SnatchPath);
            StringAssert.DoesNotContain("Bounty", snatch, "the loot values are on the Thief passive: other kits equip Steal");
            StringAssert.DoesNotContain("LootSeconds", snatch);
            string asset = System.IO.File.ReadAllText("Assets/_Game/Data/Abilities/Snatch.asset");
            StringAssert.DoesNotContain("Loot", asset, "Snatch.asset carries no loot field");
        }

        // ---- the loot-taken event through the one observer ---------------------------------------------------------

        [Test]
        public void LootTaken_FiresOncePerBump_WithTheWrittenFields_AndNeverForALateJoinersInheritedId()
        {
            var events = new ChickenLocalEvents();
            var seen = new System.Collections.Generic.List<LootTakenEvent>();
            events.LootTaken += seen.Add;

            events.Observe(new ChickenEventSnapshot { HasLoot = true, LootTakenEventId = 9 });
            Assert.IsEmpty(seen, "the first observation seeds");

            events.Observe(new ChickenEventSnapshot { HasLoot = true, LootTakenEventId = 10, LootTake = 3f, LootBounty = 7f });
            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(3f, seen[0].Take);
            Assert.AreEqual(7f, seen[0].Bounty);

            events.Observe(new ChickenEventSnapshot { HasLoot = true, LootTakenEventId = 10, LootTake = 3f, LootBounty = 7f });
            Assert.AreEqual(1, seen.Count, "an unchanged id never fires again");

            events.Observe(new ChickenEventSnapshot { HasLoot = false, LootTakenEventId = 11 });
            Assert.AreEqual(1, seen.Count, "no ThiefLoot, no event");
        }

        [Test]
        public void LootTaken_IsObservedByTheOneObserver_AndPresentationNeverReadsTheId()
        {
            string events = RuntimeSourceGuardTests.Code(Scripts + "Gameplay/ChickenLocalEvents.cs");
            StringAssert.Contains("_lootTaken.Observe(s.LootTakenEventId)", events);
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles().Where(p => !p.EndsWith("/Gameplay/ChickenLocalEvents.cs")))
            {
                string code = RuntimeSourceGuardTests.Code(path);
                if (path.EndsWith("/Gameplay/ThiefLoot.cs"))
                {
                    // The owner declares the counter and bumps it on the authority; it may not watch it (it subscribes to LocalEvents.LootTaken instead).
                    code = System.Text.RegularExpressions.Regex.Replace(code, @"\[Networked\]\s*public byte LootTakenEventId\s*\{[^}]*\}", string.Empty);
                    code = code.Replace("LootTakenEventId++", string.Empty);
                    StringAssert.Contains("LocalEvents.LootTaken +=", code, "ThiefLoot's diagnostic log listens to the one observer");
                }
                StringAssert.DoesNotContain("LootTakenEventId", code, $"{path} reads the loot counter itself (ARCH-EVT-03)");
            }
        }

        // ---- telemetry --------------------------------------------------------------------------------------------

        [Test]
        public void TheLootTelemetryLines_AreKeyValueTokensInInvariantCulture()
        {
            Assert.AreEqual("lootdraw tick=12 thief=B1 victim=P2 candidates=3 chance=0.53", MatchTelemetry.LootDraw(12, "B1", "P2", 3, 0.526f));
            Assert.AreEqual("loottake tick=40 thief=B1 victim=P2 stolen=3.00 bounty=7.00 unfit=3.00", MatchTelemetry.LootTake(40, "B1", "P2", 3f, 7f, 3f));
            Assert.AreEqual("lootmove tick=90 thief=B1 victim=P2 age=12.00 reason=timeout", MatchTelemetry.LootMove(90, "B1", "P2", 12f, MatchTelemetry.LootMoveTimeout));
            Assert.AreEqual("lootmove tick=91 thief=B1 victim=? age=1.50 reason=gone", MatchTelemetry.LootMove(91, "B1", "?", 1.5f, MatchTelemetry.LootMoveGone));
            Assert.AreEqual("lootmove tick=92 thief=B1 victim=P3 age=4.00 reason=home", MatchTelemetry.LootMove(92, "B1", "P3", 4f, MatchTelemetry.LootMoveHome));
            Assert.AreEqual("lootmove tick=93 thief=B1 victim=P3 age=4.00 reason=empty", MatchTelemetry.LootMove(93, "B1", "P3", 4f, MatchTelemetry.LootMoveEmpty));
            Assert.AreEqual("lootmove tick=94 thief=B1 victim=P3 age=4.00 reason=light", MatchTelemetry.LootMove(94, "B1", "P3", 4f, MatchTelemetry.LootMoveLight));
        }

        [Test]
        public void EveryUnpaidEnd_HasItsOwnTelemetryReason()
        {
            Assert.AreEqual("gone", MatchTelemetry.LootMoveReason(LootEnd.Gone));
            Assert.AreEqual("home", MatchTelemetry.LootMoveReason(LootEnd.Home));
            Assert.AreEqual("empty", MatchTelemetry.LootMoveReason(LootEnd.Empty));
            Assert.AreEqual("light", MatchTelemetry.LootMoveReason(LootEnd.Light));
            Assert.AreEqual("timeout", MatchTelemetry.LootMoveReason(LootEnd.Timeout));
            var named = new System.Collections.Generic.HashSet<string>();
            foreach (LootEnd end in Enum.GetValues(typeof(LootEnd)))
                if (end != LootEnd.None) Assert.IsTrue(named.Add(MatchTelemetry.LootMoveReason(end)), $"{end} shares a reason with another end");
        }

        // ---- the behaviour (source guards: a live runner is needed to execute it) --------------------------------------

        [Test]
        public void ThiefLoot_FollowsTheDeathMarksShape_AndCallsTheSharedRules()
        {
            string src = RuntimeSourceGuardTests.Code(LootPath);
            string fixedUpdate = Body(LootPath, "public override void FixedUpdateNetwork()");
            StringAssert.StartsWith("public override void FixedUpdateNetwork()", fixedUpdate.TrimStart());
            Assert.Less(fixedUpdate.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal), fixedUpdate.IndexOf("Facts", StringComparison.Ordinal),
                "ARCH-NET-01: the authority gate comes first");
            StringAssert.Contains("SelfInject.Ensure(this, _log != null);", src);
            StringAssert.DoesNotContain("Debug.Log", src);
            StringAssert.DoesNotContain("Container.Inject(this)", src);
            foreach (var call in new[] { "LootPickRules.Filter(", "LootPickRules.Weight(", "LootPickRules.Pick(", "LootRules.Advance(", "LootRules.MayDraw(", "LootRules.Keep(", "LootRules.Eligible(", "LootPickRules.HomeFactor(", "LootPickRules.SwitchTarget(" })
                StringAssert.Contains(call, src, "the mark runs the shared pure rules, not a copy of them");
        }

        [Test]
        public void ThiefLoot_WritesTheEventFieldsBeforeBumpingTheId_AndNeverResetsIt()
        {
            string paid = Body(LootPath, "public void OnMarkedStealPaid(");
            int bump = paid.IndexOf("LootTakenEventId++", StringComparison.Ordinal);
            Assert.Greater(bump, 0);
            foreach (var field in new[] { "LastLootVictim =", "LastLootTake =", "LastLootBounty =" })
                Assert.Less(paid.IndexOf(field, StringComparison.Ordinal), bump, $"{field} is written before the bump (ARCH-EVT-02)");
            StringAssert.DoesNotContain("LootTakenEventId = 0", RuntimeSourceGuardTests.Code(LootPath));
            StringAssert.Contains("if (!IsMarking(victim))", paid, "a payment after the mark ended spends no mark");
        }

        [Test]
        public void TheMark_IsConsumedOnlyByAPaidSteal_AndTheCargoCreditsTheBountyOnlyInTheSettlement()
        {
            string cargo = RuntimeSourceGuardTests.Code(CargoPath);
            string settle = Body(CargoPath, "public void RPC_SettleMarkedSteal(");
            Assert.Less(settle.IndexOf("SpeaksFor(victim, info)", StringComparison.Ordinal), settle.IndexOf("_marked.Settle(", StringComparison.Ordinal),
                "the sender check comes before anything is settled");
            Assert.Less(settle.IndexOf("_marked.Settle(", StringComparison.Ordinal), settle.IndexOf("Cargo += gained", StringComparison.Ordinal));
            Assert.Less(settle.IndexOf("Cargo += gained", StringComparison.Ordinal), settle.IndexOf("OnMarkedStealPaid(", StringComparison.Ordinal),
                "the mark is spent after the food is credited");
            StringAssert.Contains("OnMarkedStealRefused(", settle, "a refusal leaves the mark");

            string request = Body(CargoPath, "public bool RequestMarkedSteal(");
            Assert.Less(request.IndexOf("_marked.TryOpen(", StringComparison.Ordinal), request.IndexOf("RPC_DrainStolenMarked(", StringComparison.Ordinal),
                "recorded before the call: a shared authority answers synchronously inside it");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(cargo, @"\.RPC_DrainStolenMarked\(").Count, "one sender of the marked drain");

            string drain = Body(CargoPath, "public void RPC_DrainStolenMarked(");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(drain, @"ReportMarkedRefusalToSender\(thiefId, requestId, info\)").Count,
                "an implausible amount and an unresolved thief both answer");
            StringAssert.Contains("DrainForThief(thief, amount, out bool refused)", drain, "the one drain body: a shell, a wall and an empty load behave as for any steal");
            StringAssert.Contains("RPC_SettleMarkedSteal(actual, !refused,", drain);
        }

        [Test]
        public void TheOrdinaryStealPath_StillRunsTheSameDrainBody()
        {
            string rpc = Body(CargoPath, "public void RPC_DrainStolen(");
            StringAssert.Contains("DrainForThief(thief, amount, out _)", rpc);
            string drain = Body(CargoPath, "private float DrainForThief(");
            StringAssert.Contains("PlayerBase.WallBetween(", drain);
            StringAssert.Contains("IsStealProof", drain);
            StringAssert.Contains("StealEventId++", drain);
            Assert.Less(drain.IndexOf("if (actual <= 0f) return 0f;", StringComparison.Ordinal), drain.IndexOf("StealEventId++", StringComparison.Ordinal),
                "a marked steal that asks for nothing raises no steal event");
        }

        [Test]
        public void Snatch_RoutesTheMarkedRivalThroughTheThiefsMark_AndEveryoneElseThroughTheOrdinarySteal()
        {
            string activate = Body(SnatchPath, "public override void OnActivate(");
            StringAssert.Contains("loot.TakeMarked(", activate);
            StringAssert.Contains("loot.IsMarking(", activate, "a bounty only for the caster's own marked rival");
            StringAssert.Contains("StealFrom(thief, _scratch[i], StealAmount, thief.Debts)", activate, "the ordinary steal is unchanged (it only carries the Enforcer's debts argument now, BL-226)");
            StringAssert.Contains("if (i == markedIndex) continue;", activate);

            string filter = Body(SnatchPath, "protected override bool ExtraTargetFilter(");
            StringAssert.Contains("cargo.Stealable > 0f", filter, "a carrier is still a target");
            StringAssert.Contains("loot.IsMarking(candidate)", filter, "the only addition is the caster's own marked rival");

            string take = Body(LootPath, "public bool TakeMarked(");
            StringAssert.Contains("!IsMarking(victim)", take, "no marked steal, so no bounty, from an unmarked victim");
        }

        [Test]
        public void TheRoundReset_ClearsTheMark_ThroughTheExistingResetPath()
        {
            string reset = Body(Scripts + "Gameplay/ChickenController.cs", "public void RPC_ResetControlStates(");
            StringAssert.Contains("_thiefLoot?.ResetForNewRound();", reset);
            string cargoReset = Body(CargoPath, "public void RPC_ResetForNewMatch(");
            StringAssert.Contains("_marked?.Clear();", cargoReset);
        }

        [Test]
        public void TheClockPrize_HasOneProductionCallSite_TheMatchAuthoritysAward()
        {
            int sites = RuntimeSourceGuardTests.RuntimeFiles()
                .Where(p => p.Contains("/Gameplay/") || p.Contains("/Abilities/") || p.Contains("/UI/") || p.Contains("/App/"))
                .Sum(p => System.Text.RegularExpressions.Regex.Matches(RuntimeSourceGuardTests.Code(p), @"\.MatchEndBonusFood\(").Count);
            Assert.AreEqual(1, sites, "one hook, one call site: GameManager.AwardMatchEndBonuses");
            StringAssert.Contains("passive.MatchEndBonusFood(chicken)", Body(Scripts + "Gameplay/GameManager.cs", "private void AwardMatchEndBonuses()"));
            string expiry = Body(Scripts + "Gameplay/GameManager.cs", "private void EndOnTimerExpiry()");
            Assert.Less(expiry.IndexOf("AwardMatchEndBonuses();", StringComparison.Ordinal), expiry.IndexOf("PlayerBase.ActiveBases", StringComparison.Ordinal),
                "the prize is banked before the winner is picked");
        }

        // ---- the bot (the same pure functions P0 calls) -----------------------------------------------------------------

        [Test]
        public void TheGamesBot_CallsTheSameLootFunctionsAsP0()
        {
            string bot = RuntimeSourceGuardTests.Code(BotPath);
            string p0 = RuntimeSourceGuardTests.Code(Scripts + "Balance/ScriptedPolicy.cs");
            foreach (var call in new[] { "BotLootRules.BankNow(", "BotLootRules.Pursue(", "BotLootRules.HoldSteal(", "BotLootRules.BehindWall(", "BotLootRules.Retreat(" })
            {
                StringAssert.Contains(call, bot, "the game's bot plays the loot mark by the shared rule");
                StringAssert.Contains(call, p0, "P0 plays it by the same rule");
            }
            StringAssert.Contains("_holdSteal && ability.Is(AbilityJob.Steal)", bot, "Steal is kept for the marked rival");
            StringAssert.Contains("!lootThief && cargoFraction >=", bot, "a loot Thief banks by BankNow, not by the class threshold");
            StringAssert.DoesNotContain("BotSightRules", p0);
            StringAssert.Contains("Sight(mark)", bot, "the marked rival is read through the bot's sight, never off its transform");
        }
    }
}
