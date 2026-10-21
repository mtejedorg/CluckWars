using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-050: an Assassin execute must pay. Locks the ids <c>AssassinExecute.Press</c> hands to
    /// each RPC against the type its receiver resolves, the side of the wire the loot is banked
    /// on, the steal-proof gate, and the bounded loot report that stops a doorstep execute from
    /// paying the assassin for food the victim also banked.
    /// </summary>
    /// <remarks>
    /// <b>Why most of these are structural.</b> <c>TryFindBehaviour&lt;T&gt;</c> and every
    /// authority check need a live <c>NetworkRunner</c>, which EditMode has none of. But the
    /// property that makes a lookup succeed is decidable without one: the id must come from an
    /// instance of the type the receiver asks for. The ledger maths that bounds the loot is pure
    /// and is exercised for real.
    /// <para>
    /// The two ids are deliberately DIFFERENT types. <c>RPC_TransferAllToBountyBag</c> resolves an
    /// <c>AssassinExecute</c> (it checks the mark, <c>TryResolveExecutor</c>, 7cb18c6), so Press
    /// passes its own <c>Id</c> there; <c>CreditKillToAttacker</c> resolves a
    /// <c>ChickenCombat</c>, so Press passes <c>_combat.Id</c>. The unmerged
    /// <c>fix/assassin-execute-credit</c> branch predates 7cb18c6 and passed <c>_cargo.Id</c> to
    /// the transfer, which on this code would be rejected on every execute.
    /// </para>
    /// </remarks>
    public sealed class AssassinExecuteCreditTests
    {
        private const string AssassinExecutePath = "Assets/_Game/Scripts/Gameplay/AssassinExecute.cs";
        private const string ChickenCargoPath    = "Assets/_Game/Scripts/Gameplay/ChickenCargo.cs";
        private const string ChickenCombatPath   = "Assets/_Game/Scripts/Gameplay/ChickenCombat.cs";

        private const string TransferCall = "RPC_TransferAllToBountyBag(";
        private const string RemovalCall  = "RPC_ExecuteRemoval(";

        // A write to the bag (=, +=, -=), not a mention: the transfer RPC's own name contains it.
        private const string BountyBagWrite = @"\bBountyBag\s*[+\-]?=(?!=)";

        // ---- 1. The ids Press hands out, against what the receivers resolve -------------

        [Test]
        public void ExecuteRemovalRpc_IsHandedAChickenCombatsOwnBehaviourId()
        {
            string argument = FirstArgumentOfSoleCallTo(AssassinExecutePath, RemovalCall);
            var match = Regex.Match(argument, @"^(_?[A-Za-z]\w*)\.Id$");
            Assert.IsTrue(match.Success && match.Groups[1].Value != "this",
                $"AssassinExecute.Press passes '{argument}' to RPC_ExecuteRemoval. CreditKillToAttacker " +
                "resolves that id with TryFindBehaviour<ChickenCombat>, so a bare 'Id' (AssassinExecute's " +
                "own behaviour id - the CW-050 bug) never resolves and no execute scores a kill. Pass the " +
                "cached sibling: '_combat.Id'.");

            var field = typeof(AssassinExecute).GetField(match.Groups[1].Value,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(field, $"AssassinExecute has no field '{match.Groups[1].Value}'.");
            Assert.AreEqual(typeof(ChickenCombat), field.FieldType,
                $"'{field.Name}' is a {field.FieldType.Name}, not the ChickenCombat CreditKillToAttacker resolves.");
        }

        [Test]
        public void KillCredit_StillResolvesItsIdAsAChickenCombat()
        {
            string body = MethodBody(ChickenCombatPath, "private void CreditKillToAttacker(");
            StringAssert.IsMatch(@"TryFindBehaviour\([^)]*out\s+ChickenCombat\b", body,
                "CreditKillToAttacker no longer resolves the attacker as a ChickenCombat. Press's " +
                "argument is asserted against that exact type - change both together.");
        }

        [Test]
        public void TransferRpc_IsHandedThisAssassinExecutesOwnId_WhichIsWhatTheExecutorCheckResolves()
        {
            Assert.AreEqual("Id", FirstArgumentOfSoleCallTo(AssassinExecutePath, TransferCall),
                "RPC_TransferAllToBountyBag validates its caller through TryResolveExecutor, which " +
                "resolves an AssassinExecute and checks its MarkedTarget. Any other behaviour's id " +
                "(e.g. '_cargo.Id', as the pre-7cb18c6 fix branch passed) is rejected on every execute.");

            string guard = MethodBody(ChickenCargoPath, "private bool TryResolveExecutor(");
            StringAssert.IsMatch(@"TryFindBehaviour\([^)]*out\s+AssassinExecute\b", guard,
                "TryResolveExecutor no longer resolves an AssassinExecute; Press's 'Id' argument is " +
                "asserted against that type - change both together.");
        }

        // ---- 2. Which side of the wire the loot is banked on ----------------------------

        [Test]
        public void TransferRpc_NeverWritesBountyBag_AndReportsTheDrainBackAfterZeroing()
        {
            string body = MethodBody(ChickenCargoPath, "public void RPC_TransferAllToBountyBag(");

            Assert.IsFalse(Regex.IsMatch(body, BountyBagWrite),
                "RPC_TransferAllToBountyBag writes BountyBag again. It runs on the VICTIM's authority, " +
                "where the assassin's cargo is a proxy: a [Networked] write there never replicates and " +
                "the loot vanishes in Shared-mode PvP. Report the drain via RPC_CreditExecuteLoot.");

            int zero   = body.IndexOf("Cargo = 0f", StringComparison.Ordinal);
            int report = body.IndexOf(".RPC_CreditExecuteLoot(", StringComparison.Ordinal);
            Assert.Greater(zero, 0, "the transfer no longer zeroes the victim's cargo");
            Assert.Greater(report, zero,
                "the loot report must be sent after the drain, carrying the amount the victim's " +
                "authority actually zeroed - that is what makes it impossible to pay for banked food");
        }

        [Test]
        public void CreditExecuteLoot_ValidatesTheSender_AndConsumesAPendingExecute_BeforeBanking()
        {
            var method = typeof(ChickenCargo).GetMethod(nameof(ChickenCargo.RPC_CreditExecuteLoot));
            Assert.IsNotNull(method);
            Assert.IsTrue(method.GetParameters().Any(p => p.ParameterType == typeof(Fusion.RpcInfo)),
                "RPC_CreditExecuteLoot is RpcSources.All; without RpcInfo it cannot tell who is reporting.");
            Assert.IsTrue(method.GetCustomAttributes(typeof(Fusion.RpcAttribute), false).Any(),
                "RPC_CreditExecuteLoot must stay an RPC targeted at the assassin's StateAuthority.");

            string body = MethodBody(ChickenCargoPath, "public void RPC_CreditExecuteLoot(");
            int sender  = body.IndexOf("victim.Object.StateAuthority == info.Source", StringComparison.Ordinal);
            int local   = body.IndexOf("victim.Object.HasStateAuthority", StringComparison.Ordinal);
            int consume = body.IndexOf("StealRules.TryResolveExecuteLoot(_pendingExecutes, victimId", StringComparison.Ordinal);
            int bank    = body.IndexOf("BountyBag += allowed", StringComparison.Ordinal);

            Assert.Greater(sender, 0, "the wire sender must be the victim's state authority");
            Assert.Greater(local, 0, "a local invoke must have the victim simulated here");
            Assert.Greater(consume, Math.Max(sender, local), "the pending record is consumed only after attribution");
            Assert.Greater(bank, consume, "bank only what the ledger allowed, never the raw report");
            StringAssert.DoesNotContain("BountyBag += amount", body, "the raw report must never be banked");
        }

        [Test]
        public void Press_PaysOnlyTheFlatBountyItself_TheLootArrivesByReport()
        {
            string press = MethodBody(AssassinExecutePath, "public bool Press(");
            var credits = Regex.Matches(press, @"BountyBag\s*\+=\s*([^;]+);").Cast<Match>().ToArray();
            Assert.AreEqual(1, credits.Length,
                "Press must write the bounty bag exactly once - the flat execute bounty. Crediting the " +
                "victim's cargo here reads a lagging proxy and double-pays food the victim banked " +
                "while stunned in its own base (CW-050).");
            Assert.AreEqual("bounty", credits[0].Groups[1].Value.Trim());
        }

        [Test]
        public void Press_ValidatesTheVictimBeforeAnyPayoutOrRpc()
        {
            string press = MethodBody(AssassinExecutePath, "public bool Press(");
            int guard    = press.IndexOf("target.Combat.IsRemoved", StringComparison.Ordinal);
            int transfer = press.IndexOf(TransferCall, StringComparison.Ordinal);
            int removal  = press.IndexOf(RemovalCall, StringComparison.Ordinal);
            int bounty   = press.IndexOf("BountyBag +=", StringComparison.Ordinal);

            Assert.Greater(guard, 0, "Press no longer refuses an incomplete or already-removed victim");
            Assert.Greater(transfer, guard);
            Assert.Greater(removal, guard);
            Assert.Greater(bounty, guard, "a corpse or a broken prefab must not pay the execute bounty");
        }

        // ---- 3. Steal-proof victims keep their cargo, the execute still lands ------------

        [Test]
        public void Press_StealProofGate_WrapsOnlyTheCargoHalf()
        {
            string press = MethodBody(AssassinExecutePath, "public bool Press(");

            int gate = press.IndexOf("StealRules.CanStealFrom(target)", StringComparison.Ordinal);
            Assert.Greater(gate, 0,
                "Press dropped the steal-proof gate: an Egg Shell / Turtle Mode victim would be asked " +
                "to hand its cargo over (the fix branch made exactly this regression).");

            string gated = FirstBlockAfter(press, gate);
            int expect   = gated.IndexOf("ExpectExecuteLoot(target)", StringComparison.Ordinal);
            int transfer = gated.IndexOf(TransferCall, StringComparison.Ordinal);
            Assert.GreaterOrEqual(expect, 0, "the loot expectation must be recorded only for a stealable victim");
            Assert.Greater(transfer, expect,
                "record the expectation BEFORE the transfer: on a shared authority the drain and its " +
                "report back run synchronously inside the call");

            StringAssert.DoesNotContain(RemovalCall, gated, "a shelled victim is still executed");
            Assert.IsFalse(Regex.IsMatch(gated, BountyBagWrite), "a shelled victim still pays the flat bounty");
            StringAssert.Contains(RemovalCall, press);
            StringAssert.Contains("ResolveExecuteBounty(target)", press);
        }

        // ---- 4. The loot report is bounded and conserved --------------------------------
        // These call the same StealRules helpers ChickenCargo.ExpectExecuteLoot and
        // RPC_CreditExecuteLoot call (pinned by the first test below), on a ledger built by the
        // same factory, so they exercise the production amount logic rather than restating it.

        private const int Victim = 7;
        private const int Bystander = 9;

        [Test]
        public void ChickenCargo_RoutesBothHalvesThroughTheSharedExecuteLootHelpers()
        {
            string cargo = string.Join("\n", CodeLines(ChickenCargoPath));
            StringAssert.Contains("_pendingExecutes = StealRules.NewExecuteLootLedger<NetworkBehaviourId>()", cargo,
                "the execute ledger must be built by the factory, which carries its longer window");
            StringAssert.Contains("StealRules.RecordExecuteExpectation(_pendingExecutes",
                MethodBody(ChickenCargoPath, "public void ExpectExecuteLoot("));
            StringAssert.Contains("StealRules.IsPlausibleLootReport(amount)",
                MethodBody(ChickenCargoPath, "public void RPC_CreditExecuteLoot("));
        }

        [Test]
        public void ExecuteLootWindow_IsLongerThanTheRefundWindow()
        {
            var ledger = StealRules.NewExecuteLootLedger<int>();
            Assert.AreEqual(StealRules.ExecuteLootWindowSeconds, ledger.Window, 1e-5f);
            Assert.Greater(ledger.Window, PendingStealLedger<int>.WindowSeconds,
                "the loot report makes two relayed trips in Shared Mode; a refund-length window drops real loot");
        }

        [TestCase(10f, 4f, 10f)]
        [TestCase(10f, 12f, 12f)]   // transient over-capacity the assassin can see
        [TestCase(0f, 0f, 0f)]
        [TestCase(-1f, -2f, 0f)]
        public void ExecuteLootCap_IsCapacityOrVisibleCargo_NeverNegative(float capacity, float visible, float expected) =>
            Assert.AreEqual(expected, StealRules.ExecuteLootCap(capacity, visible), 1e-5f);

        [Test]
        public void ZeroCap_RecordsNoExpectation_SoTheReportIsRefused()
        {
            var ledger = StealRules.NewExecuteLootLedger<int>();
            Assert.IsFalse(StealRules.RecordExecuteExpectation(ledger, Victim, 0f, 0f, 0f, out float cap));
            Assert.AreEqual(0f, cap);
            Assert.IsFalse(StealRules.TryResolveExecuteLoot(ledger, Victim, 6f, 0.1f, out float credited));
            Assert.AreEqual(0f, credited);
        }

        [Test]
        public void DoorstepExecute_PaysOnlyWhatWasStillCarried_NotWhatWasBanked()
        {
            // The assassin sees the victim carrying 10 when it presses. Before the drain lands the
            // stunned victim, standing in its own base, banks 6. The drain zeroes the 4 left and
            // reports 4. Old behaviour credited the proxy-read 10: 16 food from a 10-food load.
            const float seen = 10f, banked = 6f, capacity = 12f;
            var ledger = StealRules.NewExecuteLootLedger<int>();
            Assert.IsTrue(StealRules.RecordExecuteExpectation(ledger, Victim, capacity, seen, 0f, out _));

            float reported = seen - banked;
            Assert.IsTrue(StealRules.TryResolveExecuteLoot(ledger, Victim, reported, 0.2f, out float looted));
            Assert.AreEqual(seen, banked + looted, 1e-5f, "the load is conserved: banked + looted == carried");
        }

        [Test]
        public void LootReport_IsClampedToTheCap_AndPaysOnce()
        {
            var ledger = StealRules.NewExecuteLootLedger<int>();
            StealRules.RecordExecuteExpectation(ledger, Victim, 10f, 3f, 0f, out _);

            Assert.IsTrue(StealRules.TryResolveExecuteLoot(ledger, Victim, 999f, 0.2f, out float credited), "a real execute is paid");
            Assert.AreEqual(10f, credited, 1e-5f, "an inflated report is clamped to one full load");
            Assert.IsFalse(StealRules.TryResolveExecuteLoot(ledger, Victim, 5f, 0.3f, out _), "a replayed report pays nothing");
        }

        [TestCase(0f)]
        [TestCase(-3f)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NaN)]
        public void LootReport_WithAnImplausibleAmount_PaysNothing_AndKeepsTheExpectation(float reported)
        {
            var ledger = StealRules.NewExecuteLootLedger<int>();
            StealRules.RecordExecuteExpectation(ledger, Victim, 10f, 0f, 0f, out _);

            Assert.IsFalse(StealRules.TryResolveExecuteLoot(ledger, Victim, reported, 0.1f, out float credited));
            Assert.AreEqual(0f, credited);
            Assert.IsTrue(StealRules.TryResolveExecuteLoot(ledger, Victim, 4f, 0.2f, out _),
                "a garbage report must not burn the genuine one");
        }

        [Test]
        public void LootReport_FromAnUnmarkedVictim_OrAfterTheWindow_PaysNothing()
        {
            var ledger = StealRules.NewExecuteLootLedger<int>();
            StealRules.RecordExecuteExpectation(ledger, Victim, 10f, 0f, 0f, out _);

            Assert.IsFalse(StealRules.TryResolveExecuteLoot(ledger, Bystander, 5f, 0.2f, out _),
                "only the executed victim may report");
            Assert.IsTrue(StealRules.TryResolveExecuteLoot(ledger, Victim, 5f, PendingStealLedger<int>.WindowSeconds + 0.5f, out _),
                "a report past the refund window but inside the execute window still pays");

            StealRules.RecordExecuteExpectation(ledger, Victim, 10f, 0f, 0f, out _);
            Assert.IsFalse(StealRules.TryResolveExecuteLoot(ledger, Victim, 5f, StealRules.ExecuteLootWindowSeconds + 0.5f, out _),
                "a stale report outside the execute window pays nothing");
        }

        // ---- Helpers ---------------------------------------------------------------------

        /// <summary>
        /// First argument of the single call to <paramref name="call"/> in <paramref name="path"/>.
        /// Insisting on exactly one is part of the check: a second call site would be a second
        /// chance to pass the wrong id, unseen by this test.
        /// </summary>
        private static string FirstArgumentOfSoleCallTo(string path, string call)
        {
            var sites = CodeLines(path).Where(l => l.Contains(call)).ToArray();
            Assert.AreEqual(1, sites.Length,
                $"Expected exactly one call to {call} in {path}, found {sites.Length}: " +
                string.Join(" | ", sites.Select(l => l.Trim())));

            string tail = sites[0].Substring(sites[0].IndexOf(call, StringComparison.Ordinal) + call.Length);
            int end = tail.IndexOfAny(new[] { ',', ')' });
            Assert.Greater(end, 0, $"Could not read the first argument of {call}: '{sites[0].Trim()}'.");
            return tail.Substring(0, end).Trim();
        }

        /// <summary>Source lines with comments removed, so prose naming a call never reads as one.</summary>
        private static string[] CodeLines(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} not found on disk.");
            return File.ReadAllLines(path)
                .Select(l =>
                {
                    int slash = l.IndexOf("//", StringComparison.Ordinal);
                    return slash >= 0 ? l.Substring(0, slash) : l;
                })
                .ToArray();
        }

        /// <summary>
        /// Body of the method whose declaration contains <paramref name="signature"/>, by brace
        /// matching. Interpolated strings in these files keep their braces balanced. Internal so
        /// other source-pinning tests (StealProofTests) can anchor on a whole method instead of a
        /// fixed character span that silently drifts as the method grows.
        /// </summary>
        internal static string MethodBody(string path, string signature)
        {
            var lines = CodeLines(path);
            int start = Array.FindIndex(lines, l => l.Contains(signature));
            Assert.GreaterOrEqual(start, 0, $"No method declaration matching '{signature}' in {path}.");

            var body = new StringBuilder();
            int depth = 0;
            bool opened = false;
            for (int i = start; i < lines.Length; i++)
            {
                foreach (char c in lines[i])
                {
                    if (c == '{') { depth++; opened = true; }
                    else if (c == '}') depth--;
                }
                body.AppendLine(lines[i]);
                if (opened && depth == 0) return body.ToString();
            }

            Assert.Fail($"Braces never balanced while reading '{signature}' out of {path}.");
            return null;
        }

        /// <summary>The first <c>{ ... }</c> block that opens after <paramref name="from"/>.</summary>
        private static string FirstBlockAfter(string src, int from)
        {
            int open = src.IndexOf('{', from);
            Assert.Greater(open, from, "no block follows the steal-proof gate");
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
            }
            Assert.Fail("the block after the steal-proof gate never closes");
            return null;
        }
    }
}
