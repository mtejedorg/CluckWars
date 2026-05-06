using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// Inert UGS implementation for demo / offline / dev builds.
    /// Always reports unavailable; never reaches the network.
    /// </summary>
    public sealed class NullUGSService : IUGSService
    {
        public bool IsAvailable => false;
        public bool IsSignedIn => false;

        public Task InitializeAsync() => Task.CompletedTask;
        public Task SignInAnonymouslyAsync() => Task.CompletedTask;
        public Task SignOutAsync() => Task.CompletedTask;
    }
}
