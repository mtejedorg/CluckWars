using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// <c>AbilityController</c> keeps four unrolled slot fields because Fusion weaves four
    /// <c>[Networked]</c> timer properties beside them, and a <c>NetworkArray</c> would change
    /// replication. The fan-out is allowed; a second copy of it is not. These guards fail if the
    /// slot fields grow another reader or another writer outside the two accessors.
    /// </summary>
    public sealed class AbilitySlotFanOutTests
    {
        private const string Path = "Assets/_Game/Scripts/Gameplay/AbilityController.cs";

        private static string Source() => File.ReadAllText(Path);

        /// <summary>Body of a method, from its signature line to the matching closing brace at the same indent.</summary>
        private static string Body(string source, string signature)
        {
            int start = source.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.Greater(start, -1, $"{signature} is gone from {Path}");
            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            Assert.Fail($"unbalanced braces after {signature}");
            return string.Empty;
        }

        [Test]
        public void TheSlotFields_AreWrittenOnlyInSetSlot()
        {
            string source = Source();
            string setSlot = Body(source, "private void SetSlot(int slot, AbilityBaseSO ability)");

            for (int i = 0; i < 4; i++)
            {
                var writes = Regex.Matches(source, $@"_slot{i}\s*=[^=]");
                Assert.AreEqual(1, writes.Count,
                    $"_slot{i} is assigned {writes.Count} times; every write goes through SetSlot(int, AbilityBaseSO).");
                StringAssert.Contains($"_slot{i} = ability;", setSlot, $"_slot{i}'s one write is not the one in SetSlot");
            }
        }

        [Test]
        public void TheSlotFields_AreReadOnlyInGetSlotAndTheirOwnProperty()
        {
            string source = Source();
            for (int i = 0; i < 4; i++)
            {
                // declaration, the Slot{i} property, GetSlot's arm, SetSlot's write.
                int mentions = Regex.Matches(source, $@"\b_slot{i}\b").Count;
                Assert.AreEqual(4, mentions,
                    $"_slot{i} is named {mentions} times; expected exactly the declaration, the Slot{i} property, " +
                    "GetSlot's arm and SetSlot's write. Read it through GetSlot(int) instead of adding a copy.");
            }
        }

        [Test]
        public void TheActiveTimers_AreReachedOnlyThroughTheirAccessors()
        {
            string source = Source();
            string get = Body(source, "private TickTimer GetActiveTimer(int slot) => slot switch");
            string set = Body(source, "private void SetActiveTimer(int slot, TickTimer timer)");

            for (int i = 0; i < 4; i++)
            {
                int mentions = Regex.Matches(source, $@"\bActiveTimer{i}\b").Count;
                Assert.AreEqual(3, mentions,
                    $"ActiveTimer{i} is named {mentions} times; expected the [Networked] declaration plus one arm each " +
                    "in GetActiveTimer and SetActiveTimer.");
                StringAssert.Contains($"ActiveTimer{i}", get);
                StringAssert.Contains($"ActiveTimer{i}", set);
            }
        }
    }
}
