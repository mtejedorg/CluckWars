using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The rule that decides whether a victim's impact may claim a direction.
    /// <see cref="HitAttribution"/> is deliberately free of Fusion and MonoBehaviour types
    /// precisely so every branch can be exercised here without a live <c>NetworkRunner</c> —
    /// which matters more than usual, because the thing it replaced (a nearest-live-rival
    /// proximity scan inside <c>HitFeedback</c>) was untestable and therefore manufactured
    /// blame for months without a single red test.
    /// </summary>
    /// <remarks>
    /// The two properties worth stating up front, because most of these tests exist to pin
    /// one of them:
    /// <list type="number">
    ///   <item><b>Both observation orders work.</b> The victim learns it was hit from its own
    ///   replicated state; the attacker is named from a different peer's cast event. Either
    ///   can be seen first, so an impact may claim a waiting attribution and a late
    ///   attribution may retro-claim a waiting impact.</item>
    ///   <item><b>Silence is a decision, never an error.</b> No attribution and no bearing
    ///   both mean "we do not know", and the correct output for "we do not know" is nothing
    ///   at all.</item>
    /// </list>
    /// </remarks>
    public sealed class HitAttributionTests
    {
        private const float Window = 0.25f;

        private static readonly Vector3 Attacker = new Vector3(3f, 0f, 4f);
        private static readonly Vector3 Victim   = new Vector3(0f, 0f, 0f);

        /// <summary>A second, distinguishable source — used where a test has to prove
        /// <i>which</i> of two pushes a later impact ended up claiming.</summary>
        private static readonly Vector3 OtherAttacker = new Vector3(-6f, 0f, 1f);

        private static HitAttribution New() => new HitAttribution(Window);

        // ---- Claim ordering ---------------------------------------------------

        [Test]
        public void Impact_WithNoAttribution_Declines()
        {
            var a = New();

            Assert.IsFalse(a.TryClaimAtImpact(0f, out var pos),
                "An impact nobody has claimed must not produce a direction. This is the whole " +
                "fix: the old code answered this question with a proximity scan.");
            Assert.AreEqual(Vector3.zero, pos, "A declined claim must not hand back a position.");
        }

        [Test]
        public void AttributionThenImpact_InsideWindow_Claims()
        {
            var a = New();
            a.RecordAttribution(Attacker, 0f, out _);

            Assert.IsTrue(a.TryClaimAtImpact(Window * 0.5f, out var pos));
            Assert.AreEqual(Attacker, pos, "The claimed position must be the one that was pushed.");
        }

        [Test]
        public void AttributionThenImpact_OutsideWindow_Declines()
        {
            var a = New();
            a.RecordAttribution(Attacker, 0f, out _);

            Assert.IsFalse(a.TryClaimAtImpact(Window + 0.01f, out _),
                "An attacker named a full window ago belongs to an earlier exchange. Letting it " +
                "serve this impact would point at whoever last hit you, not whoever just did.");
        }

        [Test]
        public void ImpactThenAttribution_InsideWindow_RetroClaims()
        {
            var a = New();
            Assert.IsFalse(a.TryClaimAtImpact(0f, out _), "Precondition: the impact plays with no direction.");

            Assert.IsTrue(a.RecordAttribution(Attacker, Window * 0.5f, out var claimed),
                "A late attribution must be able to light the direction for an impact that " +
                "already flashed — the two halves of one hit arrive from different peers.");
            Assert.AreEqual(Attacker, claimed);
        }

        [Test]
        public void ImpactThenAttribution_OutsideWindow_DeclinesButStillReportsThePosition()
        {
            var a = New();
            a.TryClaimAtImpact(0f, out _);

            Assert.IsFalse(a.RecordAttribution(Attacker, Window + 0.01f, out var claimed),
                "The impact is too old to still be claimable.");
            Assert.AreEqual(Attacker, claimed,
                "`claimed` always echoes what was pushed; the bool is what says whether to play it.");
        }

        // ---- Bookkeeping guards ----------------------------------------------

        [Test]
        public void StaleSource_IsDiscardedByTheImpactThatMissedIt()
        {
            // Freshness is a two-sided window (an age below zero is not fresh either), which is
            // what makes the discard observable at all: with a strictly monotonic clock a stale
            // source only ever gets staler, so "cleared" and "still there but too old" look
            // identical from the outside. Rewinding here pins the bookkeeping directly, so a
            // refactor cannot leave a source lying around for a later impact to pick up.
            var a = New();
            a.RecordAttribution(Attacker, 100f, out _);

            Assert.IsFalse(a.TryClaimAtImpact(0f, out _), "Precondition: a source stamped ahead is not claimable.");
            Assert.IsFalse(a.TryClaimAtImpact(100f, out _),
                "The impact that missed a stale source must discard it. Otherwise the source " +
                "survives to serve an unrelated later impact.");
        }

        [Test]
        public void PendingImpact_IsResolvedByATooLateAttribution()
        {
            // Same reason as above for the rewind: monotonic callers cannot distinguish
            // "resolved" from "still pending but too old". Pinned directly so a future change
            // cannot leave an impact permanently pending, claimable by any attribution forever.
            var a = New();
            a.TryClaimAtImpact(0f, out _);
            a.RecordAttribution(Attacker, 100f, out _);   // too late to claim — but must resolve it

            Assert.IsFalse(a.RecordAttribution(Attacker, 0f, out _),
                "The pending impact was resolved by the attribution that arrived too late for it. " +
                "A later attribution must not be able to claim that same old impact again.");
        }

        [Test]
        public void SecondAttribution_InTheSameWindow_PlaysNoSecondBeatButStaysArmed()
        {
            // Most steals in the game now push an attribution TWICE for one theft, and this is
            // the property that makes that free rather than a double directional beat.
            //
            // Every steal in the pool funnels through ChickenCargo.RPC_DrainStolen, which names
            // the thief and bumps a one-shot event id that each peer observes in Render() — so
            // the drain itself is a push point. Three of the four steal ABILITIES already had
            // one: Snatch, Sneaky Steal and Scrap re-derive their target set in
            // HitFeedback.ConfirmHits, which pushes the caster's position to every victim it
            // finds. Both fire on the same peer in the same frame, so each of those three steals
            // records twice. (The fourth, Dive Bomb, is CastPoseIsUnreconstructable and so gets
            // no push from ConfirmHits at all; Spine Coat, whose contact drain has no cast event
            // to hang off, is the reason the drain push exists.)
            //
            // The duplicate is absorbed HERE rather than deduplicated at the two call sites,
            // because neither call site can see the other — they run on different components off
            // different replicated facts. The first record claims the waiting impact and clears
            // it; the second finds nothing pending and returns false, so no second fan of motion
            // lines and no second bearing arc.
            //
            // The second half of the assertion matters just as much: the redundant record must
            // still ARM the window. A Spine Coat contact lands its knockback in the same tick as
            // its drain, and the victim's knockback edge can be observed a frame or two after the
            // pushes — it has to be able to claim a direction too. Pinning only "the second
            // record plays nothing" would read as "the second record is inert", which is wrong,
            // and would wave through a refactor that returned early whenever no impact was
            // pending.
            var a = New();

            Assert.IsFalse(a.TryClaimAtImpact(0f, out _),
                "Precondition: the cargo drop plays a directionless impact first.");

            Assert.IsTrue(a.RecordAttribution(Attacker, 0.05f, out var firstClaim),
                "Precondition: the first of the two pushes retro-claims that waiting impact.");
            Assert.AreEqual(Attacker, firstClaim, "The claimed position must be the one that was pushed.");

            Assert.IsFalse(a.RecordAttribution(OtherAttacker, 0.20f, out var secondClaim),
                "The second push for the same theft must not play a second directional beat — " +
                "the impact it would have claimed was already resolved by the first.");
            Assert.AreEqual(OtherAttacker, secondClaim,
                "`claimed` still echoes what was pushed; the bool is what says whether to play it.");

            // 0.40 s is deliberately outside the FIRST push's window (0.05 + 0.25 = 0.30) and
            // inside the SECOND's (0.20 + 0.25 = 0.45), so only a genuinely re-armed source can
            // serve it. Together with the position check that pins the re-arm on both axes.
            Assert.IsTrue(a.TryClaimAtImpact(0.40f, out var laterClaim),
                "The redundant record is not inert: it re-arms the window, so the knockback edge " +
                "arriving a beat later still gets a direction.");
            Assert.AreEqual(OtherAttacker, laterClaim,
                "And it re-arms with the position the SECOND push carried, not the first — a " +
                "source that survived here would be a stale attacker serving a newer impact.");
        }

        [Test]
        public void OneAttribution_ServesEveryImpactInItsWindow()
        {
            // A single cast routinely lands a knockback and a stun on the same victim in the
            // same frame. Both are that one attacker's doing, so consuming the attribution on
            // the first would leave the second silently directionless.
            var a = New();
            a.RecordAttribution(Attacker, 0f, out _);

            Assert.IsTrue(a.TryClaimAtImpact(0f, out var first));
            Assert.IsTrue(a.TryClaimAtImpact(0f, out var second),
                "The attribution must NOT be consumed by a successful claim.");
            Assert.AreEqual(first, second);
        }

        [Test]
        public void Default_CannotCarryAnAttributionAcrossAnyElapsedTime()
        {
            // A zero window is the correct failure mode for a type whose job is refusing to
            // guess: a field that was never constructed goes quiet rather than going wild.
            //
            // "Quiet" is precise rather than absolute — the freshness test is inclusive at both
            // ends, so a zero window still admits an attribution and an impact carrying the
            // IDENTICAL timestamp. That is unreachable in practice (the two halves are observed
            // from different objects on different frames, which is the entire reason the window
            // exists) and not worth a boundary change that would also move the real 0.25 s one.
            // What matters is that nothing survives even one tick, which is what this pins.
            var a = default(HitAttribution);

            Assert.AreEqual(0f, a.WindowSeconds);
            Assert.IsFalse(a.TryClaimAtImpact(1f, out _), "Nothing has been pushed.");

            a.RecordAttribution(Attacker, 1f, out _);
            Assert.IsFalse(a.TryClaimAtImpact(1.0001f, out _),
                "A zero window means an attribution is stale by the very next observation.");

            a.TryClaimAtImpact(2f, out _);
            Assert.IsFalse(a.RecordAttribution(Attacker, 2.0001f, out _),
                "And an impact is likewise unclaimable by the very next observation.");
        }

        [Test]
        public void NegativeWindow_IsClampedToZero()
        {
            Assert.AreEqual(0f, new HitAttribution(-5f).WindowSeconds);
        }

        // ---- Bearing ----------------------------------------------------------

        [Test]
        public void Bearing_PointsFromAttackerToVictim_AndIsUnitLength()
        {
            Assert.IsTrue(HitAttribution.TryBearing(
                new Vector3(5f, 0f, 0f), Vector3.zero, 0.35f, out var dir));

            // The victim is east of the attacker, so the incoming direction is +X — the motion
            // lines are laid on the incoming side and point AT the victim.
            Assert.AreEqual(1f, dir.x, 0.0001f);
            Assert.AreEqual(0f, dir.z, 0.0001f);
            Assert.AreEqual(1f, dir.magnitude, 0.0001f);
        }

        [Test]
        public void Bearing_IsPlanar_AndIgnoresHeight()
        {
            Assert.IsTrue(HitAttribution.TryBearing(
                new Vector3(0f, 9f, 5f), new Vector3(0f, -3f, 0f), 0.35f, out var dir));

            Assert.AreEqual(0f, dir.y, 0.0001f,
                "A bearing is drawn on the ground plane; a y component would tilt the motion lines.");
            Assert.AreEqual(1f, dir.z, 0.0001f);
        }

        [Test]
        public void Bearing_DeclinesBelowMinSeparation()
        {
            Assert.IsFalse(HitAttribution.TryBearing(
                    new Vector3(0.2f, 0f, 0f), Vector3.zero, 0.35f, out var dir),
                "An attacker standing inside the victim's own footprint has no meaningful " +
                "bearing; normalising that vector would manufacture one.");
            Assert.AreEqual(Vector3.zero, dir);
        }

        [Test]
        public void Bearing_DeclinesOnExactCoincidence()
        {
            Assert.IsFalse(HitAttribution.TryBearing(Victim, Victim, 0f, out _),
                "Zero separation must decline even when minSeparation is zero — there is no " +
                "direction to normalise, and Vector3.zero.normalized is silently Vector3.zero.");
        }

        [Test]
        public void Bearing_IgnoresAPureHeightOffset()
        {
            Assert.IsFalse(HitAttribution.TryBearing(
                    Vector3.zero, new Vector3(0f, 6f, 0f), 0.35f, out _),
                "An attacker directly overhead has no planar bearing. Collapsing the height " +
                "into a direction would point the lines at an arbitrary compass heading.");
        }

        [Test]
        public void Bearing_AcceptsExactlyAtMinSeparation()
        {
            // The boundary is inclusive on purpose: HitAttributionMinSeparation is authored as
            // "closer than this is noise", so the distance itself is still information.
            Assert.IsTrue(HitAttribution.TryBearing(
                new Vector3(0.35f, 0f, 0f), Vector3.zero, 0.35f, out _));
        }
    }
}
