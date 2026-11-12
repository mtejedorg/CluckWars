using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// PRES-4 (architecture audit 2026-11-12, rule D2a point 4): gameplay changes replicated state and raises local
    /// events; it does not call presentation. The local raises are exercised for real; the boundary itself is a
    /// source guard over the Gameplay and Abilities folders.
    /// </summary>
    public sealed class GameplayPresentationBoundaryTests
    {
        private const string ScriptsRoot = "Assets/_Game/Scripts";

        // ---- The local moments (ChickenLocalEvents.Raise...) ---------------------------------------------------------

        [Test]
        public void ACargoChange_ReachesItsListeners_WithTheDeltaAndTheStealCreditFrame()
        {
            var events = new ChickenLocalEvents();
            var seen = new List<CargoChangeEvent>();
            events.CargoChanged += seen.Add;

            events.RaiseCargoChanged(-4f, -1);
            events.RaiseCargoChanged(3f, 120);

            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual(-4f, seen[0].Delta);
            Assert.AreEqual(-1, seen[0].StealCreditFrame);
            Assert.AreEqual(3f, seen[1].Delta);
            Assert.AreEqual(120, seen[1].StealCreditFrame);
        }

        [Test]
        public void ARaiseNobodyListensTo_SaysSo_SoTheCallerCanReportTheMissingListener()
        {
            var events = new ChickenLocalEvents();
            Assert.IsFalse(events.RaiseZoneTriggered(), "a zone confirmation with no listener is a prefab fault the zone reports");
            Assert.IsFalse(events.RaiseAttackerNamed(Vector3.one));
            Assert.DoesNotThrow(() => events.RaiseCargoChanged(1f, -1));
            Assert.DoesNotThrow(() => events.RaiseClassModelAttached());

            int zones = 0, models = 0;
            Vector3 named = default;
            events.ZoneTriggered += () => zones++;
            events.AttackerNamed += p => named = p;
            events.ClassModelAttached += () => models++;

            Assert.IsTrue(events.RaiseZoneTriggered());
            Assert.IsTrue(events.RaiseAttackerNamed(new Vector3(1f, 2f, 3f)));
            events.RaiseClassModelAttached();
            Assert.AreEqual((1, 1), (zones, models));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), named);
        }

        [Test]
        public void Reset_KeepsTheSubscriptions_TheyAreTheListenersToPair()
        {
            var events = new ChickenLocalEvents();
            int zones = 0;
            events.ZoneTriggered += () => zones++;
            events.Reset();
            Assert.IsTrue(events.RaiseZoneTriggered());
            Assert.AreEqual(1, zones);
        }

        // ---- The boundary (source guards) --------------------------------------------------------------------------

        private static IEnumerable<string> GameplayFiles() =>
            new[] { "Gameplay", "Abilities" }.SelectMany(folder =>
                Directory.GetFiles($"{ScriptsRoot}/{folder}", "*.cs", SearchOption.AllDirectories)).Select(p => p.Replace('\\', '/'));

        private static string Code(string path)
        {
            string src = File.ReadAllText(path);
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            src = Regex.Replace(src, @"//[^\n]*", string.Empty);
            return Regex.Replace(src, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");   // string literals are not code
        }

        [Test]
        public void Gameplay_CallsNoOneShotPresentation()
        {
            // Camera shake, floating text, hit feedback, the local cues and animator triggers are the listeners' job.
            var presentation = new Regex(
                @"\b(MatchCamera|HitFeedback|LocalRespawnCue|HitStopDriver|FoodChunkFlight|ChickenVFX|ControlStateVFX)\b" +
                @"|\bFloatingCombatText\.Spawn\w*\(|\.ApplyShake\(|\.TriggerPeck\(|\.TriggerAbilityCast\(|\.TriggerHit\(|\.SetStunned\(");
            foreach (string path in GameplayFiles())
            {
                var m = presentation.Match(Code(path));
                Assert.IsFalse(m.Success, $"{path} calls presentation ({m.Value}). Change replicated state or raise a local event " +
                    "(ChickenController.LocalEvents, ChickenCombat.OnDeath / OnRespawn) and let presentation observe it.");
            }
        }

        [Test]
        public void TheGameplayFiles_ThatStillImportVisuals_AreTheKnownTwo()
        {
            // What is left is not a one-shot effect: the palette and the chicken's composition root.
            // Batch 4 of the audit owns these. A new import here is a new dependency from gameplay on presentation.
            var expected = new[]
            {
                $"{ScriptsRoot}/Abilities/AbilityBaseSO.cs",      // AccentKey / FeedbackPalette (the ability's colour)
                $"{ScriptsRoot}/Gameplay/ChickenController.cs",   // composition: model attach, head lift, pop anchor, indicator accessors
            };
            var actual = GameplayFiles().Where(p => Code(p).Contains("CluckWars.Visuals")).OrderBy(p => p).ToArray();
            CollectionAssert.AreEqual(expected.OrderBy(p => p).ToArray(), actual);
        }

        [Test]
        public void TheTwoSimulationConstants_LiveInGameplay_AndFeedbackTuningIsTheSameValue()
        {
            // A1 (batch 4): the tick reads these, so gameplay owns them; presentation keeps its names as aliases.
            Assert.AreEqual(0.12f, AbilityHoldStateMachine.TapHoldThresholdSeconds);
            Assert.AreEqual(1.5f, ChickenCargo.CargoChangeEventThreshold);
            Assert.AreEqual(AbilityHoldStateMachine.TapHoldThresholdSeconds, CluckWars.Visuals.FeedbackTuning.TapHoldThresholdSeconds);
            Assert.AreEqual(ChickenCargo.CargoChangeEventThreshold, CluckWars.Visuals.FeedbackTuning.FloatingTextCargoDeltaThreshold);

            string tuning = Code($"{ScriptsRoot}/Visuals/FeedbackTuning.cs");
            StringAssert.Contains("TapHoldThresholdSeconds = AbilityHoldStateMachine.TapHoldThresholdSeconds;", tuning, "an alias, not a second literal");
            StringAssert.Contains("FloatingTextCargoDeltaThreshold = ChickenCargo.CargoChangeEventThreshold;", tuning, "an alias, not a second literal");
            StringAssert.Contains("AbilityHoldStateMachine.TapHoldThresholdSeconds,", Code($"{ScriptsRoot}/Gameplay/AbilityController.cs"));
            StringAssert.Contains("< CargoChangeEventThreshold", Code($"{ScriptsRoot}/Gameplay/ChickenCargo.cs"));
        }

        [Test]
        public void TheRemoval_IsAnnouncedByEvents_AndTheZone_ByLocalRaises()
        {
            string combat = Code($"{ScriptsRoot}/Gameplay/ChickenCombat.cs");
            StringAssert.Contains("OnDeath?.Invoke();", combat);
            StringAssert.Contains("OnRespawn?.Invoke();", combat);

            string zone = Code($"{ScriptsRoot}/Gameplay/AbilityZone.cs");
            Assert.AreEqual(2, Regex.Matches(zone, @"chicken\.LocalEvents\.RaiseAttackerNamed\(center\)").Count);
            StringAssert.Contains("owner.LocalEvents.RaiseZoneTriggered()", zone);

            string feedback = Code($"{ScriptsRoot}/Visuals/HitFeedback.cs");
            foreach (string pair in new[] { "CargoChanged", "AttackerNamed", "ZoneTriggered", "ClassModelAttached", "Deposited" })
            {
                StringAssert.IsMatch($@"_events\.{pair}\s*\+=", feedback, $"HitFeedback no longer listens to {pair}");
                StringAssert.IsMatch($@"_events\.{pair}\s*-=", feedback, $"HitFeedback never unsubscribes from {pair}");
            }
            StringAssert.Contains("_combat.OnRespawn += HandleRespawn;", feedback);
            StringAssert.Contains("_combat.OnRespawn -= HandleRespawn;", feedback);
            string removal = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Visuals/HitFeedback.cs", "private void HandleRemoval()");
            StringAssert.Contains("FeedbackTuning.DeathShakeMagnitude", removal, "the death shake has one call site, here");
            StringAssert.Contains("!_controller.IsDecoy", removal, "a decoy shares its caster's input authority");
        }
    }
}
