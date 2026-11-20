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
        // 3 was LeaderBounty (removed 2026-11-20, D7b). The values below keep their numbers: the event replicates as a byte.
        Restock = 4
    }
}
