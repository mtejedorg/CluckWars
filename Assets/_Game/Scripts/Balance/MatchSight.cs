using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// What a simulated policy may know of a rival's position: the game's own bot sight rule (<see cref="BotSightRules"/>), called from
    /// here and nowhere else in the simulator. The rule works in 3D world space; the simulator is planar, so a point (x, y) goes in as
    /// (x, 0, y) and comes back the same way.
    /// </summary>
    public static class MatchSight
    {
        /// <summary>One look of <paramref name="self"/> at a rival. See <see cref="BotSightRules.Observe"/> for the rule.</summary>
        public static RivalSighting Observe(RivalSighting? previous, Vector2 truePos, float opacity, Vector2 self, float now, float arrivalRadius) =>
            BotSightRules.Observe(previous, new Vector3(truePos.x, 0f, truePos.y), opacity, new Vector3(self.x, 0f, self.y), now, arrivalRadius);

        /// <summary>The planar position a sighting holds.</summary>
        public static Vector2 PlanarPosition(in RivalSighting s) => new Vector2(s.Position.x, s.Position.z);
    }
}
