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
    }
}
