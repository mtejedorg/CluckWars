using System;
using System.Collections.Generic;
using System.IO;
using CluckWars.Logging;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>CW-055 (telegraph line material: silent null + per-instance leak) and CW-056 (RivalIndicator
    /// self-injection must not gate on <c>ProjectContext.HasInstance</c>, CONVENTIONS.md).</summary>
    public class TelegraphMaterialAndInjectionTests
    {
        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Errors = new List<string>();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors.Add(source + ": " + message);
        }

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        [SetUp, TearDown]
        public void Reset() => TelegraphShapes.ResetLineMaterialStateForTests();

        [Test]
        public void MissingShader_ReturnsNull_AndLogsOneErrorForTheSession()
        {
            var log = new RecordingLog();
            Assert.That(TelegraphShapes.CreateLineMaterial(_ => null, log), Is.Null);
            Assert.That(TelegraphShapes.CreateLineMaterial(_ => null, log), Is.Null);
            Assert.That(log.Errors.Count, Is.EqualTo(1), "one Error, not one per line");
            StringAssert.Contains("No line shader found", log.Errors[0]);
        }

        [Test]
        public void FoundShader_BuildsAMaterial_AndLogsNothing()
        {
            var log = new RecordingLog();
            var sh = Shader.Find("Sprites/Default");
            Assert.That(sh, Is.Not.Null);
            var mat = TelegraphShapes.CreateLineMaterial(_ => sh, log);
            try
            {
                Assert.That(mat, Is.Not.Null);
                Assert.That(mat.shader, Is.EqualTo(sh));
                Assert.That(log.Errors, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(mat); }
        }

        [Test]
        public void SolidMaterial_IsShared_AcrossCalls()
        {
            var a = TelegraphShapes.SharedSolidLineMaterial(null);
            var b = TelegraphShapes.SharedSolidLineMaterial(null);
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.SameAs(a), "one material for every telegraph chicken, not one per Awake");
        }

        [Test]
        public void AbilityTelegraph_SharesTheSolidMaterial_AndDestroysItsDashedOne()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs");
            StringAssert.Contains("_solidMat = TelegraphShapes.SharedSolidLineMaterial(_log);", src);
            StringAssert.Contains("if (_dashedMat != null) Destroy(_dashedMat);", src);
        }

        [Test]
        public void RivalIndicator_SelfInjects_WithoutGatingOnHasInstance()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("if (_log == null) ProjectContext.Instance.Container.Inject(this);", src);
            StringAssert.DoesNotContain("ProjectContext.HasInstance", src.Replace("Never gate on ProjectContext.HasInstance", ""));
        }
    }
}
