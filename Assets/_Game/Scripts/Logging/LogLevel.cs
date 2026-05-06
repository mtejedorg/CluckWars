namespace CluckWars.Logging
{
    /// <summary>
    /// Severity levels for the in-game logger. Numeric order is significant —
    /// <see cref="ILogService.MinLevel"/> filters by <c>level &gt;= MinLevel</c>.
    /// </summary>
    public enum LogLevel : byte
    {
        Verbose = 0, // every-frame trace, input, transform deltas
        Debug   = 1, // state transitions, network events, spawn flow
        Info    = 2, // milestone events (match start, scene load)
        Warn    = 3, // recoverable anomalies
        Error   = 4, // unrecoverable; usually paired with an exception
        Off     = 5, // disable all output
    }
}
