using CluckWars.Localization;

namespace CluckWars.UI
{
    /// <summary>How far the player is through typing a join code (THE COOP, join mode).</summary>
    public enum JoinCodeState { Empty, Partial, Complete }

    /// <summary>
    /// The join-code gate of THE COOP (Phase 4 item 9, round-2 finding 10): when JOIN MATCH and the
    /// keyboard's Done / Enter may submit, and what the status pill says meanwhile. Pure, so EditMode
    /// tests cover it.
    /// </summary>
    public static class JoinCodeEntry
    {
        /// <summary>UGS lobby codes are 6 characters; the offline host's session name ("cluck-lan") is longer.</summary>
        public const int MinLength = 6;

        /// <summary>The field's input cap.</summary>
        public const int MaxLength = 16;

        public static JoinCodeState StateOf(string code)
        {
            int n = string.IsNullOrWhiteSpace(code) ? 0 : code.Trim().Length;
            return n == 0 ? JoinCodeState.Empty : n < MinLength ? JoinCodeState.Partial : JoinCodeState.Complete;
        }

        /// <summary>True when <paramref name="code"/> (trimmed) is long enough to be a join code.</summary>
        public static bool IsComplete(string code) => StateOf(code) == JoinCodeState.Complete;

        /// <summary>The status pill's line: "Need a code" / "Keep typing…" / "Ready to join".</summary>
        public static string PillKey(JoinCodeState state) => state switch
        {
            JoinCodeState.Complete => UiKeys.LobbyStatusCodeReady,
            JoinCodeState.Partial  => UiKeys.LobbyStatusKeepTyping,
            _                      => UiKeys.LobbyStatusEnterCode,
        };
    }
}
