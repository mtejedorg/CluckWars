using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Services
{
    /// <summary>
    /// Pure rule for <c>Application.targetFrameRate</c> per platform. No Unity state is read, so EditMode
    /// tests pin it.
    /// </summary>
    /// <remarks>
    /// Android targets a stable 30 fps on 2021+ mid-range phones (CLAUDE.md platform targets): capping
    /// there keeps the frame pacing even and the device cool instead of oscillating 30-60. Every other
    /// platform is left at Unity's default (<see cref="LeaveDefault"/>): the PC quality level paces
    /// itself with <c>vSyncCount = 1</c>, which overrides targetFrameRate anyway, and the Editor must
    /// not be capped (it would throttle Play-mode tooling and tests).
    /// </remarks>
    public static class FrameRatePolicy
    {
        /// <summary>Unity's own default for <c>Application.targetFrameRate</c>: "platform default, don't cap".</summary>
        public const int LeaveDefault = -1;

        /// <summary>The Android target: 30 fps stable.</summary>
        public const int AndroidTarget = 30;

        /// <summary>The target frame rate for <paramref name="platform"/>, or <see cref="LeaveDefault"/>.</summary>
        public static int TargetFor(RuntimePlatform platform) =>
            platform == RuntimePlatform.Android ? AndroidTarget : LeaveDefault;
    }

    /// <summary>
    /// Applies <see cref="FrameRatePolicy"/> once at boot. Bound non-lazy in <c>ProjectInstaller</c>, so it runs
    /// when the ProjectContext first initialises (Bootstrap scene) and never again.
    /// </summary>
    public sealed class FrameRateInitializer : IInitializable
    {
        private readonly ILogService _log;

        public FrameRateInitializer(ILogService log) { _log = log; }

        public void Initialize()
        {
            int target = FrameRatePolicy.TargetFor(Application.platform);
            if (target == FrameRatePolicy.LeaveDefault)
            {
                _log.Verbose("Platform", $"targetFrameRate left at default on {Application.platform} " +
                                         $"(vSyncCount={QualitySettings.vSyncCount}).");
                return;
            }
            Application.targetFrameRate = target;
            _log.Info("Platform", $"Application.targetFrameRate = {target} on {Application.platform} " +
                                  $"(vSyncCount={QualitySettings.vSyncCount}).");
        }
    }
}
