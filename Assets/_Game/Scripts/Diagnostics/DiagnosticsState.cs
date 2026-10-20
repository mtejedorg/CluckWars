using System;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// What the dev-tools probes last measured, for the on-device overlay (<c>DebugHud</c>) to print. Written by the
    /// probes, read by the overlay; plain static fields so neither side allocates or needs the other injected.
    /// Diagnostic only: nothing in the game reads these.
    /// </summary>
    public static class DiagnosticsReadout
    {
        /// <summary>p95 frame time of the last 5 s PerfProbe window (ms); negative until the first summary.</summary>
        public static float PerfP95Ms = -1f;
        /// <summary>Frames over 50 ms since boot (PerfProbe).</summary>
        public static int PerfHitchCount;
        /// <summary>Offenders found by the last Visual scan; negative until the first scan.</summary>
        public static int VisualOffenders = -1;
        /// <summary>UTC <c>HH:mm:ss</c> of the last Visual scan.</summary>
        public static string LastVisualScanUtc = "never";
        /// <summary>Set by the overlay's "Scan visuals" button; consumed by <c>VisualProbe</c>.</summary>
        public static bool VisualScanRequested;
        /// <summary>Local player's round-trip time to the session (ms); negative when there is no runner.</summary>
        public static float NetRttMs = -1f;
        /// <summary>Packet loss as printed in the <c>Net</c> line (Fusion 2 does not expose it: <c>n/a</c>).</summary>
        public static string NetLoss = "n/a";
        /// <summary>Last <c>Time.frameCount</c> on which a probe did scan work (PerfProbe tags a hitch on it <c>probe=1</c>).</summary>
        public static int ProbeWorkFrame = -1;
        /// <summary>Auto-test <c>cw_hud 1</c>: show the overlay as soon as it exists.</summary>
        public static bool HudRequestedAtBoot;
    }

    /// <summary>
    /// Whether the heavy probes (automatic Visual scans, Sync heartbeat lines) run. Set once at boot by
    /// <c>ProjectInstaller</c>; true by default (players).
    /// </summary>
    public static class HeavyProbes
    {
        public static bool Enabled = true;
    }

    /// <summary>
    /// The arena cover / reveal state of the current Game scene, for breadcrumbs and probes that are not the overlay
    /// (the Fusion breadcrumbs, PerfProbe's phase, VisualProbe's reveal trigger). Written only by
    /// <c>MatchOverlaysController</c> (cover) and <c>SceneLoader</c> / <see cref="RevealBreadcrumbs"/> (scene loads);
    /// presentation-only, nothing networked reads it.
    /// </summary>
    public static class RevealState
    {
        /// <summary>The opaque arena cover is on screen this frame.</summary>
        public static bool CoverShown;
        /// <summary>The arena has been revealed in this scene load.</summary>
        public static bool Revealed;
        /// <summary>An async scene load was started and has not finished.</summary>
        public static bool SceneLoadInFlight;
        /// <summary><c>Time.realtimeSinceStartup</c> of the last completed scene load; negative before the first.</summary>
        public static float LastSceneLoadedRealtime = -1f;

        /// <summary>Fires once per scene load when the cover starts to fade (the reveal).</summary>
        public static event Action ArenaRevealed;

        /// <summary><c>Time.realtimeSinceStartup</c> at which the cover's fade-out ends; negative before a reveal.</summary>
        public static float FadeEndsRealtime = -1f;

        /// <param name="fadeSeconds">The cover's fade-out (0 under Reduced Motion).</param>
        public static void NotifyRevealed(float fadeSeconds)
        {
            Revealed = true;
            CoverShown = false;
            FadeEndsRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, fadeSeconds);
            ArenaRevealed?.Invoke();
        }

        /// <summary>A new Game scene (or leaving one): no cover, nothing revealed yet.</summary>
        public static void ResetForScene()
        {
            CoverShown = false;
            Revealed = false;
            FadeEndsRealtime = -1f;
        }

        /// <summary>
        /// A frame that STARTED (<paramref name="frameStartRealtime"/>) before the cover's fade-out ended belongs to the
        /// reveal, not to the intro / active round: its hitch is the reveal's.
        /// </summary>
        public static bool InRevealFade(float frameStartRealtime) =>
            FadeEndsRealtime >= 0f && frameStartRealtime < FadeEndsRealtime;

        /// <summary><c>cover=0|1 revealed=0|1</c>, for breadcrumb lines.</summary>
        public static string Describe() => "cover=" + (CoverShown ? "1" : "0") + " revealed=" + (Revealed ? "1" : "0");

        /// <summary>Seconds after a completed scene load that still count as "loading" (its first frames hitch).</summary>
        public const float PostLoadGraceSeconds = 1f;

        public static bool IsLoading(float realtimeNow) =>
            SceneLoadInFlight || (LastSceneLoadedRealtime >= 0f && realtimeNow - LastSceneLoadedRealtime < PostLoadGraceSeconds);
    }

    /// <summary>First local use of a costly visual, logged once per scene load as <c>Reveal first-use &lt;name&gt;</c>.</summary>
    public enum FirstUseKind : byte { HitSpark = 0, Invisibility = 1, FenceHole = 2, Telegraph = 3 }

    /// <summary>
    /// Marks the first time each costly visual is instantiated / enabled locally, so a frame hitch can be attributed to a
    /// first use (shader variant upload, material swap). <see cref="Note"/> is a bit test on the hot path (no allocation,
    /// no logging); <see cref="RevealBreadcrumbs"/> drains the marks and logs them with the frame they happened on.
    /// The arena warm-up's forced variants are not first uses and are not noted.
    /// </summary>
    public static class FirstUseMarkers
    {
        public const int KindCount = 4;
        private static int _seen, _pending;
        private static readonly int[] Frames = new int[KindCount];

        public static void Note(FirstUseKind kind)
        {
            int bit = 1 << (int)kind;
            if ((_seen & bit) != 0) return;
            _seen |= bit;
            _pending |= bit;
            Frames[(int)kind] = Time.frameCount;
        }

        /// <summary>Takes the next pending mark, oldest kind first.</summary>
        public static bool TryTake(out FirstUseKind kind, out int frame)
        {
            for (int i = 0; i < KindCount; i++)
            {
                int bit = 1 << i;
                if ((_pending & bit) == 0) continue;
                _pending &= ~bit;
                kind = (FirstUseKind)i;
                frame = Frames[i];
                return true;
            }
            kind = default;
            frame = 0;
            return false;
        }

        public static void ResetForScene() { _seen = 0; _pending = 0; }

        public static string Name(FirstUseKind kind) => kind switch
        {
            FirstUseKind.HitSpark     => "hitspark",
            FirstUseKind.Invisibility => "invisibility",
            FirstUseKind.FenceHole    => "fencehole",
            FirstUseKind.Telegraph    => "telegraph",
            _                         => kind.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>Keeps a probe alive across scene loads (Zenject parents it under the ProjectContext; this is the fallback).</summary>
    internal static class DiagnosticsObjects
    {
        public static void KeepAcrossScenes(GameObject go)
        {
            if (go != null && go.transform.parent == null) UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }
}
