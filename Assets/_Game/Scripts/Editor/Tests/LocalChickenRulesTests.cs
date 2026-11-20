using System.IO;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A10 (architecture audit 2026-11-12): one rule for "the chicken this peer drives", and no copy of it left behind.
    /// </summary>
    public sealed class LocalChickenRulesTests
    {
        private static LocalChickenCandidate C(bool valid = true, bool input = true, bool decoy = false, bool bot = false) =>
            new LocalChickenCandidate(valid, input, decoy, bot);

        [Test]
        public void NoCandidates_IsNoLocalChicken() =>
            Assert.AreEqual(-1, LocalChickenRules.Pick(new LocalChickenCandidate[0]));

        [Test]
        public void RemoteChickens_AreNeverLocal() =>
            Assert.AreEqual(-1, LocalChickenRules.Pick(new[] { C(input: false), C(input: false) }));

        [Test]
        public void TheOneWithInputAuthority_IsPicked() =>
            Assert.AreEqual(2, LocalChickenRules.Pick(new[] { C(input: false), C(input: false), C(), C(input: false) }));

        [Test]
        public void ADecoyThatSharesInputAuthority_IsSkipped_WhicheverComesFirst()
        {
            // A Doppelganger decoy shares its caster's input authority; the registry order is spawn order, not a rule.
            Assert.AreEqual(1, LocalChickenRules.Pick(new[] { C(decoy: true), C() }));
            Assert.AreEqual(0, LocalChickenRules.Pick(new[] { C(), C(decoy: true) }));
        }

        [Test]
        public void OnlyADecoyLeft_IsNoLocalChicken() =>
            Assert.AreEqual(-1, LocalChickenRules.Pick(new[] { C(decoy: true), C(input: false) }));

        [Test]
        public void ADespawnedObject_IsSkipped() =>
            Assert.AreEqual(1, LocalChickenRules.Pick(new[] { C(valid: false), C() }));

        [Test]
        public void AHuman_WinsOverABotWithInputAuthority_AndTheBotIsTheFallback()
        {
            Assert.AreEqual(1, LocalChickenRules.Pick(new[] { C(bot: true), C() }));
            Assert.AreEqual(0, LocalChickenRules.Pick(new[] { C(bot: true), C(input: false) }));
        }

        [Test]
        public void NoCopyOfTheRule_ScansTheSceneForInputAuthority()
        {
            // Source guard: the copies this replaced each scanned for HasInputAuthority with a different filter.
            foreach (string file in new[]
                     {
                         "Visuals/MatchCamera.cs", "Input/TouchControlsController.cs",
                         "UI/MatchHud.cs", "UI/MatchHudController.cs", "UI/MatchOverlaysController.cs", "UI/DebugHud.cs",
                         "Diagnostics/AutoTestDriver.cs", "Diagnostics/PerfProbe.cs",
                     })
            {
                string src = TestSources.ReadClass($"Assets/_Game/Scripts/{file}");
                StringAssert.DoesNotContain("FindObjectsByType<ChickenController>", src, file);
                StringAssert.Contains("ChickenController.Local", src, file);
            }
            // FusionNetworkService no longer looks up the local chicken at all (it only did so to resolve the aim byte).
            StringAssert.DoesNotContain("FindObjectsByType<ChickenController>", File.ReadAllText("Assets/_Game/Scripts/App/FusionNetworkService.cs"));
        }
    }
}
