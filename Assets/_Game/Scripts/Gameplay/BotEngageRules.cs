using CluckWars.Abilities;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// When a Brawler bot may leave its work to chase a rival (BL-224). A Warrior kit with no Steal and no hit that currently pays (the sim-only "Brawler"
    /// Bully) used to chase every rival in its 9 to 11 m band with nothing to cast and never forage, and sat at 3 to 7 %. Pure functions over facts, called by
    /// <c>BotController</c> and by the match simulator's P0 so what P0 measures is what the bot plays.
    /// </summary>
    public static class BotEngageRules
    {
        /// <summary>
        /// A hit of this kit pays food now: a Stun, Shove or Slow it holds has an active hit-steal amount, or the bite is on with food (a share above 0) and the
        /// kit holds a Stun, Shove or Steal to land it. A Champion's fresh stack alone is no food: it pays only through the Steal, which already opens the gate.
        /// </summary>
        public static bool HitPays(in HitStealFacts hit, in BiteFacts bite, bool holdsStun, bool holdsShove, bool holdsSlow, bool holdsSteal) =>
            (holdsStun && hit.AmountFor(AbilityJob.Stun) > 0f)
            || (holdsShove && hit.AmountFor(AbilityJob.Shove) > 0f)
            || (holdsSlow && hit.AmountFor(AbilityJob.Slow) > 0f)
            || (bite.Active && bite.FoodShare > 0f && (holdsStun || holdsShove || holdsSteal));

        /// <summary>
        /// The bot may chase a rival: always, except for a Brawler (the Warrior's strategy) that holds no Steal and has no hit that pays, which forages instead.
        /// The other strategies keep their chase whatever they hold (a Predator hunts by its class, a Bunker engages with a stun), so no shipped kit changes:
        /// both shipped Warrior kits hold Steal.
        /// </summary>
        public static bool MayEngage(bool brawler, bool holdsSteal, bool hitPays) => !brawler || holdsSteal || hitPays;
    }
}
