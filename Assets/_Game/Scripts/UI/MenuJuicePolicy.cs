using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// The pure timing, curves and decisions behind the menu juice (<see cref="MenuJuice"/>,
    /// <see cref="MatchCelebration"/>): squash-pop, staggered entries,
    /// fly-to-slot, the READY stamp, the 3-2-1 and the particle paths. Kept free of UI Toolkit and
    /// of the preference so every number and every "when does this fire" decision is unit tested.
    /// </summary>
    /// <remarks>
    /// All durations are seconds. Nothing here reads Reduced Motion: the one gate is
    /// <see cref="MenuJuice.Allowed"/>, applied by the effect entry points.
    /// </remarks>
    public static class MenuJuicePolicy
    {
        // ---- Squash-pop (class tile, deck card, perk badge, slot icon on landing) --------------
        public const float PopSeconds = 0.18f;
        /// <summary>Scale multiplier at the squash and at the overshoot (1 -> 0.92 -> 1.06 -> 1).</summary>
        public const float PopSquash = 0.92f, PopOvershoot = 1.06f;

        /// <summary>
        /// Rest scale of a selected tile / perk badge in USS (`.cw-class-tile--selected`,
        /// `.cw-perk-badge--selected`). The pop multiplies it, so it ends exactly where the stylesheet
        /// puts the element and releasing the inline value does not jump. A test pins these to the USS.
        /// A picked deck card rests at scale 1 (Phase 4, re-audit item 8: picked is the gold glow, not
        /// a size, so an equipped card never rests at a different size or height than its row).
        /// </summary>
        public const float TileRestScale = 1.04f, PerkRestScale = 1.03f;

        /// <summary>Scale multiplier <paramref name="t"/> seconds into a pop: 1 before and after, 0.92 at 36%, 1.06 at 72%.</summary>
        public static float PopFactor(float t)
        {
            float u = Mathf.Clamp01(t / PopSeconds);
            if (u < 0.36f) return Mathf.Lerp(1f, PopSquash, Smooth(u / 0.36f));
            if (u < 0.72f) return Mathf.Lerp(PopSquash, PopOvershoot, Smooth((u - 0.36f) / 0.36f));
            return Mathf.Lerp(PopOvershoot, 1f, Smooth((u - 0.72f) / 0.28f));
        }

        // ---- Staggered entries ------------------------------------------------------------------
        public const float StaggerStepSeconds = 0.04f;
        /// <summary>Latest start of any item: with <see cref="EntrySeconds"/> the whole entry stays under 400 ms.</summary>
        public const float StaggerMaxDelaySeconds = 0.22f;
        public const float EntrySeconds = 0.15f;
        public const float EntryRisePx = 18f;

        /// <summary>Start delay of item <paramref name="index"/> of <paramref name="count"/>: 40 ms apart, compressed so the last starts by 220 ms.</summary>
        public static float StaggerDelay(int index, int count)
        {
            if (count <= 1 || index <= 0) return 0f;
            float step = Mathf.Min(StaggerStepSeconds, StaggerMaxDelaySeconds / (count - 1));
            return Mathf.Min(index, count - 1) * step;
        }

        /// <summary>Seconds from the page appearing until the last item has settled.</summary>
        public static float StaggerTotal(int count) => StaggerDelay(count - 1, count) + EntrySeconds;

        // ---- Fly-to-slot -------------------------------------------------------------------------
        /// <summary>0.28 -> 0.22 s in Phase 4: the touch now has its own soft tap, so the thunk on landing comes sooner.</summary>
        public const float FlightSeconds = 0.22f;
        /// <summary>The ghost waits one frame so the slot it is flying to has been laid out.</summary>
        public const float FlightStartDelaySeconds = 0.02f;
        /// <summary>Peak lift of the flight arc, panel points.</summary>
        public const float FlightArcPx = 36f;
        /// <summary>Concurrent ghosts; a third flight lands the oldest early.</summary>
        public const int GhostPoolSize = 3;

        public static float EaseInOut(float u) => Smooth(Mathf.Clamp01(u));

        public static float EaseOutCubic(float u) { u = 1f - Mathf.Clamp01(u); return 1f - u * u * u; }

        /// <summary>Upward lift (positive = up) at flight progress <paramref name="u"/>: 0 at both ends.</summary>
        public static float FlightArc(float u) => FlightArcPx * 4f * u * (1f - u);

        // ---- READY stamp -------------------------------------------------------------------------
        public const float StampSlamSeconds = 0.18f, StampSettleSeconds = 0.08f;
        public const float StampFromScale = 1.6f, StampImpactScale = 0.97f, StampFromDegrees = -9f;
        /// <summary>Wait after THE COOP appears, so the seats have risen before the slam.</summary>
        public const float StampStartDelaySeconds = 0.25f;
        /// <summary>A stamp that is not the "everyone is ready" banner leaves again after this long.</summary>
        public const float StampHoldSeconds = 1.4f;
        public const float StampSeconds = StampSlamSeconds + StampSettleSeconds;
        /// <summary>The frame the stamp lands on: sound, dust and sparkles fire here.</summary>
        public const float StampHitSeconds = StampSlamSeconds;

        public static float StampScale(float t)
        {
            if (t <= 0f) return StampFromScale;
            if (t < StampSlamSeconds)
            {
                float u = t / StampSlamSeconds;
                return Mathf.Lerp(StampFromScale, StampImpactScale, u * u * u);   // accelerating slam
            }
            if (t < StampSeconds) return Mathf.Lerp(StampImpactScale, 1f, EaseOutCubic((t - StampSlamSeconds) / StampSettleSeconds));
            return 1f;
        }

        public static float StampDegrees(float t)
        {
            if (t <= 0f) return StampFromDegrees;
            if (t >= StampSlamSeconds) return 0f;
            float u = t / StampSlamSeconds;
            return Mathf.Lerp(StampFromDegrees, 0f, u * u);
        }

        /// <summary>Fades in over the first 60 ms of the slam.</summary>
        public static float StampOpacity(float t) => Mathf.Clamp01(t / 0.06f);

        // ---- In-game intro countdown (3-2-1-GO), presentation only ---------------------------------
        public const float CountdownPopSeconds = 0.22f;

        /// <summary>Numeral scale <paramref name="t"/> seconds after a beat starts: slams in from 1.7 with a little overshoot, then rests.</summary>
        public static float CountdownScale(float t)
        {
            if (t >= CountdownPopSeconds) return 1f;
            float u = Mathf.Clamp01(t / CountdownPopSeconds);
            const float s = 1.2f;                                    // ease-out-back
            float v = u - 1f;
            return Mathf.LerpUnclamped(1.7f, 1f, 1f + (s + 1f) * v * v * v + s * v * v);
        }

        // ---- Particles ---------------------------------------------------------------------------
        public const int MaxBurstParticles = 6, ParticlePoolSize = 16;
        public const float BurstSeconds = 0.5f;

        /// <summary>Distance covered <paramref name="u"/> (0..1) of the way through a burst: fast out, then settling.</summary>
        public static float BurstDistance(float u, float speed) => speed * EaseOutCubic(u);

        /// <summary>Size multiplier of a sparkle: grows then shrinks to nothing.</summary>
        public static float SparkleScale(float u) => Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);

        /// <summary>Opacity of a particle: full for the first third, then fading out.</summary>
        public static float BurstOpacity(float u) => 1f - Smooth(Mathf.Clamp01((u - 0.33f) / 0.67f));

        // ---- Winner celebration (post-match) ------------------------------------------------------
        public const int CelebrationParticles = 9;

        /// <summary>
        /// Looping fall: where a particle is, 0 (just above the top) .. 1 (just below the bottom),
        /// <paramref name="time"/> seconds in, for a particle with <paramref name="cycleSeconds"/> per
        /// pass and a <paramref name="phase"/> in 0..1 so they are not in step. Always in [0, 1).
        /// </summary>
        public static float DriftFall(float time, float cycleSeconds, float phase) =>
            Mathf.Repeat(time / Mathf.Max(0.01f, cycleSeconds) + phase, 1f);

        /// <summary>Side-to-side drift in points at <paramref name="time"/>.</summary>
        public static float DriftSway(float time, float amplitude, float hz, float phase) =>
            amplitude * Mathf.Sin((time * hz + phase) * 2f * Mathf.PI);

        /// <summary>Fades in at the top and out at the bottom so a looping particle never pops.</summary>
        public static float DriftOpacity(float fall, float max) =>
            max * Mathf.Min(Mathf.Clamp01(fall / 0.12f), Mathf.Clamp01((1f - fall) / 0.12f));

        private static float Smooth(float u) => u * u * (3f - 2f * u);
    }

    /// <summary>
    /// Turns the networked intro timer (<c>GameManager.IntroActive</c> / remaining seconds) into local cues:
    /// a <see cref="Cue.Tick"/> each time the whole-second number changes, one <see cref="Cue.Go"/> when the
    /// intro ends, and only if this client actually saw it running. A late joiner after GO gets no stray GO;
    /// one arriving mid-intro starts at the current number; losing the match (<c>gm</c> gone) resets silently.
    /// Pure and stateful per client: it never changes any timing.
    /// </summary>
    public sealed class IntroCueTracker
    {
        public enum Cue { None, Tick, Go }

        /// <summary>How long the GO! flourish stays on screen after the intro timer runs out.</summary>
        public const float GoHoldSeconds = 0.6f;

        private int _lastNumber = -1;
        private bool _sawIntro;

        /// <param name="introActive">The intro timer is running.</param>
        /// <param name="remainingSeconds">Seconds left (read only while active).</param>
        /// <param name="number">The numeral to show for a <see cref="Cue.Tick"/> (3, 2, 1).</param>
        public Cue Observe(bool introActive, float remainingSeconds, out int number)
        {
            number = 0;
            if (introActive)
            {
                _sawIntro = true;
                int n = Mathf.Max(1, Mathf.CeilToInt(remainingSeconds));
                if (n == _lastNumber) return Cue.None;
                _lastNumber = n;
                number = n;
                return Cue.Tick;
            }
            if (!_sawIntro) return Cue.None;
            Reset();
            return Cue.Go;
        }

        /// <summary>Forget everything without a GO (match state gone, session ended).</summary>
        public void Reset() { _lastNumber = -1; _sawIntro = false; }
    }

    /// <summary>
    /// What the intro overlay shows BEFORE a round's intro is armed (re-audit item 4): GET READY (no
    /// digit) while the session is still bringing its first GameManager up or while a round is in
    /// <see cref="CluckWars.Gameplay.MatchState.Starting"/>. Never once a manager has been seen and then
    /// lost (host change, session ending) or while BACK TO LOBBY is leaving, and never for longer than
    /// <see cref="NoManagerTimeoutSeconds"/> waiting for a manager that does not arrive (the caller logs
    /// an error and hides the card, so the player is not stuck behind it).
    /// </summary>
    public static class IntroOverlayRule
    {
        public enum PreIntro
        {
            /// <summary>Not a pre-intro moment: the normal countdown / GO / hidden logic applies.</summary>
            None,
            /// <summary>Hold the overlay up with GET READY and no digit.</summary>
            GetReady,
            /// <summary>No GameManager after the timeout: hide, and report it once.</summary>
            TimedOut,
        }

        public const float NoManagerTimeoutSeconds = 10f;

        /// <param name="secondsWithoutManager">Real seconds this overlay has existed without ever seeing a manager.</param>
        public static PreIntro Decide(bool hasManager, CluckWars.Gameplay.MatchState state, bool sawManager,
            bool leavingToLobby, float secondsWithoutManager)
        {
            if (hasManager) return state == CluckWars.Gameplay.MatchState.Starting ? PreIntro.GetReady : PreIntro.None;
            if (sawManager || leavingToLobby) return PreIntro.None;
            return secondsWithoutManager >= NoManagerTimeoutSeconds ? PreIntro.TimedOut : PreIntro.GetReady;
        }
    }

    /// <summary>
    /// When the comeback event banner may show (re-audit item 4). The event's gameplay fires on
    /// schedule (the last <c>MatchConfigSO.ComebackEventSecondsLeft</c> seconds since round 2, so in
    /// practice long after GO); only its DISPLAY waits until the GO! flourish has gone, so the two never
    /// stack whatever the config says. A banner that shows right after GO skips its sting: the GO stinger
    /// has just played.
    /// </summary>
    public static class EventBannerTiming
    {
        /// <summary>Within this long of the intro ending, the banner shows without its sting.</summary>
        public const float QuietAfterGoSeconds = 2f;

        /// <param name="introActive">The intro countdown is running.</param>
        /// <param name="secondsSinceIntro">Real seconds since this client last saw the intro running
        /// (<see cref="float.PositiveInfinity"/> if it never did: a peer that arrived after GO).</param>
        public static bool CanShow(bool introActive, float secondsSinceIntro) =>
            !introActive && secondsSinceIntro >= IntroCueTracker.GoHoldSeconds;

        public static bool PlaysSting(float secondsSinceIntro) => secondsSinceIntro >= QuietAfterGoSeconds;
    }
}
