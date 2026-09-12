using System;
using CluckWars.Logging;

namespace CluckWars.Progression
{
    /// <summary>
    /// Wraps the bound <see cref="IMatchEventSink"/> so that nothing it throws can reach the code
    /// that made the announcement.
    /// </summary>
    /// <remarks>
    /// Every announcement is an inline call made in the middle of a gameplay effect — a steal
    /// announces just before the victim's drain RPC — so an exception escaping the sink would leave
    /// that effect half-applied. <c>ProjectInstaller</c> binds the sink as guard(inner):
    /// <see cref="NullMatchEventSink"/> today, the real tracker from slice 2. Callers may therefore
    /// rely on the bound sink never throwing.
    /// <para>
    /// <b>One Error per kind, then counting.</b> The first failure of each event kind is logged as an
    /// Error naming the verb and carrying the exception — what a reader of a device log with no
    /// debugger attached needs. Later failures of the same kind are only counted
    /// (<see cref="FailureCount"/>): a broken sink would otherwise put an Error on every deposit and
    /// cast and bury everything else in the log (CONVENTIONS.md, silent-failure sorting rule).
    /// </para>
    /// </remarks>
    public sealed class GuardedMatchEventSink : IMatchEventSink
    {
        /// <summary>The six verbs of <see cref="IMatchEventSink"/>, for <see cref="FailureCount"/>.</summary>
        public enum Verb
        {
            RoundStarted,
            ResourceBanked,
            ResourceStolen,
            OpponentDisabled,
            AbilityResolved,
            RoundEnded,
        }

        private const string Source = "MatchEvents";

        private readonly IMatchEventSink _inner;
        private readonly ILogService _log;
        private readonly int[] _failures = new int[(int)Verb.RoundEnded + 1];

        public GuardedMatchEventSink(IMatchEventSink inner, ILogService log)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>The wrapped sink.</summary>
        public IMatchEventSink Inner => _inner;

        /// <summary>How many times <paramref name="verb"/> has thrown inside the wrapped sink.</summary>
        public int FailureCount(Verb verb) => _failures[(int)verb];

        public void RoundStarted(RoundRuleset ruleset)
        {
            try { _inner.RoundStarted(ruleset); }
            catch (Exception e) { OnFailure(Verb.RoundStarted, e); }
        }

        public void ResourceBanked(int actorId, float amount)
        {
            try { _inner.ResourceBanked(actorId, amount); }
            catch (Exception e) { OnFailure(Verb.ResourceBanked, e); }
        }

        public void ResourceStolen(int actorId, int victimActorId, float amount)
        {
            try { _inner.ResourceStolen(actorId, victimActorId, amount); }
            catch (Exception e) { OnFailure(Verb.ResourceStolen, e); }
        }

        public void OpponentDisabled(int actorId)
        {
            try { _inner.OpponentDisabled(actorId); }
            catch (Exception e) { OnFailure(Verb.OpponentDisabled, e); }
        }

        public void AbilityResolved(int actorId, string abilityKey, bool connected)
        {
            try { _inner.AbilityResolved(actorId, abilityKey, connected); }
            catch (Exception e) { OnFailure(Verb.AbilityResolved, e); }
        }

        public void RoundEnded(RoundStandings standings, int localActorId, float durationSeconds)
        {
            try { _inner.RoundEnded(standings, localActorId, durationSeconds); }
            catch (Exception e) { OnFailure(Verb.RoundEnded, e); }
        }

        private void OnFailure(Verb verb, Exception exception)
        {
            int failures = ++_failures[(int)verb];
            if (failures > 1) return; // already reported once; counted from here on

            _log.Error(Source,
                $"{_inner.GetType().Name}.{verb} threw {exception.GetType().Name}: {exception.Message} " +
                "The announcement was dropped and play carries on. Further " + verb +
                " failures are counted (GuardedMatchEventSink.FailureCount), not logged.",
                exception);
        }
    }
}
