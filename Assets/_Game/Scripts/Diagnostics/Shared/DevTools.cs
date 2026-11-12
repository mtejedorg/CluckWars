namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The one gate for every developer-only tool: Developer Mode and the Ability Lab (menu), and the test-session
    /// instrumentation (PerfProbe, Sync heartbeat, Visual probe, the Fusion net line, auto-test, the touch overlay).
    /// </summary>
    /// <remarks>
    /// True in the Editor and in development builds (<c>Debug.isDebugBuild</c>), or in any build compiled with
    /// <c>CLUCKWARS_DEV_TOOLS</c>. <c>CluckWarsBuildMenu</c> builds with <c>BuildOptions.None</c> (a non-development
    /// player, so the perf numbers stay representative), which makes <c>Debug.isDebugBuild</c> false on every artefact
    /// we install: the define is what turns the tools on in a test-session build ("Cluck Wars/Build/Test Session",
    /// <c>-cwDevTools</c> or <c>CW_DEV_TOOLS=1</c>). A production build (no define) has none of them.
    /// </remarks>
    public static class DevTools
    {
        public static bool Available
        {
            get
            {
#if CLUCKWARS_DEV_TOOLS
                return true;
#else
                return UnityEngine.Debug.isDebugBuild;
#endif
            }
        }

        /// <summary>The define a test-session build is compiled with.</summary>
        public const string Define = "CLUCKWARS_DEV_TOOLS";
    }
}
