using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// What a Thief bot does about its loot mark (BL-217, round 2, Maestro 2026-11-24: "enseñar al bot"): pure, next to <see cref="BotTactics"/>,
    /// every input a number or a flag, so <c>BotController</c> (later) and the simulator's <c>ScriptedPolicy</c> (now) call the same rules.
    /// Three questions: is the marked rival worth walking to (<see cref="Pursue"/>), is a Steal on someone else a waste while it is
    /// (<see cref="HoldSteal"/>), and is it time to bank (<see cref="BankNow"/>).
    /// </summary>
    /// <remarks>
    /// Why the game's bot needs them (measured in the match simulator, 2026-11-24, 420 Thief matches under P0): marks are drawn 34 m from the
    /// Thief on average and only 13 % inside the bot's 16 m hunt radius; 51 % of marks saw Steal spent on an unmarked rival; while a mark was
    /// live the bot walked toward the marked bird 6 % of the time. P0 consumed 1.47 of 3.34 marks drawn per match.
    /// </remarks>
    public static class BotLootRules
    {
        /// <summary>
        /// Walk to the marked rival, at any distance, by the wall-aware path to where it was last seen. False (abandon, play the ordinary rules and
        /// let the mark move) when there is no live mark, when the bot has given a hidden rival up (<see cref="BotSightRules"/>), when the rival is
        /// shelled (the cast would be refused) or when it stands inside its own base's wall (no ability crosses it).
        /// </summary>
        /// <param name="catchable"><see cref="CanIntercept"/>: the Thief can reach the marked bird before it is home. Defaults to true for callers that do not know.</param>
        public static bool Pursue(bool markLive, bool known, bool shelled, bool behindWall, bool catchable = true) =>
            markLive && known && !shelled && !behindWall && catchable;

        /// <summary>
        /// Can the Thief still catch the marked bird (Maestro, 2026-11-27: "decide if it reaches him; if not, use Steal on another bird and trust the mark
        /// will move")? Worst case the bird runs straight home: the Thief must be able to get within <paramref name="reach"/> of the point where the bird
        /// crosses its base's wall no later than the bird gets there (the Thief may cut the corner, so it measures the Thief's own distance to the base).
        /// A bird already inside reach, or of unknown speed, counts as catchable.
        /// </summary>
        public static bool CanIntercept(float thiefToMarkedBase, float markToMarkedBase, float wallRadius, float reach, float thiefSpeed, float markSpeed,
            float distToMark)
        {
            if (distToMark <= reach || markSpeed <= 0.01f || thiefSpeed <= 0.01f) return true;
            float thiefSeconds = Mathf.Max(0f, thiefToMarkedBase - wallRadius - reach) / thiefSpeed;
            float markSeconds = Mathf.Max(0f, markToMarkedBase - wallRadius) / markSpeed;
            return thiefSeconds <= markSeconds;
        }

        /// <summary>
        /// Does the retreat rule (a loaded bot with a rival inside its danger radius runs home) fire? Not while the Thief pursues a live mark:
        /// whenever it carries something it banks by <see cref="BankNow"/>, so a beak that is not banked is one the Thief chose to hold, and the
        /// rival it walks to is the marked one, always inside the danger radius when the cast is due. Measured in the match simulator (Thief
        /// capacity 20, P0): the retreat took 37 015 thinks with a live mark against 909 at capacity 19, and the win rate fell from 36 % to 22 %.
        /// </summary>
        public static bool Retreat(bool retreatTriggered, bool pursuing) => retreatTriggered && !pursuing;

        /// <summary>
        /// Is a rival "behind its wall" for a bot standing outside: inside the keep-out circle of its own base
        /// (<see cref="BaseWallRules.IsInside"/>, the game's own test).
        /// </summary>
        public static bool BehindWall(Vector2 rivalPos, Vector2 rivalBaseCentre, float keepOutRadius) =>
            BaseWallRules.IsInside(new Vector3(rivalPos.x, 0f, rivalPos.y), rivalBaseCentre, keepOutRadius);

        /// <summary>
        /// Keep Steal for the marked rival: a Steal spent on another rival starts the cooldown, and with the marked bird closer than one cooldown's
        /// walk the cooldown would still be running when the bot arrives. Farther than that the cast is free, it is ready again on arrival.
        /// </summary>
        public static bool HoldSteal(bool pursuing, float distToMarked, float moveSpeed, float stealCooldownSeconds) =>
            pursuing && (moveSpeed <= 0.01f || distToMarked / moveSpeed < stealCooldownSeconds);

        /// <summary>
        /// Go home and bank now, for a Thief with the loot mark on. Any of: the clock would run out on the walk
        /// (<see cref="BotTactics.MustBankNow"/>); banking what it carries reaches the win target; or the next marked steal would not fit, so
        /// the free room is below the bounty (Maestro's third note: the loot is ordinary cargo, and what does not fit is lost). The third reason
        /// is the threshold: with a bounty of 10 and a capacity of 14 the bot banks once it holds more than 4, which is after every marked steal;
        /// with 20 it banks after one marked steal and carries on after a poor one. Never true when carrying nothing.
        /// </summary>
        public static bool BankNow(float carried, float capacity, float bounty, float banked, float winTarget,
            float distHome, float moveSpeed, float depositRate, float timeLeft)
        {
            if (carried <= 0f) return false;
            if (BotTactics.MustBankNow(carried, distHome, moveSpeed, timeLeft, carried / Mathf.Max(0.5f, depositRate))) return true;
            if (banked + carried >= winTarget) return true;
            return NextMarkedStealWouldNotFit(carried, capacity, bounty);
        }

        /// <summary>The free room is below the bounty: another marked steal would lose part of it.</summary>
        public static bool NextMarkedStealWouldNotFit(float carried, float capacity, float bounty) =>
            capacity - carried < bounty;
    }
}
