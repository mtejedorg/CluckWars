namespace CluckWars.Progression
{
    /// <summary>
    /// One-way channel from a round in progress to the progression layer. Gameplay announces
    /// what happened; it never asks anything back and never learns whether anyone is listening.
    /// </summary>
    /// <remarks>
    /// <b>Actors are opaque.</b> <c>actorId</c> is an identity progression buckets by and never
    /// interprets. Gameplay derives every id with <c>MatchActorId.Of</c> (the actor's
    /// <c>NetworkId</c> raw value); <c>MatchActorId.None</c> (0) means "no actor". Ids are unique
    /// within a session only — bucket by them per round, never persist them as identities.
    /// <para>
    /// <b>Implementations must not throw; callers may rely on the bound sink never throwing
    /// (<c>ProjectInstaller</c> wraps it in <see cref="GuardedMatchEventSink"/>).</b> Every call is
    /// made inline, in the middle of a gameplay effect: <c>ResourceStolen</c>, for one, fires just
    /// before the victim's drain RPC in four steal sites, so an exception escaping here would abort
    /// a steal half-applied.
    /// </para>
    /// <para>
    /// <b>Emit sites never filter.</b> Every actor's stream arrives here, bots included (in solo
    /// the local peer holds state authority over them), and the sink buckets it per actor. Which
    /// bucket is the local human's is named once, by <see cref="RoundEnded"/>.
    /// </para>
    /// </remarks>
    public interface IMatchEventSink
    {
        /// <summary>A round began under <paramref name="ruleset"/>.</summary>
        void RoundStarted(RoundRuleset ruleset);

        /// <summary><paramref name="actorId"/> banked <paramref name="amount"/> of the scoring resource.</summary>
        /// <remarks>
        /// This is the flush attempt, one per batched deposit: the receiving side can still refuse it.
        /// A match-end bonus paid straight into a total is not announced here either, so per-actor
        /// sums of this event need not equal the final totals. Rewards based on what was banked should
        /// read the totals in <see cref="RoundEnded"/>'s standings.
        /// </remarks>
        void ResourceBanked(int actorId, float amount);

        /// <summary><paramref name="actorId"/> took <paramref name="amount"/> of carried resource from <paramref name="victimActorId"/>.</summary>
        void ResourceStolen(int actorId, int victimActorId, float amount);

        /// <summary><paramref name="actorId"/> took an opponent out of play.</summary>
        void OpponentDisabled(int actorId);

        /// <summary>
        /// <paramref name="actorId"/> resolved an ability. <paramref name="abilityKey"/> is
        /// <c>AbilityBaseSO.UnlockKey</c>; <paramref name="connected"/> is true when the cast
        /// reached at least one target.
        /// </summary>
        void AbilityResolved(int actorId, string abilityKey, bool connected);

        /// <summary>
        /// The round is over. <paramref name="localActorId"/> names the local human's actor in
        /// <paramref name="standings"/>; <paramref name="durationSeconds"/> is how long it actually ran.
        /// </summary>
        void RoundEnded(RoundStandings standings, int localActorId, float durationSeconds);
    }
}
