using System.Threading;
using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// What one balance fetch returned. <see cref="Ok"/> false means the source could not answer (offline, timeout, service
    /// error: <see cref="Error"/> says why) and the caller keeps what it has. <see cref="Ok"/> true with a null or empty
    /// <see cref="Json"/> means "no delta is published": the shipped assets are the balance.
    /// </summary>
    public readonly struct BalanceFetchResult
    {
        public readonly bool Ok;
        public readonly string Json;
        public readonly string Error;

        private BalanceFetchResult(bool ok, string json, string error)
        {
            Ok = ok;
            Json = json;
            Error = error;
        }

        public bool HasDelta => Ok && !string.IsNullOrWhiteSpace(Json);

        public static BalanceFetchResult Delta(string json) => new BalanceFetchResult(true, json, null);
        public static BalanceFetchResult NoDelta() => new BalanceFetchResult(true, null, null);
        public static BalanceFetchResult Failed(string error) => new BalanceFetchResult(false, null, error);
    }

    /// <summary>
    /// Where a server-published balance delta comes from (live-ops config: the owner writes it, clients only read it).
    /// The real source is <c>RemoteConfigBalanceSource</c> in App, the only code that sees the Remote Config package;
    /// <see cref="NullBalanceSource"/> is bound in the Editor (unless its toggle is on) and in <c>UGS_DISABLED</c> builds.
    /// </summary>
    public interface IBalanceSource
    {
        /// <summary>Short name for log lines ("RemoteConfig/production", "null").</summary>
        string Name { get; }

        /// <summary>Fetches the published delta. Should not throw; the caller still catches.</summary>
        Task<BalanceFetchResult> FetchAsync(CancellationToken ct);
    }
}
