namespace CluckWars.Abilities
{
    /// <summary>Which palette accent an ability wears (spec D3a). <see cref="Custom"/> = the ability's own
    /// serialized literal (retired assets keep theirs); every other key is resolved by presentation
    /// (<c>FeedbackPaletteSO.Accent</c>, <c>AbilityAccent.AccentColor</c>).</summary>
    /// <remarks>An ability names its accent; it does not know the colour. The key is authored on
    /// <see cref="AbilityBaseSO"/>, so it lives with the abilities (it moved here from <c>CluckWars.Visuals</c> in
    /// audit batch 4). <b>The numeric values are serialized in every ability <c>.asset</c>: never reorder, insert
    /// or renumber; append only.</b> <c>FeedbackPaletteTests</c> pins them.</remarks>
    public enum AccentKey
    {
        Custom = 0,
        Steal,
        Stun,
        Shove,
        Slow,
        Speed,
        Jump,
        EggShell,
        Hide,
        Peck,
        Hunt,
    }
}
