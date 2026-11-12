using System.Collections.Generic;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CluckWars.Tests
{
    /// <summary>
    /// S-4 (review of audit batch 2): since PRES-4 gameplay raises events and presentation listens, so the cargo
    /// pops, the death shake, the BACK IN cue, the removed pose and the class-model beat exist only if the listener
    /// components are on the chicken. The prefabs are checked here; at runtime a raise nobody hears warns once.
    /// </summary>
    public sealed class ChickenListenerWiringTests
    {
        private sealed class WarnLog : ILogService
        {
            public readonly List<string> Warnings = new List<string>();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) => Warnings.Add($"[{source}] {message}");
            public void Error(string source, string message, System.Exception exception = null) { }
        }

        [SetUp]
        public void SetUp() => LocalRaise.ForgetSilentReports();

        [TearDown]
        public void TearDown() => LocalRaise.ForgetSilentReports();

        // ---- The prefabs carry the listeners ------------------------------------------------------------------------

        private static T Required<T>(GameObject prefab) where T : Behaviour
        {
            var component = prefab.GetComponent<T>();
            Assert.IsNotNull(component, $"{prefab.name}.prefab has no {typeof(T).Name} on its root");
            Assert.IsTrue(component.enabled, $"{typeof(T).Name} is disabled on {prefab.name}.prefab: it subscribes in OnEnable");
            return component;
        }

        [Test]
        public void TheChickenPrefab_CarriesEveryListenerTheGameplayEventsRelyOn()
        {
            var chicken = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);

            // The raisers.
            Assert.IsNotNull(chicken.GetComponent<ChickenController>());
            Assert.IsNotNull(chicken.GetComponent<ChickenCargo>(), "raises CargoChanged");
            Assert.IsNotNull(chicken.GetComponent<ChickenCombat>(), "raises OnDeath / OnRespawn");
            Assert.IsNotNull(chicken.GetComponent<AbilityController>(), "raises the ability cues");

            // The listeners: HitFeedback draws the cargo pops, the death shake, the BACK IN cue, refreshes its
            // renderers on the class model and plays the own sounds; ChickenAnimator plays the removed pose.
            Required<HitFeedback>(chicken);
            Required<ChickenAnimator>(chicken);
        }

        [Test]
        public void TheDecoyPrefab_KeepsTheListeners_ItOnlyDropsTheCargo()
        {
            var decoy = TestAssets.Load<GameObject>(TestAssets.DoppelgangerPrefabPath);
            Assert.IsNull(decoy.GetComponent<ChickenCargo>(), "a decoy carries nothing (the variant strips ChickenCargo)");
            Required<HitFeedback>(decoy);
            Required<ChickenAnimator>(decoy);
        }

        [Test]
        public void HitFeedback_SubscribesToEveryLocalMomentGameplayRaises()
        {
            string code = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Visuals/HitFeedback.cs");
            foreach (string moment in new[] { "CargoChanged", "AttackerNamed", "ZoneTriggered", "ClassModelAttached", "AbilityCueRaised" })
                StringAssert.IsMatch($@"_events\.{moment}\s*\+=", code, $"HitFeedback no longer listens for {moment}");
            StringAssert.IsMatch(@"_combat\.OnDeath\s*\+=", code);
            StringAssert.IsMatch(@"_combat\.OnRespawn\s*\+=", code);
        }

        // ---- A raise nobody hears ------------------------------------------------------------------------------------

        [Test]
        public void EveryLocalRaise_SaysWhenNobodyListens()
        {
            var events = new ChickenLocalEvents();
            Assert.IsFalse(events.RaiseCargoChanged(3f, -1));
            Assert.IsFalse(events.RaiseClassModelAttached());
            Assert.IsFalse(events.RaiseZoneTriggered());
            Assert.IsFalse(events.RaiseAttackerNamed(Vector3.zero));
            Assert.IsFalse(events.RaiseAbilityCue(AbilityCue.Expired));

            events.CargoChanged += _ => { };
            events.ClassModelAttached += () => { };
            Assert.IsTrue(events.RaiseCargoChanged(3f, -1));
            Assert.IsTrue(events.RaiseClassModelAttached());
        }

        [Test]
        public void ASilentRaise_WarnsOncePerEventAndChickenKind_NamingWhatIsLost()
        {
            var log = new WarnLog();
            var owner = new GameObject("Chicken(Clone)");
            try
            {
                for (int i = 0; i < 3; i++)
                    LocalRaise.WarnNobodyListens(log, "CargoChanged", owner, isDecoy: false, "the cargo pop is not drawn");
                Assert.AreEqual(1, log.Warnings.Count, "once per chicken kind, not once per raise");
                StringAssert.Contains("Nothing listens for CargoChanged on 'Chicken(Clone)' (chicken): the cargo pop is not drawn", log.Warnings[0]);

                LocalRaise.WarnNobodyListens(log, "CargoChanged", owner, isDecoy: true, "the cargo pop is not drawn");
                LocalRaise.WarnNobodyListens(log, "OnDeath", owner, isDecoy: false, "the death shake is not played");
                Assert.AreEqual(3, log.Warnings.Count, "another kind and another event are reported on their own");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void WithNoLoggerYet_TheWarningStillGoesOut()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Nothing listens for ClassModelAttached"));
            LocalRaise.WarnNobodyListens(null, "ClassModelAttached", null, isDecoy: false, "the hit flash misses the model");
        }

        [Test]
        public void TheRaisers_ReportASilentRaise_SourceGuard()
        {
            StringAssert.IsMatch(@"!_controller\.LocalEvents\.RaiseCargoChanged\(delta, stealCreditFrame\)\)\s*LocalRaise\.WarnNobodyListens\(",
                RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs"));
            StringAssert.IsMatch(@"!LocalEvents\.RaiseClassModelAttached\(\)\)\s*LocalRaise\.WarnNobodyListens\(",
                RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/ChickenController.cs"));
            string combat = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/ChickenCombat.cs");
            StringAssert.IsMatch(@"if \(OnDeath == null\) WarnNobodyListens\(nameof\(OnDeath\)", combat);
            StringAssert.IsMatch(@"if \(OnRespawn == null\) WarnNobodyListens\(nameof\(OnRespawn\)", combat);
        }
    }
}
