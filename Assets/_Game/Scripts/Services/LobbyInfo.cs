namespace CluckWars.Services
{
    /// <summary>
    /// Snapshot of a UGS Lobby returned by <see cref="IUGSService"/> calls.
    /// Immutable; re-query to get an updated player count.
    /// </summary>
    public sealed class LobbyInfo
    {
        public string LobbyId     { get; }
        public string LobbyName   { get; }
        /// <summary>Short join code (e.g. "ABC123"). Also used as the Fusion session name.</summary>
        public string JoinCode    { get; }
        public int    PlayerCount { get; }
        public int    MaxPlayers  { get; }

        public LobbyInfo(string lobbyId, string lobbyName, string joinCode, int playerCount, int maxPlayers)
        {
            LobbyId     = lobbyId;
            LobbyName   = lobbyName;
            JoinCode    = joinCode;
            PlayerCount = playerCount;
            MaxPlayers  = maxPlayers;
        }

        public override string ToString() =>
            $"{LobbyName} [{JoinCode}] {PlayerCount}/{MaxPlayers}";
    }
}
