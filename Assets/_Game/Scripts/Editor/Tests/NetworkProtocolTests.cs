using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Networking;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// BL-218: builds with different networked layouts must never share a room. <see cref="NetworkProtocol.Version"/> is bumped by hand; this guard
    /// fails, with the instructions, when the set of <c>[Networked]</c> properties or <c>[Rpc]</c> signatures in the runtime scripts changes and the version
    /// (and so the fingerprint stored here) does not. The fingerprint is read from source text, so it needs no Fusion runner.
    /// </summary>
    public sealed class NetworkProtocolTests
    {
        // The fingerprint of the networked members as of NetworkProtocol.Version below. To update: change the layout, bump NetworkProtocol.Version,
        // run this test, and paste the value it prints into ExpectedFingerprint together with the new version in ExpectedVersion.
        private const int ExpectedVersion = 3;
        private const string ExpectedFingerprint = "994246d1";

        private static readonly Regex TypeDecl = new Regex(@"\b(?:class|struct)\s+(\w+)", RegexOptions.Compiled);
        private static readonly Regex Networked = new Regex(@"\[Networked(?:\([^\]]*\))?\]\s*(?:\[[^\]]*\]\s*)*([^{;=]+?)\s*(?:\{|;|=)", RegexOptions.Compiled);
        private static readonly Regex Rpc = new Regex(@"\[Rpc\(([^\]]*)\)\]\s*(?:\[[^\]]*\]\s*)*([^{;]+?\))\s*(?:\{|;|=>)", RegexOptions.Compiled);

        /// <summary>Every networked member, one line each: <c>Type::[Networked] decl</c> or <c>Type::[Rpc(args)] signature</c>, files in path order.</summary>
        internal static List<string> Members()
        {
            var found = new List<(string Type, int Order, string Text)>();
            int order = 0;
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles().OrderBy(p => p, System.StringComparer.Ordinal))
            {
                string code = RuntimeSourceGuardTests.Code(path);
                var types = TypeDecl.Matches(code).Cast<Match>().ToList();
                string TypeAt(int index)
                {
                    string name = "?";
                    foreach (var t in types) { if (t.Index < index) name = t.Groups[1].Value; else break; }
                    return name;
                }
                foreach (Match m in Networked.Matches(code))
                    found.Add((TypeAt(m.Index), order++, "[Networked] " + Squash(m.Groups[1].Value)));
                foreach (Match m in Rpc.Matches(code))
                    found.Add((TypeAt(m.Index), order++, "[Rpc(" + Squash(m.Groups[1].Value) + ")] " + Squash(m.Groups[2].Value)));
            }
            // Within a type the declaration order is the layout; across types only the set matters, so sort by type name and keep each type's own order.
            return found.OrderBy(f => f.Type, System.StringComparer.Ordinal).ThenBy(f => f.Order).Select(f => f.Type + "::" + f.Text).ToList();
        }

        private static string Squash(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

        internal static string Fingerprint(IEnumerable<string> members)
        {
            uint hash = 2166136261;   // FNV-1a, 32 bit: stable across runs and platforms
            foreach (char c in string.Join("\n", members))
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }

        [Test]
        public void TheNetworkedLayout_ChangesOnlyTogetherWithTheProtocolVersion()
        {
            var members = Members();
            Assert.Greater(members.Count, 20, "the scan found the game's networked members (the pattern broke if this fails)");
            string actual = Fingerprint(members);
            bool versionMoved = NetworkProtocol.Version != ExpectedVersion;
            bool layoutMoved = actual != ExpectedFingerprint;
            if (layoutMoved || versionMoved)
            {
                var sb = new StringBuilder();
                sb.AppendLine(layoutMoved && !versionMoved
                    ? $"The set of [Networked] properties / [Rpc] signatures changed (fingerprint {actual}, stored {ExpectedFingerprint}) but NetworkProtocol.Version is still {NetworkProtocol.Version}."
                    : $"NetworkProtocol.Version is {NetworkProtocol.Version} and the test stores version {ExpectedVersion}, fingerprint {ExpectedFingerprint}; the scan gives {actual}.");
                sb.AppendLine("Builds with different networked layouts must not share a room: bump NetworkProtocol.Version (Assets/_Game/Scripts/Networking/NetworkProtocol.cs),");
                sb.AppendLine($"then set ExpectedVersion = {NetworkProtocol.Version + (layoutMoved && !versionMoved ? 1 : 0)} and ExpectedFingerprint = \"{actual}\" in NetworkProtocolTests.cs.");
                sb.AppendLine("Members now:");
                foreach (var m in members) sb.AppendLine("  " + m);
                Assert.Fail(sb.ToString());
            }
        }

        [Test]
        public void TheVersionIsNotTheBuildNumber_AndReachesPhotonAsTheAppVersion()
        {
            string service = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/App/FusionNetworkService.cs");
            StringAssert.Contains("AppVersion = NetworkProtocol.AppVersion", service);
            StringAssert.Contains("CustomPhotonAppSettings", service, "the app version is part of what StartGame matches on");
            Assert.AreEqual("p" + NetworkProtocol.Version, NetworkProtocol.AppVersion);
            StringAssert.DoesNotContain("BuildInfo", RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Networking/NetworkProtocol.cs"), "two dev builds of one protocol must still meet");
        }

        [Test]
        public void TheProtocolIsShown_InTheSessionLineAndTheDebugHud()
        {
            StringAssert.Contains("NetworkProtocol.Version", RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Diagnostics/SessionStamp.cs"));
            StringAssert.Contains("ProtocolVersion", RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/UI/DebugHud.cs"));
        }
    }
}
