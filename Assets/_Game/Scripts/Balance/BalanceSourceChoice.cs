namespace CluckWars.Balance
{
    /// <summary>
    /// Which balance source a launch uses, and which Remote Config environment it reads. Pure, decided once by
    /// <c>ProjectInstaller</c>, tested.
    /// </summary>
    public static class BalanceSourceChoice
    {
        public const string EditorPrefKey = "CluckWars.Balance.RemoteConfigInEditor";
        public const string DevEnvironment = "dev";
        public const string ProductionEnvironment = "production";

        /// <summary>
        /// True when the remote source (and the on-disk cache) is used. Always in a player; in the Editor only with the
        /// "Cluck Wars/Balance/Remote Config In Editor" toggle, so day-to-day Play Mode runs the assets as they are on disk.
        /// </summary>
        public static bool Decide(bool isEditor, bool editorToggle) => !isEditor || editorToggle;

        /// <summary>
        /// The Remote Config environment a build reads: "dev" where dev tools exist (Editor, Test Session builds), else
        /// "production". Production builds read production; nothing is compiled out (owner ruling 2026-11-17).
        /// </summary>
        public static string EnvironmentFor(bool devToolsAvailable) => devToolsAvailable ? DevEnvironment : ProductionEnvironment;

        /// <summary>The Editor toggle (false in a player).</summary>
        public static bool EditorToggle
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#else
                return false;
#endif
            }
        }
    }
}
