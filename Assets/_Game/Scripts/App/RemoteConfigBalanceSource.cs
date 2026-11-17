using System;
using System.Threading;
using System.Threading.Tasks;
using CluckWars.Logging;
using Unity.Services.RemoteConfig;

namespace CluckWars.Services
{
    /// <summary>
    /// The live balance source: UGS Remote Config, key <see cref="Key"/> (a JSON or string setting holding the delta,
    /// <c>BalanceBook</c> remarks). The only code that references the Remote Config package (ARCH-LAY-05/12). It signs in
    /// through <see cref="IUGSService"/>, so there is one auth path for Lobby and Remote Config.
    /// </summary>
    /// <remarks>
    /// <para><b>Environment.</b> Remote Config reads the environment named by <see cref="Environment"/> ("dev" in a
    /// dev-tools build, "production" otherwise, <c>BalanceSourceChoice.EnvironmentFor</c>) by its id, through
    /// <c>SetEnvironmentID</c>; only Remote Config moves, Lobby and Auth stay in the project's default environment so dev
    /// builds and alpha players still meet in one lobby. An empty id means "the environment of the UGS sign-in", i.e. the
    /// project default, and is said once in the log.</para>
    /// <para>Clients only read. Nothing here writes to Remote Config: the owner publishes from the Unity Dashboard.</para>
    /// </remarks>
    public sealed class RemoteConfigBalanceSource : IBalanceSource
    {
        private const string LogSource = "Balance";
        /// <summary>The Remote Config setting that holds the delta.</summary>
        public const string Key = "balance";

        // Remote Config sends these as user / app attributes for targeting rules; this game uses none.
        private struct NoAttributes { }

        private readonly IUGSService _ugs;
        private readonly ILogService _log;
        private readonly string _environmentId;
        private bool _environmentSaid;

        public string Environment { get; }
        public string Name => "RemoteConfig/" + Environment;

        public RemoteConfigBalanceSource(IUGSService ugs, ILogService log, string environment, string environmentId)
        {
            _ugs = ugs;
            _log = log;
            Environment = environment;
            _environmentId = string.IsNullOrWhiteSpace(environmentId) ? null : environmentId.Trim();
        }

        public async Task<BalanceFetchResult> FetchAsync(CancellationToken ct)
        {
            try
            {
                await _ugs.SignInAnonymouslyAsync();
                ct.ThrowIfCancellationRequested();

                if (!_environmentSaid)
                {
                    _environmentSaid = true;
                    if (_environmentId != null)
                    {
                        RemoteConfigService.Instance.SetEnvironmentID(_environmentId);
                        _log.Info(LogSource, $"Remote Config reads the '{Environment}' environment (id {_environmentId}).");
                    }
                    else
                    {
                        _log.Warn(LogSource, $"No Remote Config environment id set for '{Environment}' (ProjectInstaller); " +
                                             "reading the environment of the UGS sign-in (the project default).");
                    }
                }

                var config = await RemoteConfigService.Instance.FetchConfigsAsync(new NoAttributes(), new NoAttributes());
                ct.ThrowIfCancellationRequested();

                // A failed request answers with the SDK's own cache (origin Cached) or nothing (Default): not a fresh
                // answer, so the caller keeps its state instead of mistaking it for "no delta published".
                if (config == null || config.origin != ConfigOrigin.Remote)
                    return BalanceFetchResult.Failed($"Remote Config did not answer (origin {config?.origin.ToString() ?? "none"})");

                if (!config.HasKey(Key)) return BalanceFetchResult.NoDelta();
                string json = config.GetJson(Key, null);
                return string.IsNullOrWhiteSpace(json) ? BalanceFetchResult.NoDelta() : BalanceFetchResult.Delta(json);
            }
            catch (OperationCanceledException)
            {
                return BalanceFetchResult.Failed("cancelled");
            }
            catch (Exception e)
            {
                return BalanceFetchResult.Failed($"{e.GetType().Name}: {e.Message}");
            }
        }
    }
}
