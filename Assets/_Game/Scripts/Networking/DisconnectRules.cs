using CluckWars.Services;
using Fusion;
using Fusion.Sockets;

namespace CluckWars.Networking
{
    /// <summary>How this peer's session ended, as far as what the player is offered next (CW-218).</summary>
    public enum SessionExitKind
    {
        /// <summary>The player left on purpose (BACK TO LOBBY, LEAVE, CHANGE BIRD).</summary>
        Voluntary = 0,
        /// <summary>This peer lost its connection (app backgrounded past the timeout, network drop). The match may still be running.</summary>
        ConnectionLost = 1,
        /// <summary>The session went with a departing host (Fusion <c>HostMigration</c>; not used by Shared Mode).</summary>
        HostLeft = 2,
        /// <summary>The session was closed normally (game closed, runner shut down without an error).</summary>
        SessionClosed = 3,
        /// <summary>Anything else (full, not found, auth, version, internal error): nothing to rejoin.</summary>
        Other = 4,
    }

    /// <summary>
    /// CW-218: what an involuntary disconnect leads to. Maestro's decision (2026-10-21): a "You were disconnected" notice
    /// on the menu with a REJOIN button that runs the ordinary join-by-code flow. No Fusion session resume and no state
    /// restore: a rejoin is a fresh join under the existing late-join rules. Pure, so EditMode tests pin every case.
    /// </summary>
    public static class DisconnectRules
    {
        /// <summary>
        /// Classifies a runner shutdown. The transport's disconnect reason, when one arrived first, wins over the
        /// shutdown reason: a backgrounded phone logs <c>OnDisconnectedFromServer reason=Timeout</c> and then
        /// <c>OnShutdown DisconnectedByPluginLogic</c> (CW-218, session-213904), and that peer lost its own connection;
        /// the host did not leave. <c>DisconnectedByPluginLogic</c> alone (a server kick, no transport reason: the PC
        /// freeze) is a lost connection too. <c>Requested</c> (we asked) and <c>ByRemote</c> (the server closed us) carry no
        /// cause of their own, so the shutdown reason decides those.
        /// </summary>
        public static SessionExitKind Classify(ShutdownReason reason, NetDisconnectReason? transportReason, bool leftOnPurpose)
        {
            if (leftOnPurpose) return SessionExitKind.Voluntary;

            switch (transportReason)
            {
                case NetDisconnectReason.Timeout:
                case NetDisconnectReason.SendWindowFull:
                case NetDisconnectReason.SequenceOutOfBounds:
                // Unknown is kept as a lost connection on purpose (an unrequested drop; REJOIN is the useful answer). It can
                // also be how a session that really ended reaches us. That case is caught on the menu, not here:
                // CheckLobbyAsync answering Gone hides REJOIN and the "Lost the signal" line and shows "The match is over or
                // full."; an Unknown answer keeps REJOIN, and a failed join shows the same line.
                case NetDisconnectReason.Unknown:
                    return SessionExitKind.ConnectionLost;
            }

            // Full mapping (Fusion 2 ShutdownReason):
            //   ConnectionLost: PhotonCloudTimeout, ConnectionTimeout, OperationTimeout, DisconnectedByPluginLogic
            //   HostLeft:       HostMigration
            //   SessionClosed:  Ok, GameClosed
            //   Other:          GameIsFull, GameNotFound, MaxCcuReached, ConnectionRefused, InvalidAuthentication,
            //                   CustomAuthenticationFailed, AuthenticationTicketExpired, IncompatibleConfiguration, Error,
            //                   InvalidArguments, InvalidRegion, AlreadyRunning, GameIdAlreadyExists, ServerInRoom,
            //                   OperationCanceled (and anything newer)
            // DisconnectedByPluginLogic is the server kicking THIS peer. In Shared Mode a departing master does not shut
            // the others down (they carry on after master migration), so a kick we did not ask for is this peer being
            // dropped: first PC -cw_autodisconnect run (session-20261021-205133) got "Kick Player ... ServerLogic" then
            // OnShutdown DisconnectedByPluginLogic with NO transport reason first. Solo never sees it; ShowsMenuNotice
            // excludes solo anyway. If the match had in fact ended, the menu's lobby check / failed join says
            // "The match is over or full."
            return reason switch
            {
                ShutdownReason.PhotonCloudTimeout or ShutdownReason.ConnectionTimeout or ShutdownReason.OperationTimeout
                    or ShutdownReason.DisconnectedByPluginLogic
                    => SessionExitKind.ConnectionLost,
                ShutdownReason.HostMigration
                    => SessionExitKind.HostLeft,
                ShutdownReason.Ok or ShutdownReason.GameClosed
                    => SessionExitKind.SessionClosed,
                _ => SessionExitKind.Other,
            };
        }

        /// <summary>
        /// The in-match session-end screen hands over to the menu's disconnect notice (and skips its countdown) only for
        /// a lost connection in a multiplayer session. Solo has no session to go back to; host-left and closed sessions
        /// keep the in-match session-end screen.
        /// </summary>
        public static bool ShowsMenuNotice(SessionExitKind kind, SessionMode mode) =>
            kind == SessionExitKind.ConnectionLost && mode != SessionMode.Solo;

        /// <summary>REJOIN is offered when there is a session to go back to: not solo, and a remembered join code.</summary>
        public static bool OffersRejoin(SessionMode mode, string lastJoinCode) =>
            mode != SessionMode.Solo && !string.IsNullOrWhiteSpace(lastJoinCode);

        /// <summary>
        /// Whether the REJOIN button stays up once the lobby check answers. Only a lobby known to be gone hides it; an
        /// unknown answer (offline service, failed check) keeps it, and the join attempt itself reports a failure.
        /// </summary>
        public static bool ShowsRejoin(bool offered, LobbyAvailability availability) =>
            offered && availability != LobbyAvailability.Gone;
    }
}
