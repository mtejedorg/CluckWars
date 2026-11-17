using System.Threading;
using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// The inert balance source: never publishes a delta, so the shipped assets are the balance. Bound in the Editor
    /// (unless "Cluck Wars/Balance/Remote Config In Editor" is on) and in <c>UGS_DISABLED</c> builds.
    /// </summary>
    public sealed class NullBalanceSource : IBalanceSource
    {
        public string Name => "null";

        public Task<BalanceFetchResult> FetchAsync(CancellationToken ct) => Task.FromResult(BalanceFetchResult.NoDelta());
    }
}
