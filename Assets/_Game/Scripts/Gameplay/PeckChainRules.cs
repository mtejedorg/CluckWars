using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>Why a Peck chain ended. <see cref="CargoFull"/> is the only one that plays a sound.</summary>
    public enum PeckChainStop : byte
    {
        None = 0,
        MatchEnded = 1,
        Dead = 2,
        Stunned = 3,
        OtherCast = 4,
        Moved = 5,
        CargoFull = 6,
        NoPile = 7,
        /// <summary>A knockback impulse landed (KnockbackEventId changed). Body pushes raise no such event.</summary>
        Knocked = 8,
    }

    /// <summary>What a tick of the chain resolves to.</summary>
    public enum PeckChainAction : byte
    {
        /// <summary>No chain is running: nothing to do (and nothing here ever starts one).</summary>
        Idle = 0,
        /// <summary>Chain alive, Peck still on cooldown.</summary>
        Wait = 1,
        /// <summary>Chain alive and Peck ready: fire the next peck through the normal activation path.</summary>
        Fire = 2,
        /// <summary>A stop condition holds; the chain ends and does not restart by itself.</summary>
        Stop = 3,
    }

    /// <summary>One tick's worth of facts the chain decision needs, gathered on the state authority.</summary>
    public struct PeckChainInputs
    {
        public bool Active;
        public bool MatchRunning;
        public bool Alive;
        /// <summary>Player MOVE INPUT this tick (not velocity: being shoved is not input).</summary>
        public bool Moving;
        public bool CargoFull;
        /// <summary>A pile with food is within collect range.</summary>
        public bool PileInReach;
        /// <summary>ControlRules.CanCast: false while stunned.</summary>
        public bool CanCast;
        /// <summary>A knockback impulse landed on the bird since the previous chain tick.</summary>
        public bool Knocked;
        /// <summary>A move other than Peck succeeded this tick.</summary>
        public bool OtherAbilityCast;
        public bool PeckReady;
    }

    /// <summary>
    /// Pure rules of the Peck auto-chain and of Auto-Peck (Phase 6 chunk 4, A6). After a manual Peck the bird
    /// keeps pecking each time the per-class cooldown is ready while it stands still by a pile with room to
    /// carry; any failing condition ends the chain and only a new manual peck (or Auto-Peck) starts another.
    /// Decided on the state authority from the networked input; the repeats go through the ordinary
    /// activation path, so cooldown, food credit and feedback are exactly a manual peck's.
    /// </summary>
    public static class PeckChainRules
    {
        /// <summary>Seconds without move input before Auto-Peck starts a peck (the brief's 0.15-0.2 s).</summary>
        public const float AutoPeckIdleSeconds = 0.18f;

        /// <summary>True when the movement vector counts as move input. The touch provider applies its dead zone
        /// before this, so any non-zero vector here is intent.</summary>
        public static bool IsMoving(Vector2 movement) => movement.sqrMagnitude > 0f;

        /// <summary>Seconds the bird has gone without move input: reset by any input, otherwise accumulating.</summary>
        public static float AdvanceIdle(float idleSeconds, bool moving, float dt) =>
            moving ? 0f : idleSeconds + dt;

        /// <summary>First failing stop condition wins; with none, fire when Peck is ready, otherwise wait.</summary>
        public static PeckChainAction Decide(in PeckChainInputs s, out PeckChainStop stop)
        {
            stop = PeckChainStop.None;
            if (!s.Active) return PeckChainAction.Idle;

            if (!s.MatchRunning)    stop = PeckChainStop.MatchEnded;
            else if (!s.Alive)      stop = PeckChainStop.Dead;
            else if (!s.CanCast)    stop = PeckChainStop.Stunned;
            else if (s.Knocked)     stop = PeckChainStop.Knocked;
            else if (s.OtherAbilityCast) stop = PeckChainStop.OtherCast;
            else if (s.Moving)      stop = PeckChainStop.Moved;
            else if (s.CargoFull)   stop = PeckChainStop.CargoFull;
            else if (!s.PileInReach) stop = PeckChainStop.NoPile;

            if (stop != PeckChainStop.None) return PeckChainAction.Stop;
            return s.PeckReady ? PeckChainAction.Fire : PeckChainAction.Wait;
        }

        /// <summary>
        /// Should Auto-Peck start a peck now? The preference bit must be set, the bird must have been idle for
        /// <see cref="AutoPeckIdleSeconds"/>, and every condition a manual peck needs must hold. Never while a
        /// chain already runs (the chain owns the repeats) and never if it would silently break a stealth move
        /// (<paramref name="wouldBreakStealth"/>): Auto-Peck must not cancel an Invisibility the player just cast.
        /// </summary>
        public static bool ShouldAutoStart(bool autoPeckBit, float idleSeconds, bool chainActive, bool pileInReach,
            bool cargoFull, bool canCast, bool peckReady, bool otherAbilityCast, bool aiming, bool wouldBreakStealth) =>
            autoPeckBit && idleSeconds >= AutoPeckIdleSeconds && !chainActive && pileInReach && !cargoFull &&
            canCast && peckReady && !otherAbilityCast && !aiming && !wouldBreakStealth;
    }
}
