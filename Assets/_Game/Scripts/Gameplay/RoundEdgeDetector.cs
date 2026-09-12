namespace CluckWars.Gameplay
{
    /// <summary>What one observation of <see cref="MatchState"/> meant for the round.</summary>
    public enum RoundEdge
    {
        None,
        Started,
        Ended,
    }

    /// <summary>
    /// Turns a stream of <see cref="MatchState"/> observations into round start/end edges.
    /// Pure and allocation-free, so the round lifecycle can be tested without a runner.
    /// </summary>
    /// <remarks>
    /// Rules, in order:
    /// <list type="bullet">
    ///   <item>The <b>first observation</b> is Started if it is <see cref="MatchState.Active"/>
    ///   (a late joiner walks into a running round) and None otherwise.</item>
    ///   <item>Any change into Active is Started — both <c>GameManager.StartMatch</c> and the
    ///   restart path, which moves Ended → Active.</item>
    ///   <item>Active → <see cref="MatchState.Ended"/> is Ended.</item>
    ///   <item>Active → anything else is an abandoned round: it leaves the round with no Ended
    ///   edge. Nothing sets such a state today; this keeps it well-defined if something does.</item>
    ///   <item>A repeated state is None, so each transition yields exactly one edge.</item>
    /// </list>
    /// <b>Invariant:</b> Ended is never returned unless Started was returned first. It holds by
    /// construction — Ended requires the previous observation to be Active, and every
    /// observation that makes the previous one Active has already returned Started.
    /// </remarks>
    public sealed class RoundEdgeDetector
    {
        private bool _hasObserved;
        private MatchState _previous;

        /// <summary>Feeds one observation and returns the edge it produced, if any.</summary>
        public RoundEdge Observe(MatchState state)
        {
            if (!_hasObserved)
            {
                _hasObserved = true;
                _previous = state;
                return state == MatchState.Active ? RoundEdge.Started : RoundEdge.None;
            }

            if (state == _previous) return RoundEdge.None;

            bool wasActive = _previous == MatchState.Active;
            _previous = state;

            if (state == MatchState.Active) return RoundEdge.Started;
            if (wasActive && state == MatchState.Ended) return RoundEdge.Ended;
            return RoundEdge.None; // abandoned round, or a change between two non-Active states
        }

        /// <summary>Forgets everything, so the next observation is a first observation again.</summary>
        public void Reset()
        {
            _hasObserved = false;
            _previous = default;
        }
    }
}
