using UnityEngine;
using UnityEngine.SceneManagement;

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
        [SerializeField] private string _nextSceneName = "Game";
        [SerializeField] private float _delaySeconds = 0f;
        [SerializeField] private bool _autoLoadOnStart = true;

        public void SetAutoLoad(bool autoLoad) => _autoLoadOnStart = autoLoad;

        private void Start()
        {
            if (!_autoLoadOnStart) return;
            if (_delaySeconds > 0f)
                Invoke(nameof(LoadNext), _delaySeconds);
            else
                LoadNext();
        }

        public void LoadNext()
        {
            SceneManager.LoadScene(_nextSceneName, LoadSceneMode.Single);
        }
    }
}
