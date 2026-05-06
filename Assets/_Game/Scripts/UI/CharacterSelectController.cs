using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// Phase-2 placeholder character select. Lives in <c>Bootstrap.unity</c>.
    /// Number keys 1-4 pick a class, Space/Enter loads <c>Game.unity</c>. An
    /// <see cref="OnGUI"/> overlay shows the current selection.
    /// </summary>
    /// <remarks>
    /// Self-injects from <c>ProjectContext</c> in Awake so it works without a
    /// <c>SceneContext</c> in the Bootstrap scene. Disables a sibling
    /// <see cref="SceneLoader"/>'s auto-load so the scene waits for player input.
    /// The proper UGUI menu lands in Phase 7.
    /// </remarks>
    [RequireComponent(typeof(SceneLoader))]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private const string Source = "CharacterSelect";

        private ISessionSelectionService _selection;
        private ILogService _log;
        private SceneLoader _sceneLoader;

        [Inject]
        public void Construct(ISessionSelectionService selection, ILogService log)
        {
            _selection = selection;
            _log = log;
        }

        private void Awake()
        {
            _sceneLoader = GetComponent<SceneLoader>();
            _sceneLoader.SetAutoLoad(false);

            // Bootstrap scene has no SceneContext, so [Inject] won't fire automatically.
            // Self-inject from ProjectContext — accessing .Instance triggers its lazy
            // instantiation, so this works on cold start before any other scene component
            // has touched the container.
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            if (_log != null)
            {
                _log.Info(Source,
                    _selection != null
                        ? $"Awake. Initial selection = {_selection.SelectedClass}."
                        : "Awake but ISessionSelectionService injection FAILED. Keys will be ignored.");
            }
            else
            {
                Debug.LogError(
                    "[CharacterSelect] ILogService injection failed — ProjectContext likely not loaded. " +
                    "Check that Assets/_Game/Resources/ProjectContext.prefab has ProjectInstaller in its Mono Installers list.");
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                if (_log != null && _log.IsEnabled(LogLevel.Verbose))
                    _log.Verbose(Source, "Keyboard.current is null — Input System has no keyboard device.");
                return;
            }

            if (_selection == null) return;

            // Accept both top-row digit keys and numpad — Spanish keyboards in particular
            // report shifted top-row digits oddly, and full-size keyboards default to numpad.
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Select(ChickenClass.Warrior, "1");
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Select(ChickenClass.Speedy, "2");
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Select(ChickenClass.Fatty, "3");
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) Select(ChickenClass.Assassin, "4");

            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            {
                _log?.Info(Source, $"Confirm pressed. Loading next scene with selection = {_selection.SelectedClass}.");
                _sceneLoader.LoadNext();
            }
        }

        private void Select(ChickenClass cls, string keyName)
        {
            _selection.SelectedClass = cls;
            _log?.Debug(Source, $"Key '{keyName}' pressed → SelectedClass = {cls}.");
        }

        private void OnGUI()
        {
            const int pad = 16;
            GUI.Box(new Rect(pad, pad, 360, 130), "Cluck Wars — Pick your chicken");
            GUI.Label(new Rect(pad + 12, pad + 28, 340, 22), "[1] Warrior   [2] Speedy");
            GUI.Label(new Rect(pad + 12, pad + 50, 340, 22), "[3] Fatty     [4] Assassin");

            var selected = _selection != null ? _selection.SelectedClass.ToString() : "(injection failed — see console)";
            GUI.Label(new Rect(pad + 12, pad + 76, 340, 22), $"Selected: {selected}");
            GUI.Label(new Rect(pad + 12, pad + 100, 340, 22), "Press SPACE / ENTER to start");
        }
    }
}
