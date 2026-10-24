using System;
using CluckWars.Progression;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// What <see cref="RoundAnnouncer"/> asks the match for, and only on an edge. Implemented by
    /// <see cref="GameManager"/>; an interface rather than delegates so the per-frame tick
    /// allocates nothing.
    /// </summary>
    public interface IRoundSnapshotSource
    {
        /// <summary>The rules the round that just started is played under. Called once, on the Started edge.</summary>
        RoundRuleset CaptureRuleset();

        /// <summary>
        /// Every actor's result and the local human's actor id. Called once, on the Ended edge —
        /// before <c>GameManager.RestartMatch</c> zeroes the bases.
        /// </summary>
        (RoundStandings standings, int localActorId) CaptureEndSnapshot();
    }

    /// <summary>
    /// Announces <c>RoundStarted</c> / <c>RoundEnded</c> to the <see cref="IMatchEventSink"/>
    /// from a polled <see cref="MatchState"/>. Plain C#, so the round emits can be tested
    /// behaviourally without a runner; <see cref="GameManager"/> ticks it from
    /// <c>LateUpdate</c> on every peer.
    /// </summary>
    /// <remarks>
    /// <b>Duration.</b> <c>GameManager.TimeRemaining</c> reads 0 as soon as the state leaves
    /// Active, so the announcer keeps the last value it saw while Active and reports
    /// <c>ruleset duration − that value</c>. The intro countdown is excluded, because
    /// <c>TimeRemaining</c> freezes at the full duration until "GO!". A late joiner reports the
    /// round's elapsed time, not how long it watched.
    /// </remarks>
    public sealed class RoundAnnouncer
    {
        private readonly IMatchEventSink _sink;
        private readonly IRoundSnapshotSource _source;
        private readonly RoundEdgeDetector _edges = new RoundEdgeDetector();

        private RoundRuleset _ruleset;
        private float _lastActiveTimeRemaining;

        public RoundAnnouncer(IMatchEventSink sink, IRoundSnapshotSource source)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>Feeds this frame's state. Emits at most one event.</summary>
        public void Tick(MatchState state, float timeRemaining)
        {
            switch (_edges.Observe(state))
            {
                case RoundEdge.Started:
                    _ruleset = _source.CaptureRuleset();
                    _lastActiveTimeRemaining = _ruleset.RoundDurationSeconds;
                    _sink.RoundStarted(_ruleset);
                    break;

                case RoundEdge.Ended:
                    var (standings, localActorId) = _source.CaptureEndSnapshot();
                    float duration = Mathf.Max(0f, _ruleset.RoundDurationSeconds - _lastActiveTimeRemaining);
                    _sink.RoundEnded(standings, localActorId, duration);
                    break;
            }

            if (state == MatchState.Active) _lastActiveTimeRemaining = timeRemaining;
        }
    }
}
