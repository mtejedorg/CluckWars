using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// What a bot knows about one rival: where it believes the rival is, and whether it still believes in it.
    /// Built only by <see cref="BotSightRules.Observe"/>.
    /// </summary>
    public readonly struct RivalSighting
    {
        /// <summary>Where the bot acts as if the rival stands: its live position while visible, the last position seen
        /// before it hid while hidden. Meaningless when <see cref="GivenUp"/> and the rival was never seen.</summary>
        public readonly Vector3 Position;

        /// <summary>The rival is faded (Hide, Smoke Roost's fade) on this look.</summary>
        public readonly bool Hidden;

        /// <summary>The bot searched the remembered spot (or ran out of patience) and the rival has not shown itself:
        /// the bot drops it until it is visible again. Never true for a visible rival.</summary>
        public readonly bool GivenUp;

        /// <summary>Simulation time at which the bot first noticed the rival hidden; only read while <see cref="Hidden"/>.</summary>
        public readonly float HiddenSince;

        public RivalSighting(Vector3 position, bool hidden, bool givenUp, float hiddenSince)
        {
            Position = position;
            Hidden = hidden;
            GivenUp = givenUp;
            HiddenSince = hiddenSince;
        }
    }

    /// <summary>
    /// The one rule for what a bot may know of a rival's position. Pure and Fusion-free, so EditMode tests and the
    /// Balance simulator run the same rule: <c>BotController</c> only feeds it and reads it.
    /// </summary>
    /// <remarks>
    /// Hide (and Smoke Roost's fade) only changes the replicated <c>VisualOpacity</c>; this rule is what makes bots
    /// respect it (Maestro, 2026-11-22). A rival that is visible is read live. A rival that hides is remembered where
    /// it was last seen, and the bot acts as if it still stands there. Using a move ends Hide, so the rival becomes
    /// visible again at once and the memory updates on the next look. A bot that reaches the remembered spot, or has
    /// searched for <see cref="GiveUpSeconds"/>, drops the rival until it shows itself.
    /// </remarks>
    public static class BotSightRules
    {
        /// <summary>Opacity below which a bird counts as faded for every observer (the single home of the number;
        /// <c>ControlStateVFX.FadedOpacityThreshold</c> reads it).</summary>
        public const float HiddenOpacityThreshold = 0.99f;

        /// <summary>Longest a bot keeps acting on a remembered spot before it drops the rival (seconds): about the
        /// time to cross a third of the arena, so a bot that cannot reach the spot (a wall, a stalk standoff) still lets go.</summary>
        public const float GiveUpSeconds = 4f;

        /// <summary>Is a bird with this replicated opacity hidden from its rivals? 0 is the [Networked] "unset" default,
        /// not invisible.</summary>
        public static bool IsHidden(float visualOpacity) => visualOpacity > 0f && visualOpacity < HiddenOpacityThreshold;

        /// <summary>
        /// One look at a rival: the bot's updated belief.
        /// </summary>
        /// <param name="previous">The belief from the last look, or null when the bot has never looked at this rival.</param>
        /// <param name="actualPosition">The rival's real position. Read only while it is visible (a hidden rival keeps the remembered one).</param>
        /// <param name="visualOpacity">The rival's replicated opacity.</param>
        /// <param name="selfPosition">The bot's own position, for the arrival test.</param>
        /// <param name="now">Simulation time, seconds.</param>
        /// <param name="arrivalRadius">Planar distance to the remembered spot at which the bot counts as arrived.</param>
        public static RivalSighting Observe(RivalSighting? previous, Vector3 actualPosition, float visualOpacity,
            Vector3 selfPosition, float now, float arrivalRadius)
        {
            if (!IsHidden(visualOpacity))
                return new RivalSighting(actualPosition, false, false, 0f);

            // Hidden before the bot ever saw it: there is nothing to remember, so there is nothing to act on.
            if (!previous.HasValue)
                return new RivalSighting(default, true, true, now);

            var p = previous.Value;
            // It faded since the last look: freeze the belief where it was and start the clock.
            float since = p.Hidden ? p.HiddenSince : now;
            bool givenUp = p.Hidden && p.GivenUp;
            if (!givenUp)
            {
                float dx = p.Position.x - selfPosition.x;
                float dz = p.Position.z - selfPosition.z;
                givenUp = dx * dx + dz * dz <= arrivalRadius * arrivalRadius || now - since >= GiveUpSeconds;
            }
            return new RivalSighting(p.Position, true, givenUp, since);
        }
    }
}
