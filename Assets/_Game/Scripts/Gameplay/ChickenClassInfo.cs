using System;
using CluckWars.Abilities;
using CluckWars.Localization;

namespace CluckWars.Gameplay
{
    /// <summary>What defines one chicken class besides its stats asset: see <see cref="ChickenClasses"/>.</summary>
    public readonly struct ChickenClassInfo
    {
        public readonly ChickenClass Class;
        /// <summary>The class's bit in an ability's <c>AllowedClasses</c> mask: always <c>1 &lt;&lt; (int)Class</c>.</summary>
        public readonly ChickenClassFlags Flag;
        /// <summary>May this class forage from piles (carry a Peck)? The Predation axiom: the Assassin may not.</summary>
        public readonly bool MayForage;
        /// <summary>Wording keys (UiKeys): the short name, the role line, the two callouts of the class picker.</summary>
        public readonly string ShortNameKey, RoleKey, StrongKey, WeakKey;
        /// <summary>How a bot of this class plays.</summary>
        public readonly BotProfile Bot;

        public ChickenClassInfo(ChickenClass cls, bool mayForage, string shortNameKey, string roleKey,
            string strongKey, string weakKey, BotProfile bot)
        {
            Class = cls;
            Flag = ChickenClasses.FlagOf(cls);
            MayForage = mayForage;
            ShortNameKey = shortNameKey;
            RoleKey = roleKey;
            StrongKey = strongKey;
            WeakKey = weakKey;
            Bot = bot;
        }
    }

    /// <summary>
    /// The one code-side definition of each chicken class (A17): its ability-mask flag, whether it forages, its wording
    /// keys and its bot profile. The numbers a designer tunes (speed, capacity, tint, model) stay on the class's
    /// <c>ChickenStatsSO</c>, reached through <c>ChickenClassRegistrySO</c>.
    /// </summary>
    /// <remarks>
    /// To add a class: a value in <see cref="ChickenClass"/> (append only, it is a networked byte), the matching bit in
    /// <see cref="ChickenClassFlags"/>, an entry here, a stats asset in the registry, and the presentation data
    /// <c>ChickenClassTableTests</c> lists (it fails, naming each place, until every table answers for the new value).
    /// </remarks>
    public static class ChickenClasses
    {
        // Warrior — Brawler. Banks late because its income is robbery, not foraging: a
        // big beakful is the bait that makes rivals come to it. Hunts anyone, loaded or
        // not, out to a long radius, and guards the pile it is standing on.
        //
        // Picks piles on proximity ALONE (roundTripBias 0) — it does not care about the
        // walk home, because the walk home is exactly when it wants to be jumped.
        //
        // This shipped at 0.25 and that was a real inconsistency, not a rounding
        // preference: any nonzero weight lets a large enough base-distance spread
        // overwhelm the proximity term, and on a 51.3 m arena those spreads are ordinary.
        // At 0.25 a pile 5 m away but 26 m from base lost to one 8 m away and 4 m from
        // base — the Brawler walking to the quiet corner, which is the opposite of the
        // sentence above it. Pinned twice now: once by
        // RoundTripBias_IsWhatSeparatesARouteFromAStep against the Runner, and once by
        // TheBrawler_TakesTheNearerPile_AtEveryBaseDistance, which sweeps the return leg
        // and so states the property rather than sampling it. If this class is ever meant
        // to have partial round-trip awareness, argue with the sweep first.
        //
        // dangerRadius 4.0 -> 5.5 (2026-10-22, decision A10): 4.0 sat inside the longest
        // steal reaches (Sneaky Steal 5.4 m, Dive Bomb 3.25 + 2.05 = 5.3 m), so a loaded
        // Brawler let a thief stand in range without reacting. 5.5 is just past 5.4.
        //
        // returnAmount 12 (2026-11-25, Maestro): banks at 12 food whatever the beak (0.85 of 20 is 17, and every extra
        // food carried was exposure at flat income); on the way home it steals from a loaded rival in reach (BotTactics.ShouldStealInPassing).
        private static readonly ChickenClassInfo Warrior = new ChickenClassInfo(
            ChickenClass.Warrior, mayForage: true,
            shortNameKey: UiKeys.ClassWarriorShort, roleKey: UiKeys.RoleWarrior,
            strongKey: UiKeys.CalloutWarriorStrong, weakKey: UiKeys.CalloutWarriorWeak,
            bot: new BotProfile(
                    strategy: BotStrategy.Brawler,
                    returnThreshold: 0.85f, protectCargoThreshold: 0.60f, dangerRadius: 5.5f,
                    huntRadius: 11.0f, huntCargoThreshold: 0.10f, engageRadius: 9.0f,
                    guardRadius: 6.0f, roundTripBias: 0.0f, leaderFocus: 0.35f,
                    thinkInterval: 0.28f, returnAmount: 12f));

