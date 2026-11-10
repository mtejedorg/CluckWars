namespace CluckWars.Visuals
{
    /// <summary>
    /// I5: when the thief's own <c>+n</c> pop is shown. The cargo write lands at the cast, about 0.35 s before the
    /// stolen kernels (<see cref="FoodChunkFlight"/>) reach the thief, so the pop is held and spawned as the last
    /// kernel arrives. Pure and presentation-only: the cargo value itself changes at the cast exactly as before.
    /// </summary>
    /// <remarks>
    /// One instance per thief (owned by its <see cref="HitFeedback"/>). Every steal gain seen while a hold is open
    /// is summed into one pop. Release rules, in order:
    /// <list type="bullet">
    ///   <item>a kernel flight to this thief landed and none is still flying: release now;</item>
    ///   <item>no flight is flying (none launched: hidden-thief reroute, or one was culled from the pool) and
    ///   <see cref="FeedbackTuning.StealSelfPopFallbackSeconds"/> passed: release;</item>
    ///   <item>a flight is still flying after <see cref="FeedbackTuning.StealSelfPopMaxHoldSeconds"/>: release anyway,
    ///   so a pop is never lost to a flight whose arrival was never reported.</item>
    /// </list>
    /// Flights are counted even with no hold open: on the thief's own peer the victim can launch its kernels in a
    /// Render pass that runs before the thief's cargo change is observed in the same frame.
    /// </remarks>
    public sealed class StealGainPopHold
    {
        private float _pending;
        private float _elapsed;
        private int _flightsLive;

        /// <summary>A steal gain is waiting for its kernels.</summary>
        public bool IsHolding => _pending > 0f;

        /// <summary>The summed gain waiting to pop.</summary>
        public float Pending => _pending;

        /// <summary>Kernel flights to this thief still in the air.</summary>
        public int FlightsLive => _flightsLive;

        /// <summary>
        /// Is the cargo gain observed on <paramref name="frame"/> the thief's own steal credit, written on
        /// <paramref name="creditFrame"/> (-1: none)? The credit is observed by the Render of the same frame, or
        /// the next one at the latest; anything older is some other gain and pops at once.
        /// </summary>
        public static bool IsStealCredit(int creditFrame, int frame) =>
            creditFrame >= 0 && frame >= creditFrame && frame - creditFrame <= 1;

        /// <summary>Queues a steal gain of <paramref name="delta"/> (summed into an open hold).</summary>
        public void Hold(float delta)
        {
            if (delta <= 0f) return;
            if (_pending <= 0f) _elapsed = 0f;
            _pending += delta;
        }

        /// <summary>A kernel flight toward this thief started.</summary>
        public void FlightLaunched() => _flightsLive++;

        /// <summary>
        /// A kernel flight toward this thief landed. Returns the gain to pop now (0: nothing held, or another
        /// flight is still in the air).
        /// </summary>
        public float FlightArrived()
        {
            if (_flightsLive > 0) _flightsLive--;
            return _flightsLive == 0 ? Release() : 0f;
        }

        /// <summary>A flight toward this thief ended without arriving (its mover was recycled). The fallback timer decides.</summary>
        public void FlightCulled()
        {
            if (_flightsLive > 0) _flightsLive--;
        }

        /// <summary>Advances the hold by <paramref name="dt"/>. Returns the gain to pop now (0: still holding, or nothing held).</summary>
        public float Tick(float dt)
        {
            if (_pending <= 0f) return 0f;
            _elapsed += dt;
            bool fallback = _flightsLive == 0 && _elapsed >= FeedbackTuning.StealSelfPopFallbackSeconds;
            bool capped = _elapsed >= FeedbackTuning.StealSelfPopMaxHoldSeconds;
            return fallback || capped ? Release() : 0f;
        }

        /// <summary>Drops everything (despawn, pooled reuse): a held pop must not land on the next occupant.</summary>
        public void Clear()
        {
            _pending = 0f;
            _elapsed = 0f;
            _flightsLive = 0;
        }

        private float Release()
        {
            float released = _pending;
            _pending = 0f;
            _elapsed = 0f;
            return released;
        }
    }
}
