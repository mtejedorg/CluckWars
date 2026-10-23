namespace CluckWars.Gameplay
{
    /// <summary>
    /// Whether two chickens count as touching for the collision slow (GDD §6.1), with
    /// hysteresis (A11, decisions 2026-10-21): contact begins at <see cref="EnterMeters"/> and
    /// only ends past <see cref="ExitMeters"/>. Pure; <c>ChickenController.CheckCollisionSlow</c>
    /// keeps the state on its own authority, never networked.
    /// </summary>
    /// <remarks>
    /// Without the band, two chickens walking side by side at about 1.2 m crossed the threshold
    /// every few ticks, so the slow (and its replicated <c>ControlVfx.Slowed</c> bit) flickered on
    /// and off: the "contact edge" flicker of CW-226. Cosmetic in effect, which is why the band is
    /// only 0.1 m wide. The Spine Coat steal-back that shares the loop keeps its plain
    /// <see cref="EnterMeters"/> test; only the slow uses the band.
    /// </remarks>
    public static class ContactHysteresis
    {
        /// <summary>Contact begins at or inside this centre-to-centre distance (two chickens touching).</summary>
        public const float EnterMeters = 1.2f;

        /// <summary>Contact, once begun, lasts until the distance exceeds this.</summary>
        public const float ExitMeters = 1.3f;

        /// <summary>
        /// The contact state after this tick, from the last tick's state and the distance to the
        /// nearest other chicken. NaN reads as "no one near" (false), as does +infinity.
        /// </summary>
        public static bool Update(bool inContact, float nearestDistance) =>
            nearestDistance <= (inContact ? ExitMeters : EnterMeters);
    }
}
