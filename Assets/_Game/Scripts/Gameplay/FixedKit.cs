using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The one rule for <see cref="LoadoutMode.Fixed"/>: (class, specialization) -> four abilities, read from the kit authored on
    /// <see cref="PassiveAbilitySO.Kit"/>. Pure (no scene), so the spawner, GEAR UP, LastSetup, the Balance Editor and the
    /// data-integrity test share it. The kit contents live in the assets, never here: this class only says what a legal kit is.
    /// </summary>
    /// <remarks>
    /// A kit bypasses <c>AllowedClasses</c> on purpose: Death Mark and Peck are None-class (pre-equip-only), and a kit is
    /// exactly such a pre-equip. A kit that fails <see cref="Problems"/> is never used: the caller reports it and takes the Free path,
    /// so a forager still gets its Peck.
    /// </remarks>
    public static class FixedKit
    {
        /// <summary>Every rule one kit breaks, in words that name the passive; empty when it is legal.</summary>
        public static List<string> Problems(PassiveAbilitySO passive)
        {
            var problems = new List<string>();
            if (passive == null) { problems.Add("no specialization"); return problems; }

            string who = passive.name;
            var kit = passive.Kit;
            if (kit == null || kit.Length != AbilityController.SlotCount)
            {
                problems.Add($"{who}: the kit has {kit?.Length ?? 0} abilities, expected {AbilityController.SlotCount}.");
                if (kit == null) return problems;
            }

            for (int i = 0; i < kit.Length; i++)
            {
                if (kit[i] == null) { problems.Add($"{who}: kit slot {i} is empty."); continue; }
                if (kit[i] is PassiveAbilitySO) problems.Add($"{who}: kit slot {i} holds passive '{kit[i].name}'.");
                if (Array.IndexOf(kit, kit[i]) != i) problems.Add($"{who}: '{kit[i].name}' is in the kit twice.");
            }

            if (!PreEquippedLoadout.TryGetSingleClass(passive.AllowedClasses, out var cls))
            {
                problems.Add($"{who}: AllowedClasses ({passive.AllowedClasses}) must be exactly one class.");
                return problems;
            }

            bool hasPeck = kit.Any(a => a is PeckAbilitySO);
            if (PreEquippedLoadout.ClassMayForage(cls) && !hasPeck)
                problems.Add($"{who}: a {cls} must carry its Peck (food only enters through it).");
            if (!PreEquippedLoadout.ClassMayForage(cls) && hasPeck)
                problems.Add($"{who}: a {cls} cannot forage, so its kit must not hold a Peck.");

            // Speedy's trip is what the SCT axiom measures: a kit that walks through terrain would cut it unmeasured.
            if (cls == ChickenClass.Speedy)
                foreach (var a in kit.Where(a => a != null && a.TerrainTraversal != TerrainTraversal.None))
                    problems.Add($"{who}: Speedy kits grant no terrain traversal, but '{a.name}' has {a.TerrainTraversal}.");

            if (passive.Subclass == ChickenSubclass.Assassin_Spoiler && !kit.Any(a => a is MarkKillAbilitySO))
                problems.Add($"{who}: the Spoiler kit must hold Death Mark (its execute is the class's income).");
            // An execute only qualifies on a stunned target, so the mark without a stun source of its own waits for luck.
            if (kit.Any(a => a is MarkKillAbilitySO) && !kit.Any(EmitsStun))
                problems.Add($"{who}: Death Mark needs a stun source in the same kit (none emits ControlKind.Stun).");

            return problems;
        }

        /// <summary>True when <paramref name="ability"/> stuns (read from <see cref="AbilityBaseSO.EmittedControl"/>).</summary>
        public static bool EmitsStun(AbilityBaseSO ability) =>
            ability != null && ability.EmittedControl.Kind == ControlKind.Stun && ability.EmittedControl.Seconds > 0f;

        /// <summary>
        /// Every kit problem in a roster, for the Balance Editor and the data-integrity test: each passive's
        /// <see cref="Problems"/>, each subclass having one passive, and every kit ability being a registered pool asset.
        /// </summary>
        public static List<string> ValidateRoster(IEnumerable<PassiveAbilitySO> passives, IEnumerable<AbilityBaseSO> pool)
        {
            var problems = new List<string>();
            var seen = new HashSet<ChickenSubclass>();
            var registered = new HashSet<AbilityBaseSO>(pool?.Where(a => a != null) ?? Enumerable.Empty<AbilityBaseSO>());

            foreach (var p in passives ?? Enumerable.Empty<PassiveAbilitySO>())
            {
                if (p == null) continue;
                seen.Add(p.Subclass);
                problems.AddRange(Problems(p));
                foreach (var a in p.Kit ?? Array.Empty<AbilityBaseSO>())
                    if (a != null && !registered.Contains(a))
                        problems.Add($"{p.name}: '{a.name}' is not in the ability registry.");
            }

            foreach (ChickenSubclass s in Enum.GetValues(typeof(ChickenSubclass)))
                if (s != ChickenSubclass.None && !seen.Contains(s))
                    problems.Add($"Subclass {s} has no specialization, so it has no kit.");
            return problems;
        }

        /// <summary>
        /// The slots to play: <paramref name="kit"/> is a fresh copy in slot order when <paramref name="mode"/> is Fixed and
        /// the passive's kit is legal. False means "take the Free path"; <paramref name="problem"/> then says why it was
        /// refused, or is null when Free was simply the mode.
        /// </summary>
        public static bool TryResolve(LoadoutMode mode, PassiveAbilitySO passive, out AbilityBaseSO[] kit, out string problem)
        {
            kit = null;
            problem = null;
            if (mode != LoadoutMode.Fixed) return false;

            var problems = Problems(passive);
            if (problems.Count > 0) { problem = string.Join(" ", problems); return false; }
            kit = (AbilityBaseSO[])passive.Kit.Clone();
            return true;
        }

        /// <summary>
        /// The specialization each solo bot plays in Fixed mode: <paramref name="count"/> bots from one seed, so the menu's
        /// COOP and the spawner compute the same lineup from <c>SoloCornerSeed</c>. Classes are dealt from a shuffled deck, so
        /// they are distinct until the classes run out; each class then rolls one of its specializations, so all of them are
        /// reachable. <paramref name="forced"/> (dev tools, <c>cw_botperks</c>) pins a bot by index; None rolls as usual.
        /// Null entries mean the roster has nothing playable for that bot.
        /// </summary>
        public static PassiveAbilitySO[] RollBotLineup(int seed, int count, IEnumerable<PassiveAbilitySO> passives,
            IReadOnlyList<ChickenSubclass> forced = null)
        {
            var lineup = new PassiveAbilitySO[Math.Max(0, count)];
            var byClass = new SortedDictionary<ChickenClass, List<PassiveAbilitySO>>();
            foreach (var p in passives ?? Enumerable.Empty<PassiveAbilitySO>())
            {
                if (p == null || !PreEquippedLoadout.TryGetSingleClass(p.AllowedClasses, out var cls)) continue;
                if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new List<PassiveAbilitySO>();
                list.Add(p);
            }
            if (byClass.Count == 0) return lineup;
            foreach (var list in byClass.Values) list.Sort((a, b) => a.Subclass.CompareTo(b.Subclass));

            var rng = new Random(unchecked(seed ^ LineupSalt));
            var deck = byClass.Keys.ToList();
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            for (int i = 0; i < lineup.Length; i++)
            {
                var specs = byClass[deck[i % deck.Count]];
                lineup[i] = specs[rng.Next(specs.Count)];   // drawn even when forced, so one pin leaves the rest as rolled
                var pin = forced != null && i < forced.Count ? forced[i] : ChickenSubclass.None;
                if (pin == ChickenSubclass.None) continue;
                foreach (var list in byClass.Values)
                    foreach (var p in list)
                        if (p.Subclass == pin) lineup[i] = p;
            }
            return lineup;
        }

        // Decorrelates the lineup from the corner permutation, which is seeded from the same SoloCornerSeed.
        private const int LineupSalt = 0x5bd1e995;
    }
}
