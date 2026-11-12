namespace CluckWars.Visuals
{
    /// <summary>
    /// The audience rule for a chicken's OWN one-shot sounds: the cast sound and the deposit sound (A04,
    /// architecture audit 2026-11-12). Pure, tested.
    /// </summary>
    /// <remarks>
    /// <b>Heard only on the peer of the human who drives that chicken.</b> Not for a bot, not for a rival, not for a
    /// Doppelganger decoy (which shares its caster's input authority). These are 2D confirmation sounds for the
    /// player's own action, the same audience as the hit-confirm / whiff one-shots (E1) and
    /// <c>AbilityController.PlayLocalSfx</c>.
    /// <para>
    /// Before A04 both were played by gameplay on whichever peer held the chicken's state authority. For a human's
    /// own chicken that is the same peer, so nothing changes for it. For bots it was the master client (and the
    /// solo player), who heard every bot's cast and deposit while the other clients heard none: with this rule
    /// nobody hears a bot's.
    /// </para>
    /// </remarks>
    public static class OwnSoundRules
    {
        public static bool IsAudible(bool hasInputAuthority, bool isBot, bool isDecoy) =>
            hasInputAuthority && !isBot && !isDecoy;
    }
}
