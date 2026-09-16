using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The picking rules behind <c>GameManager</c>'s round-end snapshot: which actor owns each
    /// claimed base, and which actor is the local human. Plain tuples in, so the rules are tested
    /// without a runner; <c>GameManager.CaptureEndSnapshot</c> only gathers the live values.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>One row per claimed base</b>, in input order, whatever the chickens are doing.</item>
    ///   <item><b>Decoys never own a base and are never the local actor.</b> A Doppelganger decoy
    ///   can stand at its caster's corner and shares its caster's input authority (the
    ///   <c>MatchCamera.FindLocalChicken</c> rule), so without <c>!isDecoy</c> either pick could land
    ///   on the decoy — depending only on list order.</item>
    ///   <item><b>An orphaned base keeps its row</b> with <see cref="MatchActorId.None"/> and a null
    ///   role: a player who left mid-round still banked that total, and everyone else's placement
    ///   has to count it. Such a row is never an actor's bucket.</item>
    ///   <item><b>No local actor</b> (the human left, or none exists on this peer) gives
    ///   <see cref="MatchActorId.None"/>.</item>
    /// </list>
    /// </remarks>
    public static class RoundSnapshotSelector
    {
        /// <param name="claimedBases">Every claimed base: its corner and its banked total.</param>
        /// <param name="liveChickens">Every spawned chicken, decoys included: home corner, decoy flag,
        /// whether this peer has input authority over it, its actor id and its role key.</param>
        /// <returns>The rows for <see cref="RoundStandingsBuilder"/> and the local human's actor id.</returns>
        public static (List<(int actorId, string roleKey, float total)> rows, int localActorId) Select(
            IReadOnlyList<(int corner, float total)> claimedBases,
            IReadOnlyList<(int corner, bool isDecoy, bool isLocal, int actorId, string roleKey)> liveChickens)
        {
            if (claimedBases == null) throw new ArgumentNullException(nameof(claimedBases));
            if (liveChickens == null) throw new ArgumentNullException(nameof(liveChickens));

            var rows = new List<(int actorId, string roleKey, float total)>(claimedBases.Count);
            for (int i = 0; i < claimedBases.Count; i++)
            {
                var (corner, total) = claimedBases[i];
                int actorId = MatchActorId.None;
                string roleKey = null;

                for (int j = 0; j < liveChickens.Count; j++)
                {
                    var chicken = liveChickens[j];
                    if (chicken.isDecoy || chicken.corner != corner) continue;
                    actorId = chicken.actorId;
                    roleKey = chicken.roleKey;
                    break;
                }

                rows.Add((actorId, roleKey, total));
            }

            int localActorId = MatchActorId.None;
            for (int j = 0; j < liveChickens.Count; j++)
            {
                var chicken = liveChickens[j];
                if (!chicken.isLocal || chicken.isDecoy) continue;
                localActorId = chicken.actorId;
                break;
            }

            return (rows, localActorId);
        }
    }
}
