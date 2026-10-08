using System;
using System.Linq;
using NUnit.Framework;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the serialisation-sensitive contracts: enum backing types, the meaning
    /// of the zero member, and the switch statements that must stay exhaustive.
    /// </summary>
    /// <remarks>
    /// These are cheap tests protecting expensive mistakes. Three project rules
    /// converge here:
    ///
    /// * <c>ChickenClass : byte</c> — the backing type is a Fusion serialisation
    ///   decision (CONVENTIONS.md "ChickenClass is byte-backed"). Widening it changes
    ///   the networked payload; renumbering it re-labels every already-authored asset
    ///   and every stamped <c>[Networked]</c> value.
    /// * <c>[Networked]</c> enums default to 0 (CONVENTIONS.md "[Networked] defaults"),
    ///   so the zero member of every networked enum has to be the correct pre-init
    ///   state — <c>MatchState.WaitingForPlayers</c>, <c>MatchEventKind.None</c>.
    /// * Every per-class or per-category switch must cover the whole enum. A new class
    ///   or category compiles fine and then falls through to a placeholder at runtime.
    /// </remarks>
    public sealed class ContractsAndEnumsTests
    {
        // ---- Backing types ------------------------------------------------------

        [Test]
        public void NetworkedEnums_AreByteBacked()
        {
            // Each of these is either [Networked] itself or authored on a networked
            // SO. Widening the backing type silently grows the replicated payload.
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(ChickenClass)),
                "ChickenClass must stay byte-backed — see CONVENTIONS.md.");
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(AbilityCategory)));
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(BotRole)));
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(MatchEventKind)));
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(ChickenSubclass)));
        }

        [Test]
        public void ChickenSubclassFlags_MirrorsChickenSubclass_MemberForMember()
        {
            var subclasses = Enum.GetValues(typeof(ChickenSubclass)).Cast<ChickenSubclass>()
                .Where(x => x != ChickenSubclass.None).ToList();
            var flagMembers = Enum.GetValues(typeof(ChickenSubclassFlags)).Cast<ChickenSubclassFlags>()
                .Where(x => x != ChickenSubclassFlags.None).ToList();

            Assert.AreEqual(typeof(ushort), Enum.GetUnderlyingType(typeof(ChickenSubclassFlags)));
            CollectionAssert.AreEqual(subclasses.Select(x => x.ToString()).ToList(),
                flagMembers.Select(x => x.ToString()).ToList(),
                "ChickenSubclassFlags must have the same member names in the same order as ChickenSubclass (minus None).");

            for (int i = 0; i < subclasses.Count; i++)
            {
                Assert.AreEqual((ushort)(1 << ((int)subclasses[i] - 1)), (ushort)flagMembers[i],
                    $"{subclasses[i]}: flag must equal 1 << (subclass - 1).");
                Assert.AreEqual(flagMembers[i], subclasses[i].ToFlag());
            }
        }

        [Test]
        public void ChickenSubclass_KeepsItsAuthoredNumbering()
        {
            // Passive .asset files serialise the integer, so these are append-only.
            Assert.AreEqual(0, (byte)ChickenSubclass.None);
            Assert.AreEqual(1, (byte)ChickenSubclass.Warrior_Relentless);
            Assert.AreEqual(2, (byte)ChickenSubclass.Warrior_Bully);
            Assert.AreEqual(3, (byte)ChickenSubclass.Speedy_Slippery);
            Assert.AreEqual(4, (byte)ChickenSubclass.Speedy_Featherfoot);
            Assert.AreEqual(5, (byte)ChickenSubclass.Fatty_Hoarder);
            Assert.AreEqual(6, (byte)ChickenSubclass.Fatty_Bulwark);
            Assert.AreEqual(7, (byte)ChickenSubclass.Assassin_Spoiler);
            Assert.AreEqual(8, (byte)ChickenSubclass.Assassin_Thief);
        }

        [Test]
        public void ChickenClass_KeepsItsAuthoredNumbering()
        {
            // ChickenClassRegistrySO entries, the _botLoadouts presets and every
            // stamped [Networked] Class value are all stored as these raw numbers.
            // Renumbering silently reassigns every one of them to a different class.
            Assert.AreEqual(0, (byte)ChickenClass.Warrior);
            Assert.AreEqual(1, (byte)ChickenClass.Speedy);
            Assert.AreEqual(2, (byte)ChickenClass.Fatty);
            Assert.AreEqual(3, (byte)ChickenClass.Assassin);
            Assert.AreEqual(4, Enum.GetValues(typeof(ChickenClass)).Length,
                "A class was added or removed. Append new classes at the END and re-check every " +
                "per-class switch, the registry asset and the bot loadout presets.");
        }

        [Test]
        public void ChickenClass_RoundTripsThroughItsByteRepresentation()
        {
            // This is exactly what Fusion does to the value on the wire.
            foreach (ChickenClass cls in Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.AreEqual(cls, (ChickenClass)(byte)cls);
            }
        }

        // ---- Zero members = the [Networked] default ------------------------------

        [Test]
        public void NetworkedEnums_ZeroMember_IsTheCorrectUninitialisedState()
        {
            Assert.AreEqual(0, (int)MatchState.WaitingForPlayers,
                "MatchState is [Networked] on GameManager and defaults to 0 before the first snapshot. " +
                "If 0 were Active or Ended, every peer would render a live or finished match for a frame.");
            Assert.AreEqual(0, (byte)MatchEventKind.None,
                "MatchEventKind is [Networked]; 0 must mean 'no comeback event running'.");
            Assert.AreEqual(0, (byte)BotRole.Auto,
                "An ability asset authored before BotRole existed deserialises to 0, which must be the " +
                "'derive from Category' behaviour rather than a concrete role.");
        }

        [Test]
        public void MatchState_HasExactlyTheFourLifecyclePhases()
        {
            // Starting (re-audit item 4) was APPENDED: the three original phases keep their wire values.
            Assert.AreEqual(1, (int)MatchState.Active);
            Assert.AreEqual(2, (int)MatchState.Ended);
            Assert.AreEqual(3, (int)MatchState.Starting);
            CollectionAssert.AreEquivalent(
                new[] { MatchState.WaitingForPlayers, MatchState.Active, MatchState.Ended, MatchState.Starting },
                Enum.GetValues(typeof(MatchState)).Cast<MatchState>().ToList(),
                "MatchState changed. Every HUD overlay gate switches on it — audit MatchOverlaysController " +
                "and MatchHudController before adding a phase.");
        }

        // ---- Exhaustive switches ------------------------------------------------

        [Test]
        public void ResolveBotRole_MapsEveryCategory_ToAConcreteRole()
        {
            // AbilityBaseSO.ResolveBotRole's inner switch has a `_ => Escape` catch-all,
            // so a new category silently becomes an escape ability for every bot. This
            // pins the intended mapping so that stays a deliberate choice.
            var expected = new (AbilityCategory cat, BotRole role)[]
            {
                (AbilityCategory.Steal,   BotRole.Steal),
                (AbilityCategory.Control, BotRole.Control),
                (AbilityCategory.Defense, BotRole.Defense),
                (AbilityCategory.Utility, BotRole.Escape),
            };

            Assert.AreEqual(Enum.GetValues(typeof(AbilityCategory)).Length, expected.Length,
                "An AbilityCategory was added. Decide its BotRole explicitly in ResolveBotRole — the " +
                "catch-all would otherwise make it an Escape ability for every bot.");

            foreach (var (cat, role) in expected)
            {
                var probe = UnityEngine.ScriptableObject.CreateInstance<SnatchAbilitySO>();
                try
                {
                    probe.Category = cat;
                    probe.BotRole = BotRole.Auto;
                    Assert.AreEqual(role, probe.ResolveBotRole(), $"Auto + {cat} should resolve to {role}.");
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
        }

        [Test]
        public void ResolveBotRole_AnExplicitRole_AlwaysWinsOverTheCategory()
        {
            // Sneaky Steal relies on this: Category is Utility (which Auto would turn
            // into Escape) but it is authored BotRole.Steal.
            var probe = UnityEngine.ScriptableObject.CreateInstance<SnatchAbilitySO>();
            try
            {
                probe.Category = AbilityCategory.Utility;
                probe.BotRole = BotRole.Steal;
                Assert.AreEqual(BotRole.Steal, probe.ResolveBotRole());
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        // ---- Cross-class determinism contract ----------------------------------

        [Test]
        public void SessionNameSeed_IsIdentical_InMapGeneratorAndCornerAssignment()
        {
            // Both hash the session name to seed a System.Random, and both rely on every peer
            // computing the same value: MapGenerator lays out the interior walls (LOCAL geometry,
            // so a mismatch means peers physically disagree about where the walls are) and
            // CornerAssignment (MatchBootstrapper + the menu Coop) picks the corner permutation.
            // The two copies are duplicated on purpose (MapGenerator: "kept in sync (same
            // polynomial)"), so this test is the only thing actually keeping them in sync.
            var mapSeed = typeof(MapGenerator).GetMethod("SessionNameSeed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(mapSeed, "MapGenerator.SessionNameSeed was renamed or removed.");

            foreach (var name in new[] { "cluck-lan", "ABC123", "", "a", "Zz9-Zz9", "ÑoÑo" })
            {
                Assert.AreEqual(
                    (int)mapSeed.Invoke(null, new object[] { name }),
                    CornerAssignment.SessionNameSeed(name),
                    $"The two SessionNameSeed copies disagree for session name '{name}'. Peers would " +
                    "build different interior-wall layouts and assign different corners from the same session.");
            }
        }

        [Test]
        public void SessionNameSeed_IsStable_AcrossCalls_AndDiffersPerSessionName()
        {
            var seed = typeof(MapGenerator).GetMethod("SessionNameSeed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(seed, "MapGenerator.SessionNameSeed was renamed or removed.");

            int Seed(string s) => (int)seed.Invoke(null, new object[] { s });

            Assert.AreEqual(Seed("cluck-lan"), Seed("cluck-lan"),
                "The hash must be deterministic — string.GetHashCode is not, which is why this exists.");
            Assert.AreNotEqual(Seed("ABC123"), Seed("ABC124"),
                "Adjacent join codes must produce different layouts, or every room looks the same.");
        }
    }
}
