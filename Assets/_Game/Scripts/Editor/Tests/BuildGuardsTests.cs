using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using CluckWars.EditorTools;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-200 (headless builds carried no UGS cloud project id) and CW-201 (headless APK installed as
    /// com.DefaultCompany.CluckWars). The decision logic is pure; one test also pins the committed ProjectSettings.
    /// </summary>
    public sealed class BuildGuardsTests
    {
        private const string RealId = "14249009-abbe-423c-8c0c-8391b8974857";

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not-a-guid")]
        [TestCase("14249009abbe423c8c0c8391b8974857")]                       // no dashes
        [TestCase(" 14249009-abbe-423c-8c0c-8391b8974857")]                  // surrounding whitespace
        [TestCase("{14249009-abbe-423c-8c0c-8391b8974857}")]                 // braces
        [TestCase("00000000-0000-0000-0000-000000000000")]
        public void CloudProjectId_RejectsEmptyOrMalformed(string id)
        {
            Assert.IsNotNull(BuildGuards.ValidateCloudProjectId(id));
        }

        [Test]
        public void CloudProjectId_AcceptsTheRealProjectId()
        {
            Assert.IsNull(BuildGuards.ValidateCloudProjectId(RealId));
            Assert.AreEqual(RealId, BuildGuards.UgsProjectId, "the baked-in fallback must be the real project id");
        }

        [Test]
        public void Resolve_KeepsValidProjectSettingsId()
        {
            const string other = "11111111-2222-3333-4444-555555555555";
            Assert.AreEqual(other, BuildGuards.ResolveCloudProjectId(other, RealId, out var source));
            Assert.AreEqual("ProjectSettings", source);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("garbage")]
        public void Resolve_FallsBackToConstantWhenNoOverride(string current)
        {
            Assert.AreEqual(RealId, BuildGuards.ResolveCloudProjectId(current, null, out _));
            Assert.AreEqual(RealId, BuildGuards.ResolveCloudProjectId(current, "  ", out _));
        }

        [Test]
        public void Resolve_EnvOverrideWinsOverConstant_AndIsTrimmed()
        {
            const string other = "11111111-2222-3333-4444-555555555555";
            Assert.AreEqual(other, BuildGuards.ResolveCloudProjectId("", $" {other} ", out var source));
            Assert.AreEqual(BuildGuards.CloudProjectIdEnvVar, source);
        }

        [Test]
        public void Resolve_MalformedEnvOverrideIsNotSilentlyReplaced()
        {
            var resolved = BuildGuards.ResolveCloudProjectId("", "oops", out _);
            Assert.AreEqual("oops", resolved);
            Assert.IsNotNull(BuildGuards.ValidateCloudProjectId(resolved), "the guard must still fail the build");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("com.DefaultCompany.CluckWars")]
        [TestCase("com.defaultcompany.cluckwars")]
        [TestCase("com.UnityTechnologies.com.unity.template.urpblank")]
        [TestCase("com.Unity-Technologies.com.unity.template.urpblank")]
        [TestCase("cluckwars")]                                              // not reverse-DNS
        [TestCase("com.cluck wars.game")]
        [TestCase("com..game")]
        public void ApplicationIdentifier_RejectsEmptyDefaultOrMalformed(string id)
        {
            Assert.IsNotNull(BuildGuards.ValidateApplicationIdentifier(id));
        }

        [Test]
        public void ApplicationIdentifier_AcceptsThePinnedId()
        {
            Assert.AreEqual("com.cluckwars.game", BuildGuards.AppIdentifier);
            Assert.IsNull(BuildGuards.ValidateApplicationIdentifier(BuildGuards.AppIdentifier));
        }

        [Test]
        public void ProjectSettings_AndroidAndIosIdentifiersArePinned()
        {
            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                var id = PlayerSettings.GetApplicationIdentifier(target);
                Assert.IsNull(BuildGuards.ValidateApplicationIdentifier(id), $"{target.TargetName}: {id}");
                Assert.AreEqual(BuildGuards.AppIdentifier, id, $"{target.TargetName} identifier drifted from the pinned id");
            }
        }
    }
}
