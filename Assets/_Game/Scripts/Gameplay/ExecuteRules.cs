using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Receiver-side rules for the two RPCs an Assassin execute sends besides the cargo transfer
    /// (CW-230): <c>ChickenCombat.RPC_ExecuteRemoval</c> on the victim's authority and
    /// <c>ChickenMatchStats.RPC_CreditKill</c> back on the assassin's authority. Pure and
    /// Fusion-free, like <see cref="StealRules"/>: the components gather the facts (who sent it,
    /// what the replicated state says) and this class decides, so EditMode can exercise every branch.
    /// </summary>
    /// <remarks>
    /// Both RPCs are <c>RpcSources.All</c>. Before CW-230 neither looked at its sender, so any peer
    /// could remove any chicken (and, since CW-050 fixed kill credit, award itself kills). The only
    /// legitimate sequence is <c>AssassinExecute.Press</c> on the assassin's state authority:
    /// it records a kill expectation, asks the victim's authority to remove the victim, and the
    /// victim's authority reports the kill back.
    /// </remarks>
    public static class ExecuteRules
    {
        /// <summary>How long an execute removes its victim. The only duration Press sends.</summary>
        public const float RemovalDurationSeconds = 2.0f;

        /// <summary>
        /// Extra planar slack on the execute range check, on top of the drift margin. Covers what
        /// the drift margin does not model: proxy interpolation, the tick between the mark's own
        /// range check and the press, and both chickens' pivots. A forged call from across the
        /// arena is still refused; a real one at the edge of the mark is not.
        /// </summary>
        public const float RangeSlackMeters = 1.0f;

        /// <summary>
        /// How long a kill expectation stays payable. Same two relayed trips as the execute loot
        /// report (assassin -> victim's authority -> assassin), so the same window.
        /// </summary>
        public const float KillCreditWindowSeconds = StealRules.ExecuteLootWindowSeconds;

        /// <summary>
        /// Floor on a remote sender's latency. Whether <c>Runner.GetPlayerRtt</c> reports a real
        /// value for remote players in Shared Mode is unconfirmed, so no allowance may rely on it
        /// being non-zero.
        /// </summary>
        public const float MinRemoteLatencySeconds = 0.15f;

        /// <summary>
        /// How long after the victim last saw an attacker's replicated mark name it a removal or
        /// cargo claim from that attacker still counts as marked, on top of the latency. Covers
        /// Press clearing the mark in the same tick it sends the RPC, so the cleared mark can reach
        /// the victim's peer before (or with) the RPC while the stun that qualified it is over.
        /// </summary>
        public const float RecentMarkGraceSeconds = 0.5f;

        /// <summary>A remote sender's latency for allowances: the measured RTT, never below the floor (NaN reads as the floor).</summary>
        public static float RemoteLatency(float measuredRtt) =>
            measuredRtt > MinRemoteLatencySeconds ? measuredRtt : MinRemoteLatencySeconds;

        /// <summary>
        /// The evidence half of an execute, shared by the removal and the cargo transfer so both
        /// halves accept and refuse together: the attacker's replicated mark names this victim
        /// now, OR the victim is stunned on its own authority, OR the victim saw that mark within
        /// <c>latency + RecentMarkGraceSeconds</c> (the mark-reset race, stun already over).
        /// </summary>
        /// <param name="markEverSeen">The victim has seen this attacker's mark name it at all.</param>
        /// <param name="secondsSinceMarkSeen">Since the victim last saw the mark (ignored when never seen).</param>
        public static bool HasExecuteEvidence(bool markedNow, bool victimStunned, bool markEverSeen,
            float secondsSinceMarkSeen, float latency) =>
            markedNow || victimStunned ||
            (markEverSeen && secondsSinceMarkSeen >= 0f &&
             secondsSinceMarkSeen <= Mathf.Max(0f, latency) + RecentMarkGraceSeconds);

        // ---- RPC_ExecuteRemoval ---------------------------------------------------------

        public enum RemovalVerdict
        {
            Accepted,
            ImplausibleDuration,
            AttackerUnresolved,
            SelfExecute,
            DecoyAttacker,
            NotAttributable,
            NoAssassinExecute,
            NotMarkedNorStunned,
            OutOfRange,
        }

        /// <summary>The facts the victim's authority can establish about a removal claim.</summary>
        public struct RemovalClaim
        {
            public float Duration;
            public bool AttackerResolved;
            public bool AttackerIsVictim;
            public bool AttackerIsDecoy;
            /// <summary>Local invoke: the attacker is simulated here. Off the wire: the sender is its state authority.</summary>
            public bool SenderSpeaksForAttacker;
            public bool AttackerHasExecute;
            /// <summary>The attacker's replicated <c>AssassinExecute.MarkedTarget</c> is this victim.</summary>
            public bool MarkedThisVictim;
            /// <summary>The victim's own (authoritative) stun.</summary>
            public bool VictimStunned;
            /// <summary>The victim has seen this attacker's replicated mark name it (local record).</summary>
            public bool MarkEverSeen;
            /// <summary>Seconds since the victim last saw that mark (ignored unless <see cref="MarkEverSeen"/>).</summary>
            public float SecondsSinceMarkSeen;
            /// <summary>The sender's latency (0 for a local invoke, floored for a remote one).</summary>
            public float Latency;
            public float PlanarDistance;
            public float RangeMargin;
        }

        /// <summary>True for a finite duration in (0, <see cref="RemovalDurationSeconds"/>].</summary>
        public static bool IsPlausibleRemovalDuration(float duration) =>
            duration > 0f && duration <= RemovalDurationSeconds + 1e-4f;

        /// <summary>Furthest apart a real execute's two chickens can be when the removal lands.</summary>
        public static float MaxExecuteDistance(float driftMargin) =>
            AssassinExecute.MaxMarkRange + RangeSlackMeters + Mathf.Max(0f, driftMargin);

        /// <summary>
        /// Decides a removal claim. The evidence rule is the one <c>ChickenCargo.TryResolveExecutor</c>
        /// applies to the cargo half of the same execute, so the two halves accept and refuse together.
        /// </summary>
        /// <remarks>
        /// <b>Why not "marked AND KillReady".</b> Press resets the mark in the same tick it sends
        /// this RPC, and KillReady can turn true one tick before a press, so either replicated value
        /// can be stale when the RPC lands on another peer. KillReady also outlives the stun that
        /// qualified it (stuns 1-1.5 s, window 5 s), so "stunned now" alone is not enough either:
        /// the victim's own recent sighting of the mark (<see cref="HasExecuteEvidence"/>) covers it.
        /// </remarks>
        public static RemovalVerdict EvaluateRemoval(in RemovalClaim c)
        {
            if (!IsPlausibleRemovalDuration(c.Duration)) return RemovalVerdict.ImplausibleDuration;
            if (!c.AttackerResolved) return RemovalVerdict.AttackerUnresolved;
            if (c.AttackerIsVictim) return RemovalVerdict.SelfExecute;
            if (c.AttackerIsDecoy) return RemovalVerdict.DecoyAttacker;
            if (!c.SenderSpeaksForAttacker) return RemovalVerdict.NotAttributable;
            if (!c.AttackerHasExecute) return RemovalVerdict.NoAssassinExecute;
            if (!HasExecuteEvidence(c.MarkedThisVictim, c.VictimStunned, c.MarkEverSeen, c.SecondsSinceMarkSeen, c.Latency))
                return RemovalVerdict.NotMarkedNorStunned;
            if (!(c.PlanarDistance <= MaxExecuteDistance(c.RangeMargin))) return RemovalVerdict.OutOfRange;
            return RemovalVerdict.Accepted;
        }

        public static string Describe(RemovalVerdict v)
        {
            switch (v)
            {
                case RemovalVerdict.ImplausibleDuration: return $"the duration is not in (0, {RemovalDurationSeconds:0.0}] s.";
                case RemovalVerdict.AttackerUnresolved:  return "no live ChickenCombat resolves from the attacker id.";
                case RemovalVerdict.SelfExecute:         return "the named attacker is the victim itself.";
                case RemovalVerdict.DecoyAttacker:       return "the named attacker is a Doppelganger decoy.";
                case RemovalVerdict.NotAttributable:     return "the sender is not entitled to act for the named attacker.";
                case RemovalVerdict.NoAssassinExecute:   return "the named attacker has no AssassinExecute.";
                case RemovalVerdict.NotMarkedNorStunned: return "the attacker has not marked this chicken recently and it is not stunned.";
                case RemovalVerdict.OutOfRange:          return "the attacker is too far away to have executed this chicken.";
                default:                                 return "accepted.";
            }
        }

        // ---- RPC_CreditKill -------------------------------------------------------------

        public enum KillCreditVerdict
        {
            Accepted,
            VictimUnresolved,
            SelfKill,
            DecoyVictim,
            NotAttributable,
            NoPendingExecute,
        }

        /// <summary>The facts the assassin's authority can establish about a kill report.</summary>
        public struct KillCreditClaim
        {
            public bool VictimResolved;
            public bool VictimIsSelf;
            public bool VictimIsDecoy;
            /// <summary>Local invoke: the victim is simulated here. Off the wire: the sender is the victim's state authority.</summary>
            public bool SenderSpeaksForVictim;
        }

        /// <summary>The ledger a chicken's match stats keep their pending executes in.</summary>
        public static PendingStealLedger<TKey> NewKillCreditLedger<TKey>() where TKey : struct =>
            new PendingStealLedger<TKey>(KillCreditWindowSeconds);

        /// <summary>Assassin side, at press time: one kill report from <paramref name="victim"/> is expected.</summary>
        public static void ExpectKillCredit<TKey>(PendingStealLedger<TKey> ledger, TKey victim, float now) where TKey : struct =>
            ledger?.Record(victim, 1f, now);

        /// <summary>
        /// Decides a kill report. The pending expectation is consumed only after every other
        /// check passes, so a forged report can never burn the genuine one.
        /// </summary>
        public static KillCreditVerdict EvaluateKillCredit<TKey>(PendingStealLedger<TKey> ledger, in KillCreditClaim c,
            TKey victim, float now) where TKey : struct
        {
            if (!c.VictimResolved) return KillCreditVerdict.VictimUnresolved;
            if (c.VictimIsSelf) return KillCreditVerdict.SelfKill;
            if (c.VictimIsDecoy) return KillCreditVerdict.DecoyVictim;
            if (!c.SenderSpeaksForVictim) return KillCreditVerdict.NotAttributable;
            if (ledger == null || !ledger.TryConsume(victim, 1f, now, out _)) return KillCreditVerdict.NoPendingExecute;
            return KillCreditVerdict.Accepted;
        }

        public static string Describe(KillCreditVerdict v)
        {
            switch (v)
            {
                case KillCreditVerdict.VictimUnresolved: return "no live chicken resolves from the victim id.";
                case KillCreditVerdict.SelfKill:         return "the named victim is this chicken.";
                case KillCreditVerdict.DecoyVictim:      return "the named victim is a Doppelganger decoy, which scores no kill.";
                case KillCreditVerdict.NotAttributable:  return "the sender is not entitled to speak for the named victim.";
                case KillCreditVerdict.NoPendingExecute: return "this chicken has no pending execute on that victim (unsolicited, expired or already credited).";
                default:                                 return "accepted.";
            }
        }
    }
}
