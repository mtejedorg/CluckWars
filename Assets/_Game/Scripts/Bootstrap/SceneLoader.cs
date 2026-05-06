using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Bootstrap
{
    /// <summary>
    /// Lives in <c>Bootstrap.unity</c>. As soon as the scene is up (and Zenject's
    /// <c>ProjectContext</c> has loaded), it transitions to <c>Game.unity</c>.
    /// </summary>
    /// <remarks>
    /// Phase 1 is intentionally dumb: a hard-coded next scene name. In Phase 7 this
    /// becomes Bootstrap → MainMenu → Game.
    /// </remarks>
    public sealed class SceneLoader : MonoBehaviour
    {
        [SerializeField] private string _nextSceneName = "Game";
        [SerializeField] private float _delaySeconds = 0f;

        private void Start()
        {
            if (_delaySeconds > 0f)
                Invoke(nameof(LoadNext), _delaySeconds);
            else
                LoadNext();
        }

        private void LoadNext()
        {
            SceneManager.LoadScene(_nextSceneName, LoadSceneMode.Single);
        }
    }
}
