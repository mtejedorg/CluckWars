using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The receiver-side steal validation that stands between <c>RpcSources.All</c> and every
    /// chicken's <c>Cargo</c>. Both of its bounds are derived at runtime from the shipped
    /// <c>AbilityRegistry.asset</c>, so these tests exist to make sure that derivation cannot
    /// quietly stop covering a real steal — the failure mode that would reject legitimate
    /// gameplay with no red test, exactly as a wrong attribution rule did in the deposit pass.
    /// </summary>
    /// <remarks>
    /// Every expectation is recomputed from the shipped assets and from
    /// <see cref="JumpResolver"/>'s own constants, never from a literal copied out of
    /// <see cref="StealRules"/> — that is how <c>BalanceOracleTests</c> managed to stay green
    /// through a 20% speed drift (docs/STATE.md).
    /// </remarks>
    public sealed class StealRulesTests
    {
        private const string AbilityScriptsDir = "Assets/_Game/Scripts/Abilities";
        private const string ScriptsRoot       = "Assets/_Game/Scripts";

        private static AbilityRegistrySO Registry() =>
            TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        /// <summary>The bound the game actually runs with — derived from the registry, as ChickenCargo does.</summary>
        private static float ShippedMaxSingleSteal() => StealRules.MaxSingleSteal(Registry().All);

        private static float ShippedMaxReach() => StealRules.MaxReach(Registry().All);

        /// <summary>
        /// Every ability asset on disk. Found as <see cref="ScriptableObject"/> and filtered in
        /// C# rather than with an <c>AssetDatabase</c> <c>t:AbilityBaseSO</c> filter, so the
        /// sweep cannot silently come back empty on an abstract base type — an empty sweep would
        /// turn every completeness check below into a no-op that passes.
        /// </summary>
        private static List<AbilityBaseSO> AllAbilityAssets() =>
            TestAssets.LoadAllIn<ScriptableObject>(TestAssets.AbilitiesDir)
                      .OfType<AbilityBaseSO>()
                      .ToList();

        // ---- The magnitude bound -------------------------------------------------

        [Test]
        public void MaxSingleSteal_CoversTheBiggestStealTheShippedPoolCanLand()
        {
            // Recomputed from the assets on disk rather than from the registry, so an ability
            // that exists but is missing from the registry shows up here as a bound that is
            // too small — which is what would silently reject its steals in a real match.
            var assets = AllAbilityAssets();
            Assert.IsNotEmpty(assets, $"No ability assets found under {TestAssets.AbilitiesDir}.");

            float biggestAmount     = assets.Max(a => a.NominalStealAmount);
            float biggestMultiplier = Mathf.Max(1f, assets.OfType<PassiveAbilitySO>()
                                                          .Select(p => p.MaxStealMultiplier)
                                                          .DefaultIfEmpty(1f)
                                                          .Max());
            float largestRealSteal = biggestAmount * biggestMultiplier;

            Assert.IsTrue(StealRules.IsPlausibleAmount(largestRealSteal, ShippedMaxSingleSteal()),
                $"The largest steal the shipped assets can produce is {largestRealSteal:0.000} " +
                $"(amount {biggestAmount:0.00} x multiplier {biggestMultiplier:0.00}) but the bound " +
                $"derived from AbilityRegistry.asset only admits {ShippedMaxSingleSteal():0.000}. Real " +
                "steals would be rejected as forgeries — most likely an ability or passive asset is " +
                "missing from the registry's All array.");
        }

        [Test]
        public void EveryStealAbilityAssetIsListedInTheRegistry()
        {
            // The bound is read off the registry, so an ability that steals but is not in it
            // contributes nothing to the maximum. If it is also the biggest stealer, every one
            // of its steals gets refused at the victim's authority.
            var registered = new HashSet<AbilityBaseSO>(Registry().All ?? System.Array.Empty<AbilityBaseSO>());

            foreach (var ability in AllAbilityAssets().Where(a => a.NominalStealAmount > 0f))
            {
                Assert.IsTrue(registered.Contains(ability),
                    $"{ability.name} steals {ability.NominalStealAmount} cargo but is not in " +
                    $"{TestAssets.AbilityRegistryPath}. ChickenCargo derives RPC_DrainStolen's bounds from " +
                    "that array, so this ability's steals would be rejected at the victim.");
            }
        }

        [Test]
        public void EveryStealScalingPassiveIsListedInTheRegistry()
        {
            var registered = new HashSet<AbilityBaseSO>(Registry().All ?? System.Array.Empty<AbilityBaseSO>());

            foreach (var passive in AllAbilityAssets().OfType<PassiveAbilitySO>()
                                                     .Where(p => p.MaxStealMultiplier > 1f))
            {
                Assert.IsTrue(registered.Contains(passive),
                    $"{passive.name} multiplies steals by {passive.MaxStealMultiplier} but is not in " +
                    $"{TestAssets.AbilityRegistryPath}, so the receiver's bound does not account for it " +
                    "and every steal it scales would be rejected.");
            }
        }

        [Test]
        public void IsPlausibleAmount_RejectsAFabricatedDrain()
        {
            float max = ShippedMaxSingleSteal();
            Assert.IsFalse(StealRules.IsPlausibleAmount(9999f, max));
            Assert.IsFalse(StealRules.IsPlausibleAmount(max + 1f, max));
        }

        [Test]
        public void IsPlausibleAmount_RejectsNonPositiveAmounts()
        {
            float max = ShippedMaxSingleSteal();
            Assert.IsFalse(StealRules.IsPlausibleAmount(0f, max));
            Assert.IsFalse(StealRules.IsPlausibleAmount(-5f, max));
        }

        [Test]
        public void MaxSingleSteal_IsZeroForAPoolThatCannotSteal()
        {
            // ChickenCargo reads a zero here as "no honest bound could be derived" and skips the
            // magnitude check rather than rejecting everything. If this ever returned something
            // non-zero for an empty pool, that graceful-degradation branch would go dead.
            Assert.AreEqual(0f, StealRules.MaxSingleSteal(null), 0.0001f);
            Assert.AreEqual(0f, StealRules.MaxSingleSteal(new AbilityBaseSO[] { null }), 0.0001f);
        }

        // ---- The reach bound -----------------------------------------------------

        [Test]
        public void MaxReach_CoversDiveBombsPositionAfterItsOwnJump()
        {
            // THE trap in this hardening pass. Dive Bomb has a Capsule aim shape, so
            // AbilityController.TryActivate resolves its lane from the take-off pose and defers
            // the teleport until after OnActivate has already sent RPC_DrainStolen. By the time
            // the victim's authority applies it, the thief is a whole jump further away than the
            // lane it was measured in. A bound built on plain ability reach rejects every real
            // Dive Bomb steal — and nothing else in the suite would notice.
            var diveBomb = AllAbilityAssets().OfType<RollTrampleAbilitySO>().FirstOrDefault();
            Assert.IsNotNull(diveBomb, "No RollTrampleAbilitySO (Dive Bomb) asset found.");

            float nominalJump = JumpResolver.GetNominalDistance(diveBomb.JumpTier);
            Assert.Greater(nominalJump, 0f,
                "Dive Bomb lost its JumpTier, so CastPoseIsUnreconstructable is now false and this " +
                "test no longer exercises the post-jump allowance it exists for.");

            float worstCase = diveBomb.ForwardOffset + diveBomb.SweepRadius
                            + nominalJump + JumpResolver.GetMaxExtension(nominalJump);

            Assert.GreaterOrEqual(ShippedMaxReach(), worstCase,
                $"Dive Bomb can be {worstCase:0.00} m from its victim when the drain lands " +
                $"(lane {diveBomb.ForwardOffset} + {diveBomb.SweepRadius}, then a jump of up to " +
                $"{nominalJump + JumpResolver.GetMaxExtension(nominalJump):0.00} m), but the bound only " +
                $"admits {ShippedMaxReach():0.00} m. Real Dive Bomb steals would be silently refused.");
        }

        [Test]
        public void MaxReach_CoversEveryOtherStealAbilitysAuthoredReach()
        {
            // The three honestly proximity-gated stealers. They resolve targets at the caster's
            // live position, so their authored AimRadius is the whole story.
            var assets = AllAbilityAssets();
            float bound = ShippedMaxReach();

            foreach (var ability in assets.Where(a => a.NominalStealAmount > 0f && !a.CastPoseIsUnreconstructable))
            {
                float reach = Mathf.Max(0f, ability.AimRadius) + Mathf.Max(0f, ability.AimForwardOffset);
                Assert.GreaterOrEqual(bound, reach,
                    $"{ability.name} reaches {reach:0.00} m but the pool bound is only {bound:0.00} m.");
            }
        }

        [Test]
        public void MaxReach_IsFarShorterThanTheArena()
        {
            // Proves the check actually bites. If the bound ever grew to arena scale it would
            // still compile, still pass every other test here, and stop constraining anything.
            float bound = ShippedMaxReach();
            Assert.Greater(bound, 0f, "No steal reach could be derived from the registry at all.");
            Assert.Less(bound, MapGenerator.ArenaHalfSize,
                $"The steal reach bound ({bound:0.00} m) is no longer small against the arena " +
                $"(half-size {MapGenerator.ArenaHalfSize:0.00} m), so it barely constrains a forged " +
                "drain from across the map.");
        }

        [Test]
        public void Reach_IgnoresTheJumpForAnAbilityWhoseCastPoseSurvives()
        {
            // The allowance is keyed off CastPoseIsUnreconstructable, not off "has a JumpTier".
            // An ability that jumps FIRST and scans after (the default ordering) is already
            // measured from where it landed, so paying it a jump of slack would be pure laxity.
            foreach (var ability in AllAbilityAssets().Where(a => a.NominalStealAmount > 0f && !a.CastPoseIsUnreconstructable))
            {
                float expected = Mathf.Max(0f, ability.AimRadius) + Mathf.Max(0f, ability.AimForwardOffset);
                Assert.AreEqual(expected, StealRules.Reach(ability), 0.0001f,
                    $"{ability.name} resolves its shape after any jump, so its reach is exactly its " +
                    "aim shape — no travel allowance.");
            }
        }

        [Test]
        public void Reach_IsZeroForAnAbilityThatDoesNotSteal()
        {
            var nonStealer = AllAbilityAssets().FirstOrDefault(a => a.NominalStealAmount <= 0f);
            Assert.IsNotNull(nonStealer, "Every shipped ability now steals, which is unexpected.");
            Assert.AreEqual(0f, StealRules.Reach(nonStealer), 0.0001f);
            Assert.AreEqual(0f, StealRules.Reach(null), 0.0001f);
        }

        [Test]
        public void MaxJumpTravel_IsZeroForATierlessAbility()
        {
            // JumpResolver.GetMaxExtension(0) is 0.4, not 0 — composing the two primitives
            // naively would hand a non-jumping ability 0.4 m of free slack.
            Assert.AreEqual(0f, StealRules.MaxJumpTravel(JumpLengthTier.None), 0.0001f);
        }

        [Test]
        public void MaxJumpTravel_IncludesTheLandingSearchTolerance()
        {
            // JumpResolver.Resolve steps outward from the nominal distance looking for a clear
            // landing, so a jump routinely travels further than its tier says.
            float nominal = JumpResolver.GetNominalDistance(JumpLengthTier.Short);
            Assert.AreEqual(nominal + JumpResolver.GetMaxExtension(nominal),
                StealRules.MaxJumpTravel(JumpLengthTier.Short), 0.0001f);
        }

        // ---- Proximity -----------------------------------------------------------

        [Test]
        public void IsWithinStealRange_AcceptsAThiefAtTheEdgeOfItsReach()
        {
            float reach = ShippedMaxReach();
            Assert.IsTrue(StealRules.IsWithinStealRange(
                Vector3.zero, new Vector3(reach * 0.99f, 0f, 0f), reach, margin: 0f));
        }

        [Test]
        public void IsWithinStealRange_IgnoresHeight()
        {
            // Same footgun ChickenCargo.HorizontalSqr documents: a chicken's pivot floats about a
            // capsule half-height up, and a jump-cleared obstacle puts one of them higher still.
            float reach = ShippedMaxReach();
            Assert.IsTrue(StealRules.IsWithinStealRange(
                Vector3.zero, new Vector3(reach * 0.99f, 6f, 0f), reach, margin: 0f));
        }

        [Test]
        public void IsWithinStealRange_RejectsADrainFromAcrossTheArena()
        {
            float reach  = ShippedMaxReach();
            float margin = StealRules.RangeMargin(2f * TopMoveSpeed(), latencySeconds: 0.25f);

            Assert.IsFalse(StealRules.IsWithinStealRange(
                Vector3.zero, new Vector3(MapGenerator.ArenaHalfSize, 0f, 0f), reach, margin),
                "A drain claimed from the arena edge must be refused even at a quarter-second of " +
                "latency with both chickens at the roster's top speed.");
        }

        [Test]
        public void RangeMargin_CoversBothEndsDriftingApartForTheMessagesFlightTime()
        {
            const float latency = 0.1f;
            float combined = 2f * TopMoveSpeed();

            Assert.AreEqual(combined * latency, StealRules.RangeMargin(combined, latency), 0.0001f,
                "The margin must be exactly how far the pair could have separated while the drain " +
                "was in the air — no more (it is slack an attacker gets too) and no less (real " +
                "steals drop).");
        }

        [Test]
        public void RangeMargin_IsZeroOnALocalInvoke()
        {
            // ChickenCargo passes latency 0 for info.IsInvokeLocal — solo play and every bot.
            Assert.AreEqual(0f, StealRules.RangeMargin(2f * TopMoveSpeed(), 0f), 0.0001f);
        }

        [Test]
        public void RangeMargin_TreatsNegativeInputsAsZero()
        {
            Assert.AreEqual(0f, StealRules.RangeMargin(-5f, -5f), 0.0001f);
        }

        // ---- Completeness: nothing may steal without declaring how much ----------

        [Test]
        public void EveryAbilityThatCallsTheDrainRpcDeclaresItsStealAmount()
        {
            // NominalStealAmount is the ONLY thing that puts an ability into the receiver's
            // bound. A new steal ability that forgets the override contributes 0, so the bound
            // never grows to cover it and every one of its steals is refused — at runtime, with
            // no compile error and no other failing test.
            foreach (var type in AbilityTypesCallingTheDrainRpc())
            {
                var property = type.GetProperty(nameof(AbilityBaseSO.NominalStealAmount));
                Assert.IsNotNull(property, $"{type.Name}: NominalStealAmount was renamed or removed.");
                Assert.AreEqual(type, property.GetGetMethod().DeclaringType,
                    $"{type.Name} calls ChickenCargo.RPC_DrainStolen but does not override " +
                    "NominalStealAmount, so it contributes nothing to the bound the victim checks " +
                    "against and its steals will be rejected. Override it with the ability's own " +
                    "authored amount field.");
            }
        }

        [Test]
        public void EveryPassiveThatScalesStealsDeclaresItsCeiling()
        {
            // The receiver has no caster to hand ModifyStealAmount — the RPC arrives on the
            // victim — so the ceiling has to be declared separately or the bound cannot see it.
            var passiveTypes = typeof(PassiveAbilitySO).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(PassiveAbilitySO).IsAssignableFrom(t));

            foreach (var type in passiveTypes)
            {
                var modify = type.GetMethod(nameof(PassiveAbilitySO.ModifyStealAmount));
                if (modify == null || modify.DeclaringType != type) continue; // does not scale steals

                var ceiling = type.GetProperty(nameof(PassiveAbilitySO.MaxStealMultiplier));
                Assert.IsNotNull(ceiling, $"{type.Name}: MaxStealMultiplier was renamed or removed.");
                Assert.AreEqual(type, ceiling.GetGetMethod().DeclaringType,
                    $"{type.Name} overrides ModifyStealAmount but not MaxStealMultiplier, so " +
                    "StealRules cannot see how large a steal it can produce and the receiver will " +
                    "reject the steals it scales.");
            }
        }

        [Test]
        public void SpineCoatIsTheOnlyStealerThatFiresFromOutsideItsOwnAbility()
        {
            // Spine Coat arms ChickenController.StealBackAmount and the drain fires later from
            // CheckCollisionSlow, so it is a real RPC_DrainStolen caller that the ability-folder
            // scan above cannot see. Pinning the set here means a SECOND such caller — which
            // would have no NominalStealAmount backing it and no bound to fit inside — cannot be
            // added without this going red.
            var callers = NonAbilityFilesCallingTheDrainRpc();
            CollectionAssert.AreEquivalent(
                new[] { "ChickenController.cs" }, callers,
                "The set of non-ability RPC_DrainStolen callers changed: " + string.Join(", ", callers) +
                ". Every caller's amount must be covered by StealRules.MaxSingleSteal, which is " +
                "derived from AbilityBaseSO.NominalStealAmount — so a caller with no ability asset " +
                "behind it has nothing putting it inside the bound.");
        }

        [Test]
        public void SpineCoatsStealBackFitsInsideTheAmountBound()
        {
            var spineCoat = AllAbilityAssets().OfType<SpineCoatAbilitySO>().FirstOrDefault();
            Assert.IsNotNull(spineCoat, "No SpineCoatAbilitySO asset found.");

            // The steal-back does NOT route through ResolveStealAmount, so no passive multiplies
            // it — the authored amount is the whole of what arrives at the victim.
            Assert.IsTrue(StealRules.IsPlausibleAmount(spineCoat.StealBackAmount, ShippedMaxSingleSteal()),
                $"Spine Coat steals back {spineCoat.StealBackAmount} on contact, above the bound " +
                $"({ShippedMaxSingleSteal():0.00}). Its steal-backs would be rejected at the victim.");
        }

        // ---- Helpers -------------------------------------------------------------

        private static IEnumerable<System.Type> AbilityTypesCallingTheDrainRpc()
        {
            var assembly = typeof(AbilityBaseSO).Assembly;
            var found = new List<System.Type>();

            foreach (var path in SourceFilesCallingTheDrainRpc(AbilityScriptsDir))
            {
                string typeName = Path.GetFileNameWithoutExtension(path);
                var type = assembly.GetType($"{typeof(AbilityBaseSO).Namespace}.{typeName}");
                Assert.IsNotNull(type,
                    $"{path} calls RPC_DrainStolen but no type '{typeName}' exists in " +
                    $"{typeof(AbilityBaseSO).Namespace} — the file and its class have drifted apart, " +
                    "so this completeness check can no longer see it.");
                found.Add(type);
            }

            Assert.IsNotEmpty(found,
                $"No file under {AbilityScriptsDir} calls RPC_DrainStolen. Either every steal ability " +
                "was removed or the RPC was renamed and this check has gone blind.");
            return found;
        }

        private static string[] NonAbilityFilesCallingTheDrainRpc() =>
            SourceFilesCallingTheDrainRpc(ScriptsRoot)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains(AbilityScriptsDir + "/"))
                .Where(p => !p.EndsWith("/ChickenCargo.cs"))     // declares the RPC
                .Where(p => !p.Contains("/Editor/"))             // the suite itself, incl. this file
                .Select(Path.GetFileName)
                .ToArray();

        /// <summary>
        /// Source files under <paramref name="root"/> that <b>call</b> <c>RPC_DrainStolen</c>.
        /// Comment lines are skipped so the several XML docs that name the RPC do not read as
        /// call sites; the <c>Editor/</c> tree is excluded by the caller because this very file
        /// carries the search string in a literal and would otherwise report itself.
        /// </summary>
        private static IEnumerable<string> SourceFilesCallingTheDrainRpc(string root)
        {
            Assert.IsTrue(Directory.Exists(root), $"{root} not found on disk.");

            foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                bool calls = File.ReadLines(path).Any(line =>
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//")) return false;
                    return trimmed.Contains("RPC_DrainStolen(");
                });
                if (calls) yield return path;
            }
        }

        private static float TopMoveSpeed()
        {
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);
            Assert.IsNotEmpty(stats, "No ChickenStatsSO assets found.");
            return stats.Max(s => s.MoveSpeed);
        }
    }
}
