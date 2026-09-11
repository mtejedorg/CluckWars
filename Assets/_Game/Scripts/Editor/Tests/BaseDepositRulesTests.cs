using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;
// NUnit.Framework.RangeAttribute collides with UnityEngine's — same shape as the
// Assert / LogLevel collisions in docs/CONVENTIONS.md. We want the inspector one.
using RangeAttribute = UnityEngine.RangeAttribute;

namespace CluckWars.Tests
{
    /// <summary>
    /// The receiver-side deposit validation that stands between <c>RpcSources.All</c> and
    /// <c>PlayerBase.FoodTotal</c>. Every expectation is derived from the shipped
    /// <c>MatchConfig.asset</c>, the shipped ability assets and the shipped
    /// <c>PlayerBase.prefab</c> — never from a literal copied out of the implementation, which
    /// is how <c>BalanceOracleTests</c> managed to stay green through a 20% speed drift
    /// (docs/STATE.md).
    /// </summary>
    public sealed class BaseDepositRulesTests
    {
        private static MatchConfigSO Config() => TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

        private static float DepositRadius()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.PlayerBasePrefabPath);
            var playerBase = prefab.GetComponent<PlayerBase>();
            Assert.IsNotNull(playerBase, $"{TestAssets.PlayerBasePrefabPath} has no PlayerBase component.");
            return TestAssets.PrivateField<float>(playerBase, "_depositRadius");
        }

        // ---- The magnitude bound -------------------------------------------------

        [Test]
        public void MaxSingleDeposit_CannotBankAWinInOneCall()
        {
            // The whole point of the bound. A single forged RPC must never be able to end the
            // match, no matter what the deposit rate is tuned to.
            var cfg = Config();
            float max = BaseDepositRules.MaxSingleDeposit(cfg.DepositRatePerSecond);

            Assert.Less(max, cfg.FoodTargetToWin,
                $"One RPC_AddFood may carry up to {max:0.00} food but the win target is only " +
                $"{cfg.FoodTargetToWin} — a single call would win the match outright. Either the " +
                "deposit rate or the flush window has grown past what the bound can safely allow.");
        }

        [Test]
        public void MaxSingleDeposit_AcceptsTheLargestLegitimateFlush()
        {
            // ChickenCargo batches at most FlushSeconds worth of transfer into one RPC, at a
            // rate that abilities may multiply. If the bound were tighter than that, real
            // deposits from a Quick Drop burst would be rejected and food would vanish.
            var cfg = Config();
            float largestRealFlush =
                cfg.DepositRatePerSecond * MaxAuthoredDepositRateMultiplier() * BaseDepositRules.FlushSeconds;

            Assert.IsTrue(
                BaseDepositRules.IsPlausibleAmount(largestRealFlush, cfg.DepositRatePerSecond),
                $"The largest legitimate flush is {largestRealFlush:0.000} food but the bound only " +
                $"admits {BaseDepositRules.MaxSingleDeposit(cfg.DepositRatePerSecond):0.000}. Real " +
                "deposits would be rejected as forgeries.");
        }

        [Test]
        public void MaxDepositRateMultiplier_CoversEveryAuthoredAbility()
        {
            // The bound is derived from this ceiling, so a new deposit-rate ability authored
            // above it would silently make legitimate deposits unbankable.
            foreach (var ability in TestAssets.LoadAllIn<QuickDropAbilitySO>(TestAssets.AbilitiesDir))
            {
                Assert.LessOrEqual(ability.DepositRateMultiplier, BaseDepositRules.MaxDepositRateMultiplier,
                    $"{ability.name} multiplies the deposit rate by {ability.DepositRateMultiplier}, above " +
                    $"BaseDepositRules.MaxDepositRateMultiplier ({BaseDepositRules.MaxDepositRateMultiplier}). " +
                    "Raise the constant (and re-check MaxSingleDeposit_CannotBankAWinInOneCall) or lower the ability.");
            }
        }

        [Test]
        public void MaxDepositRateMultiplier_MatchesTheInspectorCeiling()
        {
            // The [Range] is what stops a designer typing a bigger number into the inspector,
            // so it — not any authored value — is the real upper bound the receiver must admit.
            var field = typeof(QuickDropAbilitySO).GetField(nameof(QuickDropAbilitySO.DepositRateMultiplier));
            Assert.IsNotNull(field, "QuickDropAbilitySO.DepositRateMultiplier was renamed or removed.");

            var range = field.GetCustomAttributes(typeof(RangeAttribute), false).Cast<RangeAttribute>().FirstOrDefault();
            Assert.IsNotNull(range, "QuickDropAbilitySO.DepositRateMultiplier lost its [Range] — nothing now " +
                                    "caps what a designer can type in, so the deposit bound has no ceiling to derive from.");

            Assert.AreEqual(BaseDepositRules.MaxDepositRateMultiplier, range.max, 0.001f,
                $"The inspector allows up to {range.max}x deposit rate but BaseDepositRules assumes " +
                $"{BaseDepositRules.MaxDepositRateMultiplier}x. The two must agree or the receiver will " +
                "reject deposits the designer can legitimately author.");
        }

        [Test]
        public void IsPlausibleAmount_RejectsAFabricatedWin()
        {
            var cfg = Config();
            Assert.IsFalse(BaseDepositRules.IsPlausibleAmount(9999f, cfg.DepositRatePerSecond));
            Assert.IsFalse(BaseDepositRules.IsPlausibleAmount(cfg.FoodTargetToWin, cfg.DepositRatePerSecond));
        }

        [Test]
        public void IsPlausibleAmount_RejectsNonPositiveAmounts()
        {
            var cfg = Config();
            Assert.IsFalse(BaseDepositRules.IsPlausibleAmount(0f, cfg.DepositRatePerSecond));
            Assert.IsFalse(BaseDepositRules.IsPlausibleAmount(-5f, cfg.DepositRatePerSecond));
        }

        [Test]
        public void IsPlausibleAmount_AcceptsAnOrdinaryFlush()
        {
            var cfg = Config();
            float ordinary = cfg.DepositRatePerSecond * BaseDepositRules.FlushSeconds;
            Assert.IsTrue(BaseDepositRules.IsPlausibleAmount(ordinary, cfg.DepositRatePerSecond),
                $"An unbuffed full flush ({ordinary:0.000}) must always be accepted.");
        }

        // ---- Proximity -----------------------------------------------------------

        [Test]
        public void IsWithinDepositRange_AcceptsAChickenParkedAtTheBase()
        {
            float radius = DepositRadius();
            Assert.IsTrue(BaseDepositRules.IsWithinDepositRange(
                Vector3.zero, new Vector3(radius * 0.9f, 0f, 0f), radius, margin: 0f));
        }

        [Test]
        public void IsWithinDepositRange_IgnoresHeight()
        {
            // A chicken's pivot sits about a capsule half-height above the base's. Measuring in
            // 3D would push an in-range chicken outside the tuned radius — the footgun
            // ChickenCargo.HorizontalSqr documents.
            float radius = DepositRadius();
            Assert.IsTrue(BaseDepositRules.IsWithinDepositRange(
                Vector3.zero, new Vector3(radius * 0.9f, 5f, 0f), radius, margin: 0f));
        }

        [Test]
        public void IsWithinDepositRange_RejectsAChickenBeyondTheRadiusAndItsDriftMargin()
        {
            float radius = DepositRadius();
            float margin = BaseDepositRules.RangeMargin(TopMoveSpeed(), latencySeconds: 0.1f);
            float justOutside = radius + margin + PinwheelLayout.ChickenDiameter;

            Assert.IsFalse(BaseDepositRules.IsWithinDepositRange(
                Vector3.zero, new Vector3(justOutside, 0f, 0f), radius, margin),
                $"A chicken a full body past the deposit radius plus its drift margin " +
                $"({justOutside:0.00} m out) must not be able to bank, even at the roster's top speed.");
        }

        [Test]
        public void RangeMargin_IsZeroForAStationaryChickenOnALocalInvoke()
        {
            Assert.AreEqual(0f, BaseDepositRules.RangeMargin(moveSpeed: 0f, latencySeconds: 0f), 0.0001f);
        }

        [Test]
        public void RangeMargin_CoversTheDistanceTheFastestClassCanTravelWhileTheFlushIsInFlight()
        {
            float topSpeed = TopMoveSpeed();
            const float latency = 0.1f;

            float margin = BaseDepositRules.RangeMargin(topSpeed, latency);
            Assert.AreEqual(topSpeed * (BaseDepositRules.FlushSeconds + latency), margin, 0.0001f,
                "The margin must be exactly how far the depositor could have walked since the batch " +
                "accrued — no more (it is slack an attacker gets too) and no less (real deposits drop).");
        }

        [Test]
        public void RangeMargin_TreatsNegativeInputsAsZero()
        {
            Assert.AreEqual(0f, BaseDepositRules.RangeMargin(-5f, -5f), 0.0001f);
        }

        // ---- Allegiance ----------------------------------------------------------

        [Test]
        public void IsAllegianceMatch_AcceptsOnlyTheChickensOwnCorner()
        {
            Assert.IsTrue(BaseDepositRules.IsAllegianceMatch(2, 2));
            Assert.IsFalse(BaseDepositRules.IsAllegianceMatch(2, 3));
        }

        [Test]
        public void IsAllegianceMatch_RejectsAnUnstampedChicken()
        {
            // HomeCornerIndex defaults to -1 until MatchBootstrapper stamps it. A chicken with
            // no match identity has no base to bank into, and -1 must not pair with a base
            // whose CornerIndex is somehow also -1.
            Assert.IsFalse(BaseDepositRules.IsAllegianceMatch(-1, -1));
            Assert.IsFalse(BaseDepositRules.IsAllegianceMatch(-1, 0));
        }

        // ---- Sender / receiver agreement ----------------------------------------

        [Test]
        public void ChickenCargo_BatchesAgainstTheSharedFlushWindow()
        {
            // The receiver's bound is FlushSeconds worth of deposit. If the sender goes back to
            // its own literal, one side can grow without the other and real deposits start
            // getting rejected as forgeries — with no compile error to catch it.
            const string cargoPath = "Assets/_Game/Scripts/Gameplay/ChickenCargo.cs";
            Assert.IsTrue(System.IO.File.Exists(cargoPath), $"{cargoPath} not found on disk.");

            string source = System.IO.File.ReadAllText(cargoPath);
            Assert.IsTrue(source.Contains("BaseDepositRules.FlushSeconds"),
                "ChickenCargo no longer derives its flush window from BaseDepositRules.FlushSeconds. " +
                "The sender's batch size and the receiver's plausible-amount bound must come from " +
                "the same constant.");
        }

        private static float TopMoveSpeed()
        {
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);
            Assert.IsNotEmpty(stats, "No ChickenStatsSO assets found.");
            return stats.Max(s => s.MoveSpeed);
        }

        private static float MaxAuthoredDepositRateMultiplier()
        {
            var abilities = TestAssets.LoadAllIn<QuickDropAbilitySO>(TestAssets.AbilitiesDir);
            return abilities.Count == 0 ? 1f : abilities.Max(a => a.DepositRateMultiplier);
        }
    }
}
