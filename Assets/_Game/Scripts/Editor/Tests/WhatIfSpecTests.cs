using CluckWars.Balance;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The optional <c>"roster"</c> key of the what-if file (design 2026-11-25).</summary>
    public sealed class WhatIfSpecTests
    {
        [Test]
        public void Roster_ParsesAndRejectsAnythingButAnObjectOfNames()
        {
            Assert.IsTrue(WhatIfSpec.TryParse("{\"roster\":{\"Enforcer\":\"Relentless\"},\"scenarios\":[]}", out var spec, out string error), error);
            Assert.AreEqual(1, spec.Roster.Count);
            Assert.AreEqual("Enforcer", spec.Roster[0].Key);
            Assert.AreEqual("Relentless", spec.Roster[0].Value);

            Assert.IsTrue(WhatIfSpec.TryParse("{\"scenarios\":[]}", out spec, out error), error);
            Assert.AreEqual(0, spec.Roster.Count, "no key, no swap");

            foreach (string bad in new[]
            {
                "{\"roster\":[\"Relentless\"]}", "{\"roster\":\"Relentless\"}", "{\"roster\":{\"Relentless\":3}}",
                "{\"roster\":{\"Relentless\":\"\"}}", "{\"roster\":{\"Relentless\":null}}", "{\"roster\":{\"Relentless\":\"A\",\"Relentless\":\"B\"}}",
            })
            {
                Assert.IsFalse(WhatIfSpec.TryParse(bad, out spec, out error), bad);
                Assert.IsNotEmpty(error, bad);
            }
        }
    }
}
