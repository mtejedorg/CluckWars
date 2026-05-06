using CluckWars.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Bootstrap
{
    /// <summary>
    /// Lives in <c>Bootstrap.unity</c>. Default behavior is to auto-load
    /// <c>Game.unity</c> on Start; pre-game UI components (e.g.
    /// <c>CharacterSelectController</c>) call <see cref="SetAutoLoad"/> in Awake
    /// to suppress that and trigger <see cref="LoadNext"/> manually on confirm.
    /// </summary>
    /// <remarks>
    /// Phase 7 will replace this with a proper Bootstrap → MainMenu → Game flow.
    /// </remarks>
    public sealed class SceneLoader : MonoBehaviour
    {
        private const string Source = "SceneLoader";

        [SerializeField] private string _nextSceneName = "Game";
        [SerializeField] private float _delaySeconds = 0f;
        [SerializeField] private bool _autoLoadOnStart = true;

        private ILogService _log;

        public void SetAutoLoad(bool autoLoad) => _autoLoadOnStart = autoLoad;

        private void Awake()
        {
            // Self-inject so we work whether or not the host scene has a SceneContext.
            // Accessing .Instance triggers the lazy load if no other component has yet.
            if (_log == null)
                ProjectContext.Instance.Container.Inject(this);
        }

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Start()
        {
            if (!_autoLoadOnStart)
            {
                _log?.Debug(Source, $"Auto-load suppressed; awaiting external LoadNext() (next scene = '{_nextSceneName}').");
                return;
            }
            if (_delaySeconds > 0f)
            {
                _log?.Debug(Source, $"Auto-load scheduled in {_delaySeconds:0.00}s → '{_nextSceneName}'.");
                Invoke(nameof(LoadNext), _delaySeconds);
            }
            else
            {
                LoadNext();
            }
        }

        public void LoadNext()
        {
            _log?.Info(Source, $"Loading scene '{_nextSceneName}' (Single mode).");
            SceneManager.LoadScene(_nextSceneName, LoadSceneMode.Single);
        }
    }
}
