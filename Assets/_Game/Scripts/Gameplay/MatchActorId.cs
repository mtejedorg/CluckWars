using Fusion;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The one way gameplay derives the opaque <c>actorId</c> it hands to
    /// <see cref="CluckWars.Progression.IMatchEventSink"/>. Every emit site and the round
    /// standings go through here, so the same chicken is always the same number.
    /// </summary>
    /// <remarks>
    /// The id is the actor's <c>NetworkId</c> raw value. Fusion never issues raw value 0 to a
    /// live object (<c>NetworkId.IsValid</c> is false for it), so 0 is free to mean "no actor".
    /// The id is unique within a session, not across sessions: progression buckets by it per
    /// round and must never persist it as an identity.
    /// </remarks>
    public static class MatchActorId
    {
        /// <summary>No actor: the object was null, not spawned, or already despawned.</summary>
        public const int None = 0;

        /// <summary>The actor id of <paramref name="networkObject"/>, or <see cref="None"/>.</summary>
        public static int Of(NetworkObject networkObject)
        {
            // Unity's overloaded == so a destroyed object reads as null, never as a stale id.
            if (networkObject == null || !networkObject.IsValid) return None;
            return unchecked((int)networkObject.Id.Raw);
        }

        /// <summary>The actor id of the object <paramref name="behaviour"/> sits on, or <see cref="None"/>.</summary>
        public static int Of(NetworkBehaviour behaviour)
        {
            // Explicit Unity null check, not ?. — the null-conditional operator bypasses
            // UnityEngine.Object's == overload and would dereference a destroyed component.
            if (behaviour == null) return None;
            return Of(behaviour.Object);
        }
    }
}
