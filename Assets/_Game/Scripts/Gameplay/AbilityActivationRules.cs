namespace CluckWars.Gameplay
{
    /// <summary>
    /// Why an ability slot refuses to fire right now (FEEDBACK.md §6). Computed live
    /// by <see cref="AbilityController.EvaluateRefusal"/> for the HUD to poll — this
    /// enum is deliberately NOT <c>[Networked]</c>; the button-greying HUD only ever
    /// runs on the input authority, which for a chicken's own <c>AbilityController</c>
    /// is the same client as its state authority (Shared Mode).
    /// </summary>
    public enum AbilityRefusal : byte
    {
        None = 0,
        Cooldown = 1,
        NoTarget = 2,
        Stunned = 3,
        OtherAbilityActive = 4,
        SlotUnavailable = 5,
    }

    /// <summary>
    /// Pure precedence table for <see cref="AbilityRefusal"/> — the ordering itself,
    /// decoupled from any live <c>NetworkBehaviour</c> state so it is EditMode-testable
    /// without a <c>NetworkRunner</c>. <see cref="AbilityController.EvaluateRefusal"/>
    /// (the query the HUD polls) and <c>AbilityController.TryActivate</c>'s own refusal
    /// gate both route every input through this single table, so they can never
    /// disagree about which reason wins when more than one applies at once — e.g. a
    /// stunned chicken whose slot also happens to be on cooldown always reports
    /// Cooldown, from both call sites, because both ask the same question here.
    /// </summary>
    public static class AbilityRefusalRules
    {
        public static AbilityRefusal Evaluate(
            bool slotUnavailable, bool onCooldown, bool stunned, bool otherAbilityActive, bool noTarget)
        {
            if (slotUnavailable) return AbilityRefusal.SlotUnavailable;
            if (onCooldown) return AbilityRefusal.Cooldown;
            if (stunned) return AbilityRefusal.Stunned;
            if (otherAbilityActive) return AbilityRefusal.OtherAbilityActive;
            if (noTarget) return AbilityRefusal.NoTarget;
            return AbilityRefusal.None;
        }
    }

    /// <summary>What <see cref="AbilityController.FixedUpdateNetwork"/> should do this
    /// tick, as decided by <see cref="AbilityHoldStateMachine.Decide"/>.</summary>
    public enum ChargeAction : byte
    {
        /// <summary>Nothing to do this tick.</summary>
        None = 0,
        /// <summary>
        /// Promote <see cref="ChargeDecision.Slot"/> from pending to a real, everyone-visible
        /// aim charge: the caller sets <c>ChargingSlot</c> and clears the pending state. Only
        /// ever reached once a pending hold has outlasted
        /// <c>FeedbackTuning.TapHoldThresholdSeconds</c> — this is the moment the player
        /// starts paying for the aim (telegraph, target marks, wind-up glow, aim-rotate
        /// movement lock all key off <c>ChargingSlot</c>).
        /// </summary>
        BeginCharge = 1,
        /// <summary>Fire <see cref="ChargeDecision.Slot"/> now — a release, from any of the
        /// three states a slot can be released from (charging, pending, or never observed
        /// held at all because the whole tap landed between ticks).</summary>
        Fire = 2,
        /// <summary>Clear the current charge <i>and</i> any pending hold without firing and
        /// without burning cooldown.</summary>
        Cancel = 3,
        /// <summary>
        /// A hold began on a slot that can never fire (unavailable for this class, or on
        /// cooldown) and a press landed the same tick. Route straight to
        /// <c>TryActivate</c> so the normal refusal logging fires once, instead of
        /// silently ignoring the input — a pressed-but-dead button must never go quiet.
        /// </summary>
        RefuseAttempt = 4,
        /// <summary>
        /// A hold just started on a live slot. Record <see cref="ChargeDecision.Slot"/> as
        /// <i>pending</i> — caller-local scratch, deliberately NOT the networked
        /// <c>ChargingSlot</c> — and start clocking how long it stays held. Nothing is drawn
        /// and nothing is locked in this state: a release from here fires at full movement
        /// speed with no telegraph, which is Maestro's "if not, there is speed" half of the
        /// casting model.
        /// </summary>
        BeginPendingHold = 5,
    }

    /// <summary>One tick's verdict: what to do, and which slot it concerns (meaningless
    /// for <see cref="ChargeAction.None"/> / <see cref="ChargeAction.Cancel"/>).</summary>
    public readonly struct ChargeDecision
    {
        public readonly ChargeAction Action;
        public readonly int Slot;

        public ChargeDecision(ChargeAction action, int slot = -1)
        {
            Action = action;
            Slot = slot;
        }

        public static readonly ChargeDecision None = new ChargeDecision(ChargeAction.None);
        public static readonly ChargeDecision Cancel = new ChargeDecision(ChargeAction.Cancel);
    }

    /// <summary>
    /// Pure decision function for one <c>FixedUpdateNetwork</c> tick of the hold /
    /// release / cancel state machine (FEEDBACK.md §2). No Fusion, no
    /// <c>ChickenController</c>, no ability assets — every input is a plain bool/byte/float
    /// so the whole state machine is EditMode-testable.
    ///
    /// <b>Firing rule (Maestro, v0.6.1): there is no tap window. An ability ALWAYS fires on
    /// release.</b> Nothing waits out a threshold before committing, and no branch here ever
    /// swallows an input. What the threshold decides is only <i>how much the gesture cost</i>:
    ///
    /// <list type="bullet">
    ///   <item><b>Below</b> <c>FeedbackTuning.TapHoldThresholdSeconds</c> — the slot sits in
    ///   the <i>pending</i> state (<see cref="ChargeAction.BeginPendingHold"/>). Caller-local
    ///   scratch only; <c>ChargingSlot</c> stays 0, so the ground telegraph, the target marks,
    ///   the opponent-visible wind-up glow and the aim-rotate movement lock — all four of
    ///   which key off <c>ChargingSlot</c> — never engage. Release fires at full speed.</item>
    ///   <item><b>At or above</b> it — the pending hold is promoted to
    ///   <see cref="ChargeAction.BeginCharge"/>, <c>ChargingSlot</c> goes live, and the
    ///   player pays the movement cost in exchange for the aim.</item>
    /// </list>
    ///
    /// The state machine therefore has three live states for a slot, not two: charging
    /// (networked), pending (local), and idle.
    /// </summary>
    public static class AbilityHoldStateMachine
    {
        /// <summary>Sentinel for <c>pendingSlot</c>: no hold is currently pending. Numerically
        /// equal to <c>AbilityController.InvalidSlot</c>, restated here so this file stays
        /// dependency-free.</summary>
        public const int NoPendingSlot = -1;


        /// <param name="chargingSlot">Current <c>AbilityController.ChargingSlot</c> encoding: 0 = none, 1..N = slot+1.</param>
        /// <param name="pendingSlot">Slot whose hold is being clocked but has not yet earned a
        /// charge, or <see cref="NoPendingSlot"/>. Caller-owned local scratch — never networked.</param>
        /// <param name="pendingHeldSeconds">How long <paramref name="pendingSlot"/> has been held,
        /// accumulated by the caller from <c>Runner.DeltaTime</c>. Ignored when there is no pending slot.</param>
        /// <param name="tapHoldThresholdSeconds">The cost/feedback ramp boundary,
        /// <c>FeedbackTuning.TapHoldThresholdSeconds</c>. Passed in rather than referenced so this
        /// stays a pure function of its arguments.</param>
        /// <param name="canCast">False while stunned (<c>ControlRules.CanCast</c>).</param>
        /// <param name="otherAbilityActive">True while a different ability is already mid-duration (no double-cast).</param>
        /// <param name="cancelPressed">This tick's edge-triggered cancel bit (Esc / touch drag-off).</param>
        /// <param name="hold">One live hold bit per slot. Its LENGTH defines how many slots
        /// exist — the count is deliberately not a constant in here, so growing the loadout
        /// (2 slots -> 3 -> 4) needs no edit to this file and cannot leave a stale 3 behind.</param>
        /// <param name="press">Same length as <paramref name="hold"/>: this tick's latched press-edge bit per slot.</param>
        /// <param name="canBeginCharge">Same length as <paramref name="hold"/>: true if the slot is available for this
        /// class AND off cooldown. Deliberately excludes target-in-range — a target may
        /// walk into the shape mid-hold, which is the entire point of aiming, so
        /// "no target yet" must never block a hold from starting.</param>
        public static ChargeDecision Decide(
            byte chargingSlot, int pendingSlot, float pendingHeldSeconds, float tapHoldThresholdSeconds,
            bool canCast, bool otherAbilityActive, bool cancelPressed,
            bool[] hold, bool[] press, bool[] canBeginCharge)
        {
            int slotCount = hold != null ? hold.Length : 0;
            if (press == null || canBeginCharge == null ||
                press.Length != slotCount || canBeginCharge.Length != slotCount)
            {
                // Ragged inputs would index past the end of one array while reading a live
                // bit from another — a crash at best, a phantom cast at worst. Refuse the
                // tick instead; the caller owns all three arrays and sizes them together.
                return ChargeDecision.None;
            }

            bool pendingLive = pendingSlot >= 0 && pendingSlot < slotCount;

            // 1. Caster can no longer complete a cast: clear BOTH pre-fire states without
            // firing/burning cooldown. An already-ACTIVE ability (mid-duration) is a
            // separate concern the caller handles elsewhere — this only ever cancels the
            // pre-fire aiming states.
            if (!canCast || otherAbilityActive)
                return (chargingSlot != 0 || pendingLive) ? ChargeDecision.Cancel : ChargeDecision.None;

            // 2. Explicit cancel (Esc / touch drag-off) kills either pre-fire state.
            if (cancelPressed && (chargingSlot != 0 || pendingLive))
                return ChargeDecision.Cancel;

            // 3. A committed charge outranks everything else: it is the gesture the player
            // is already visibly aiming, so nothing may steal the tick from it.
            if (chargingSlot != 0)
            {
                int slot = chargingSlot - 1;
                // chargingSlot is a NETWORKED byte that persists across ticks, so unlike
                // pendingSlot it can outlive the array it indexes — a loadout that shrank,
                // or a peer on a different build. Cancel rather than index past the end.
                if (slot >= slotCount) return ChargeDecision.Cancel;

                return hold[slot] ? ChargeDecision.None : new ChargeDecision(ChargeAction.Fire, slot);
            }

            // 4. A pending hold likewise owns the tick — "one live gesture at a time".
            if (pendingLive)
            {
                // The sub-threshold tap. It fires like any other release, and because it
                // never entered charge state it never cost a telegraph, a wind-up tell, or
                // a single frame of the aim-rotate movement lock.
                if (!hold[pendingSlot]) return new ChargeDecision(ChargeAction.Fire, pendingSlot);

                // >= not >: a hold that lands exactly on the boundary is a hold. The caller
                // accumulates in whole ticks, so exact equality is reachable in practice
                // (see AbilityController's tick-count note) — treating it as "not yet" would
                // silently push the real threshold out by a full tick.
                if (pendingHeldSeconds >= tapHoldThresholdSeconds)
                    return new ChargeDecision(ChargeAction.BeginCharge, pendingSlot);

                return ChargeDecision.None; // still inside the ramp — keep clocking.
            }

            // 5. Idle. Lowest-numbered slot whose hold bit is set claims the tick, whether
            // or not it can actually begin charging — this preserves "one live gesture at a
            // time" the same way the old press-only code let Ability1 shadow the rest on a
            // same-tick press collision.
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (hold[slot])
                {
                    if (canBeginCharge[slot]) return new ChargeDecision(ChargeAction.BeginPendingHold, slot);
                    if (press[slot]) return new ChargeDecision(ChargeAction.RefuseAttempt, slot);
                    return ChargeDecision.None; // held but dead this tick — wait, don't spam-refuse every tick.
                }
                if (press[slot])
                {
                    // Network-timing edge case, NOT the ordinary human tap: press and
                    // release both landed inside the gap between simulation ticks, so the
                    // hold bit was never observed set for this slot at all. Fires
                    // immediately rather than waiting for a hold that will never arrive —
                    // the "never swallow a fast tap" guarantee FusionNetworkService's press
                    // latch exists to make good on. An ordinary tap goes through the
                    // pending path above instead.
                    return new ChargeDecision(ChargeAction.Fire, slot);
                }
            }
            return ChargeDecision.None;
        }
    }
}
