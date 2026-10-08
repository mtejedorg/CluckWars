using CluckWars.Localization;
using Fusion;

namespace CluckWars.UI
{
    /// <summary>
    /// What the session-end screen says (re-audit round 2, finding 12): every Fusion
    /// <see cref="ShutdownReason"/> maps to an in-voice line from the wording dictionary, so a raw
    /// enum name ("PhotonCloudTimeout") never reaches the screen. Pure, so an EditMode test walks every
    /// enum value; a value Fusion adds later lands on the generic line, never on its own name.
    /// </summary>
    public static class SessionEndCopy
    {
        /// <summary>The UiKeys line for <paramref name="reason"/>.</summary>
        public static string ReasonKey(ShutdownReason reason) => reason switch
        {
            // The master left and the session went with it.
            ShutdownReason.HostMigration or ShutdownReason.DisconnectedByPluginLogic
                => UiKeys.SessionHostLeft,

            ShutdownReason.PhotonCloudTimeout or ShutdownReason.ConnectionTimeout
                or ShutdownReason.OperationTimeout or ShutdownReason.ConnectionRefused
                => UiKeys.SessionReasonSignal,

            ShutdownReason.Ok or ShutdownReason.GameClosed => UiKeys.SessionReasonClosed,
            ShutdownReason.GameIsFull => UiKeys.SessionReasonFull,
            ShutdownReason.MaxCcuReached => UiKeys.SessionReasonBusy,
            ShutdownReason.GameNotFound => UiKeys.SessionReasonNotFound,

            ShutdownReason.InvalidAuthentication or ShutdownReason.CustomAuthenticationFailed
                or ShutdownReason.AuthenticationTicketExpired
                => UiKeys.SessionReasonAuth,

            ShutdownReason.IncompatibleConfiguration => UiKeys.SessionReasonVersion,

            // Error, InvalidArguments, InvalidRegion, AlreadyRunning, GameIdAlreadyExists, ServerInRoom,
            // OperationCanceled and anything newer: nothing the player can act on beyond trying again.
            _ => UiKeys.SessionReasonGeneric,
        };

        /// <summary>The in-voice line for <paramref name="reason"/>.</summary>
        public static string For(ShutdownReason reason) => UiText.Get(ReasonKey(reason));

        /// <summary>
        /// The headline key: MATCH OVER only when the round this peer watched had really ended (its result
        /// was on screen) before the session closed; COOP CLOSED for every disconnect or shutdown mid-room
        /// or mid-round.
        /// </summary>
        public static string HeadlineKey(bool roundHadEnded) =>
            roundHadEnded ? UiKeys.SessionEnded : UiKeys.SessionClosed;
    }
}