        // Speedy — Runner. The only class that plans a ROUTE: pile choice is a full
        // round-trip cost, not proximity. Banks at 45% because four fast trips beat two
        // slow ones, and refuses every fight.
        private static readonly ChickenClassInfo Speedy = new ChickenClassInfo(
            ChickenClass.Speedy, mayForage: true,
            shortNameKey: UiKeys.ClassSpeedyShort, roleKey: UiKeys.RoleSpeedy,
            strongKey: UiKeys.CalloutSpeedyStrong, weakKey: UiKeys.CalloutSpeedyWeak,
            bot: new BotProfile(
                    strategy: BotStrategy.Runner,
                    returnThreshold: 0.45f, protectCargoThreshold: 0.10f, dangerRadius: 7.5f,
                    huntRadius: 0f, huntCargoThreshold: 1f, engageRadius: 0f,
                    guardRadius: 0f, roundTripBias: 1.0f, leaderFocus: 0f,
                    thinkInterval: 0.22f));

        // Fatty — Bunker. Does not chase (HuntRadius 0) but is NOT passive: GuardRadius
        // is what makes its whole control kit reachable. Anything that walks within 7 m
        // of the pile it is working, or of its own base, eats Ground Quake / Belly Flop.
        // Highest cargo capacity, so it banks in single big hauls.
        private static readonly ChickenClassInfo Fatty = new ChickenClassInfo(
            ChickenClass.Fatty, mayForage: true,
            shortNameKey: UiKeys.ClassFattyShort, roleKey: UiKeys.RoleFatty,
            strongKey: UiKeys.CalloutFattyStrong, weakKey: UiKeys.CalloutFattyWeak,
            bot: new BotProfile(
                    strategy: BotStrategy.Bunker,
                    returnThreshold: 0.90f, protectCargoThreshold: 0.35f, dangerRadius: 6.0f,
                    huntRadius: 0f, huntCargoThreshold: 1f, engageRadius: 0f,
                    guardRadius: 7.0f, roundTripBias: 0.6f, leaderFocus: 0.15f,
                    thinkInterval: 0.30f));

        // Assassin — Predator. Cannot forage at all (Peck excludes it), so 100% of its
        // income is theft plus the execute bounty. Hunts across the map, weights the
        // scoreboard hardest, and banks the moment it holds anything worth losing.
        private static readonly ChickenClassInfo Assassin = new ChickenClassInfo(
            ChickenClass.Assassin, mayForage: false,
            shortNameKey: UiKeys.ClassAssassinShort, roleKey: UiKeys.RoleAssassin,
            strongKey: UiKeys.CalloutAssassinStrong, weakKey: UiKeys.CalloutAssassinWeak,
            bot: new BotProfile(
                    strategy: BotStrategy.Predator,
                    returnThreshold: 0.30f, protectCargoThreshold: 0.05f, dangerRadius: 6.5f,
                    huntRadius: 16.0f, huntCargoThreshold: 0.05f, engageRadius: 7.0f,
                    guardRadius: 0f, roundTripBias: 0f, leaderFocus: 1.0f,
                    thinkInterval: 0.25f));

        private static readonly ChickenClassInfo[] Table = { Warrior, Speedy, Fatty, Assassin };

        /// <summary>Every class, in enum order.</summary>
        public static readonly ChickenClass[] All =
            { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        /// <summary>
        /// The classes of the three solo bots in Free mode, by seat: bot i plays <c>SoloBotRoster[i]</c> (the human is
        /// the fourth chicken). One list for the spawn (<c>MatchBootstrapper</c>), THE COOP's seats and the bot names.
        /// </summary>
        public static readonly ChickenClass[] SoloBotRoster =
            { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        /// <summary>True (and the entry) for a class the table defines.</summary>
        public static bool TryGet(ChickenClass cls, out ChickenClassInfo info)
        {
            int i = (int)cls;
            if (i >= 0 && i < Table.Length && Table[i].Class == cls) { info = Table[i]; return true; }
            info = default;
            return false;
        }

        /// <summary>The entry of <paramref name="cls"/>; the Warrior's for a value the table does not define
        /// (the fallback every per-class switch it replaced had).</summary>
        public static ChickenClassInfo Of(ChickenClass cls) => TryGet(cls, out var info) ? info : Warrior;

        /// <summary>The class's bit in an <c>AllowedClasses</c> mask; None for a value that is not a class.</summary>
        public static ChickenClassFlags FlagOf(ChickenClass cls) =>
            Enum.IsDefined(typeof(ChickenClass), cls) ? (ChickenClassFlags)(1 << (int)cls) : ChickenClassFlags.None;

        /// <summary>True (and the class) when <paramref name="flags"/> has exactly one class bit set.</summary>
        public static bool TryGetSingleClass(ChickenClassFlags flags, out ChickenClass cls)
        {
            for (int i = 0; i < All.Length; i++)
                if (flags == FlagOf(All[i])) { cls = All[i]; return true; }
            cls = default;
            return false;
        }

        /// <summary>May <paramref name="cls"/> forage? True for a value the table does not define, as before.</summary>
        public static bool MayForage(ChickenClass cls) => !TryGet(cls, out var info) || info.MayForage;
    }
}
