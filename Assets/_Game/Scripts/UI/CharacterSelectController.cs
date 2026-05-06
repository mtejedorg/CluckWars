using CluckWars.Bootstrap;
using CluckWars.Gameplay;
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
    /// Disables a sibling <see cref="SceneLoader"/>'s auto-load on Awake so the
    /// scene waits for player input. The proper UGUI menu lands in Phase 7.
    /// </remarks>
    [RequireComponent(typeof(SceneLoader))]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private ISessionSelectionService _selection;
        private SceneLoader _sceneLoader;

        [Inject]
        public void Construct(ISessionSelectionService selection)
        {
            _selection = selection;
        }

        private void Awake()
        {
            _sceneLoader = GetComponent<SceneLoader>();
            // Suppress SceneLoader.Start() — we'll trigger LoadNext() manually on confirm.
            _sceneLoader.SetAutoLoad(false);
        }

        private void Update()
        {
            if (_selection == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) _selection.SelectedClass = ChickenClass.Warrior;
            else if (kb.digit2Key.wasPressedThisFrame) _selection.SelectedClass = ChickenClass.Speedy;
            else if (kb.digit3Key.wasPressedThisFrame) _selection.SelectedClass = ChickenClass.Fatty;
            else if (kb.digit4Key.wasPressedThisFrame) _selection.SelectedClass = ChickenClass.Assassin;

            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            {
                _sceneLoader.LoadNext();
            }
        }

        private void OnGUI()
        {
            const int pad = 16;
            GUI.Box(new Rect(pad, pad, 360, 130), "Cluck Wars — Pick your chicken");
            GUI.Label(new Rect(pad + 12, pad + 28, 340, 22), "[1] Warrior   [2] Speedy");
            GUI.Label(new Rect(pad + 12, pad + 50, 340, 22), "[3] Fatty     [4] Assassin");

            var selected = _selection != null ? _selection.SelectedClass.ToString() : "—";
            GUI.Label(new Rect(pad + 12, pad + 76, 340, 22), $"Selected: {selected}");
            GUI.Label(new Rect(pad + 12, pad + 100, 340, 22), "Press SPACE / ENTER to start");
        }
    }
}
