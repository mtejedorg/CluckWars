namespace CluckWars.Gameplay
{
    /// <summary>
    /// Comeback events that trigger in the final minute of a match.
    /// </summary>
    public enum MatchEventKind : byte
    {
        None = 0,
        GoldenPile = 1,
        UnderdogSurge = 2,
        LeaderBounty = 3,
        Restock = 4
    }
}
