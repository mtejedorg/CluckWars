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
        public static bool Pursue(bool markLive, bool known, bool shelled, bool behindWall) => markLive && known && !shelled && !behindWall;

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
