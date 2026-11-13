namespace CluckWars.Audio
{
    /// <summary>
    /// Typed sound ids. A replicated gameplay moment names its sound with one of these, never with an
    /// <c>AudioClip</c>: the clip is resolved by the audio side (<see cref="AudioRegistrySO.Resolve"/>), so a
    /// language or voice pack can swap it without touching gameplay. Add an id here when a new moment needs one.
    /// </summary>
    public enum SfxId : byte
    {
        /// <summary>No sound: playing it is a silent no-op.</summary>
        None = 0,
        AbilityActivate = 1,
        Deposit = 2,
        /// <summary>The player cancelled an aim on purpose.</summary>
        AbilityCancel = 3,
        /// <summary>A release with nobody in range.</summary>
        AbilityFizzle = 4,
        /// <summary>The peck chain stopped on a full cargo.</summary>
        CargoFull = 5,
        /// <summary>A timed ability ran out on its own.</summary>
        AbilityExpire = 6,
        /// <summary>The caster's own cast hit somebody.</summary>
        HitConfirm = 7,
        /// <summary>The caster's own cast hit nobody.</summary>
        CastWhiff = 8,
        /// <summary>A press the ability refused: the HUD's denied click.</summary>
        DeniedClick = 9,
        /// <summary>Egg Shell closing around its owner.</summary>
        ShellClose = 10,
        /// <summary>Egg Shell hatching open again.</summary>
        ShellOpen = 11,
        /// <summary>The Assassin's HUNT! sting. Deliberately optional: while the slot is empty the
        /// hunter hears a pitched-up MarkLock instead, so the moment is never silent.</summary>
        HuntLive = 12,
    }
}
