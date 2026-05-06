using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// Unity Gaming Services facade. All UGS calls (Auth, Lobby, Relay) go through here.
    /// Demo build binds <see cref="NullUGSService"/> via the UGS_DISABLED scripting define.
    /// </summary>
    public interface IUGSService
    {
        bool IsAvailable { get; }
        bool IsSignedIn { get; }

        Task InitializeAsync();
        Task SignInAnonymouslyAsync();
        Task SignOutAsync();
    }
}
