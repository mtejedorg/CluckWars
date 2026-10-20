using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The round-reset teleport (<c>GameManager.ResetWorldForNewRound</c> -> <c>ChickenController.RPC_TeleportTo</c>), pure so
    /// EditMode tests pin it: which chickens go home, and the one-shot pending target the chicken applies itself.
    /// </summary>
    /// <remarks>
    /// <b>Why the target is applied later, by the chicken, through NetworkTransform.Teleport.</b> The master's reset runs
    /// inside <c>GameManager.FixedUpdateNetwork</c>; for a chicken the master itself simulates (its own human, its bots)
    /// the RPC is a local invocation that ran immediately, mid-tick, and wrote <c>transform.position</c> directly. The
    /// chicken has a Fusion NetworkTransform with shared-mode interpolation on, which keeps its own tick state and puts
    /// the transform back to it around simulation and render, so that write did not survive: in every round >= 2 of the
    /// 2026-10-20 sessions the host's chicken opened where the previous round ended (Maestro: "the second match starts
    /// where the first one ended") while the remote chickens, whose RPC arrives before the simulation step, were home.
    /// Now the RPC only records the target; the chicken applies it at the top of its own state-authority
    /// FixedUpdateNetwork with <c>NetworkTransform.Teleport</c> (state + no interpolation across the arena, on every
    /// peer) and the CharacterController disabled around it - the same path on every peer and in every game mode.
    /// </remarks>
    public static class RoundTeleportRules
    {
        /// <summary>
        /// Every chicken in the round goes home except a Doppelganger decoy. Humans are included whatever their
        /// PlayerRef looks like (the old filter skipped any chicken whose input authority was not a "real player" and was
        /// not a bot; in Single mode the local human must never depend on that).
        /// </summary>
        public static bool ShouldTeleport(bool valid, bool isDecoy, bool isBot, bool inputAuthorityIsRealPlayer, bool singleMode) =>
            valid && !isDecoy && (isBot || inputAuthorityIsRealPlayer || singleMode);
    }

    /// <summary>A one-shot teleport target: set by the RPC, consumed once by the chicken's own simulation tick.</summary>
    public struct PendingTeleport
    {
        private bool _pending;
        private Vector3 _target;

        public bool IsPending => _pending;

        public void Request(Vector3 target)
        {
            _pending = true;
            _target = target;
        }

        public bool TryConsume(out Vector3 target)
        {
            target = _target;
            if (!_pending) return false;
            _pending = false;
            return true;
        }

        public void Clear() => _pending = false;
    }
}
