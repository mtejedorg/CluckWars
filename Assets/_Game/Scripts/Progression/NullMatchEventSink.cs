namespace CluckWars.Progression
{
    /// <summary>
    /// A sink that ignores every match event. Every method is an intentional no-op.
    /// </summary>
    /// <remarks>
    /// A Null implementation is legitimate, not a swallowed failure: "nobody is listening" is a legal
    /// state, so under the silent-failure sorting rule (<c>docs/CONVENTIONS.md</c>) it stays silent. It is
    /// no longer bound at runtime — <c>ProjectInstaller</c> binds the <see cref="MatchTracker"/> inside a
    /// <see cref="GuardedMatchEventSink"/> — and stays for tests and as the inner sink of guard tests.
    /// </remarks>
    public sealed class NullMatchEventSink : IMatchEventSink
    {
        public void RoundStarted(RoundRuleset ruleset) { }
        public void ResourceBanked(int actorId, float amount) { }
        public void ResourceStolen(int actorId, int victimActorId, float amount) { }
        public void OpponentDisabled(int actorId) { }
        public void AbilityResolved(int actorId, string abilityKey, bool connected) { }
        public void RoundEnded(RoundStandings standings, int localActorId, float durationSeconds) { }
    }
}
