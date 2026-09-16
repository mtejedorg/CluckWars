using CluckWars.Progression;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The progression round types are journaled with <c>JsonUtility</c>, which drops anything
    /// it cannot see (properties, dictionaries, non-serializable types) without complaint. These
    /// round-trips are the guard: a field that stops serializing turns red here instead of
    /// silently vanishing from a player's saved history.
    /// </summary>
    public sealed class RoundTypesTests
    {
        [Test]
        public void RoundRuleset_RoundTripsThroughJsonUtility()
        {
            var original = new RoundRuleset { ResourceTargetToWin = 40.5f, RoundDurationSeconds = 44.75f, MaxActors = 4 };

            string json = JsonUtility.ToJson(original);
            var copy = JsonUtility.FromJson<RoundRuleset>(json);

            string why = $"RoundRuleset lost a field through JsonUtility (json: {json}). Every field must be a " +
                         "public field of a JsonUtility-serializable type — no properties, no dictionaries.";
            Assert.AreEqual(original.ResourceTargetToWin, copy.ResourceTargetToWin, why);
            Assert.AreEqual(original.RoundDurationSeconds, copy.RoundDurationSeconds, why);
            Assert.AreEqual(original.MaxActors, copy.MaxActors, why);
        }

        [Test]
        public void RoundStandings_RoundTripsThroughJsonUtility_WithATieAtFirst()
        {
            var original = new RoundStandings
            {
                Entries = new[]
                {
                    new RoundStandingEntry { ActorId = 101, RoleKey = "class.warrior",  Placement = 1, ResourceTotal = 12.5f },
                    new RoundStandingEntry { ActorId = 202, RoleKey = "class.assassin", Placement = 1, ResourceTotal = 12.5f },
                },
            };

            string json = JsonUtility.ToJson(original);
            var copy = JsonUtility.FromJson<RoundStandings>(json);

            string why = $"RoundStandings did not survive JsonUtility (json: {json}). Entries must stay an array of a " +
                         "[Serializable] struct with public fields — JsonUtility silently drops anything else.";
            Assert.IsNotNull(copy?.Entries, why);
            Assert.AreEqual(2, copy.Entries.Length, why);
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(original.Entries[i].ActorId,       copy.Entries[i].ActorId,       why);
                Assert.AreEqual(original.Entries[i].RoleKey,       copy.Entries[i].RoleKey,       why);
                Assert.AreEqual(original.Entries[i].Placement,     copy.Entries[i].Placement,     why);
                Assert.AreEqual(original.Entries[i].ResourceTotal, copy.Entries[i].ResourceTotal, why);
            }
        }
    }
}
