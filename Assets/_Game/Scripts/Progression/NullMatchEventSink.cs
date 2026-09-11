namespace CluckWars.Progression
{
    /// <summary>
    /// The sink bound while nothing consumes match events. Every method is an intentional no-op.
    /// </summary>
    /// <remarks>
    /// A Null implementation is legitimate here, not a swallowed failure: "nobody is listening"
    /// is a legal state, so under the silent-failure sorting rule (<c>docs/CONVENTIONS.md</c>) it
    /// stays silent. Slice 2 rebinds <see cref="IMatchEventSink"/> to the real tracker in
    /// <c>ProjectInstaller</c>.
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
