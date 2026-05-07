using System;
using CluckWars.Gameplay;

namespace CluckWars.Services
{
    /// <summary>
    /// In-memory <see cref="ISessionSelectionService"/>. Lives for the lifetime of the
    /// app (singleton bound under <c>ProjectContext</c>); resets to defaults on cold
    /// start. No persistence yet — that lands when UGS profiles arrive post-demo.
    /// </summary>
    public sealed class SessionSelectionService : ISessionSelectionService
    {
        private ChickenClass _selectedClass = ChickenClass.Warrior;

        public ChickenClass SelectedClass
        {
            get => _selectedClass;
            set
            {
                if (_selectedClass == value) return;
                _selectedClass = value;
                OnSelectionChanged?.Invoke(value);
            }
        }

        public SessionMode Mode { get; set; } = SessionMode.Solo;

        // Single hardcoded session name for the LAN demo: any peer that picks Host
        // creates "cluck-lan", any peer that picks Join joins it. Phase 7 will replace
        // this with proper UGUI session entry.
        public string SessionName { get; set; } = "cluck-lan";

        public event Action<ChickenClass> OnSelectionChanged;
    }
}
