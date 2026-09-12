using System.Collections.Generic;
using System.Linq;
using CluckWars.Progression;

namespace CluckWars.Tests
{
    /// <summary>
    /// Test double for <see cref="IMatchEventSink"/>: an ordered, typed log of every call, so a
    /// test can assert both what was announced and in which order.
    /// </summary>
    internal sealed class RecordingMatchEventSink : IMatchEventSink
    {
        public enum Kind
        {
            RoundStarted,
            ResourceBanked,
            ResourceStolen,
            OpponentDisabled,
            AbilityResolved,
            RoundEnded,
        }

        /// <summary>One recorded call. Only the fields its <see cref="Kind"/> carries are set.</summary>
        public sealed class Call
        {
            public Kind Kind;
            public int ActorId;
            public int VictimActorId;
            public float Amount;
            public string AbilityKey;
            public bool Connected;
            public RoundRuleset Ruleset;
            public RoundStandings Standings;
            public int LocalActorId;
            public float DurationSeconds;
        }

        public readonly List<Call> Calls = new List<Call>();

        /// <summary>The kinds of every call, in order.</summary>
        public Kind[] Kinds => Calls.Select(c => c.Kind).ToArray();

        public List<Call> OfKind(Kind kind) => Calls.Where(c => c.Kind == kind).ToList();

        public void RoundStarted(RoundRuleset ruleset) =>
            Calls.Add(new Call { Kind = Kind.RoundStarted, Ruleset = ruleset });

        public void ResourceBanked(int actorId, float amount) =>
            Calls.Add(new Call { Kind = Kind.ResourceBanked, ActorId = actorId, Amount = amount });

        public void ResourceStolen(int actorId, int victimActorId, float amount) =>
            Calls.Add(new Call { Kind = Kind.ResourceStolen, ActorId = actorId, VictimActorId = victimActorId, Amount = amount });

        public void OpponentDisabled(int actorId) =>
            Calls.Add(new Call { Kind = Kind.OpponentDisabled, ActorId = actorId });

        public void AbilityResolved(int actorId, string abilityKey, bool connected) =>
            Calls.Add(new Call { Kind = Kind.AbilityResolved, ActorId = actorId, AbilityKey = abilityKey, Connected = connected });

        public void RoundEnded(RoundStandings standings, int localActorId, float durationSeconds) =>
            Calls.Add(new Call { Kind = Kind.RoundEnded, Standings = standings, LocalActorId = localActorId, DurationSeconds = durationSeconds });
    }
}
