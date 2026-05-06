using System;
using CluckWars.Gameplay;

namespace CluckWars.Services
{
    /// <summary>
    /// In-memory <see cref="ISessionSelectionService"/>. Lives for the lifetime of the
    /// app (singleton bound under <c>ProjectContext</c>); resets to <see cref="ChickenClass.Warrior"/>
    /// on cold start. No persistence yet — that lands when UGS profiles arrive post-demo.
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

        public event Action<ChickenClass> OnSelectionChanged;
    }
}
