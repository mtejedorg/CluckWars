using CluckWars.Audio;
using CluckWars.Input;
using CluckWars.Services;
using Zenject;

namespace CluckWars.Installers
{
    /// <summary>
    /// App-wide bindings. Lives as a component on
    /// <c>Assets/_Game/Resources/ProjectContext.prefab</c> — Zenject auto-loads that
    /// prefab the first time any container is resolved. Anything bound here survives
    /// scene loads and is shared by every <c>SceneContext</c>.
    /// </summary>
    public sealed class ProjectInstaller : MonoInstaller<ProjectInstaller>
    {
        public override void InstallBindings()
        {
            Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();
            Container.Bind<IAudioService>().To<NullAudioService>().AsSingle();
            Container.Bind<IInputProvider>().To<KeyboardInputProvider>().AsSingle();
        }
    }
}
