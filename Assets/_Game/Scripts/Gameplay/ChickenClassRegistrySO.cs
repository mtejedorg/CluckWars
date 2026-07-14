using System;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Maps a <see cref="ChickenClass"/> to its tunables and presentation metadata.
    /// One asset for the whole game lives at <c>Assets/_Game/Data/ChickenClassRegistry.asset</c>.
    /// </summary>
    /// <remarks>
    /// Phase 2 is intentionally minimal: stats + tint color. As art lands we'll add
    /// <c>Mesh</c>, <c>RuntimeAnimatorController</c>, and ability load-outs per entry.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ChickenClassRegistry",
        menuName = "Cluck Wars/Chicken Class Registry",
        order = 2)]
    public sealed class ChickenClassRegistrySO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ChickenClass Class;
            public ChickenStatsSO Stats;
            [ColorUsage(showAlpha: false, hdr: false)]
            public Color TintColor;
            public string LoreQuote;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>Look up an entry by class. Returns false if missing.</summary>
        public bool TryGet(ChickenClass cls, out Entry entry)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Class == cls)
                {
                    entry = _entries[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        /// <summary>Defaults to Warrior if the requested class is missing.</summary>
        public Entry GetOrDefault(ChickenClass cls)
        {
            return TryGet(cls, out var entry) ? entry
                : TryGet(ChickenClass.Warrior, out var fallback) ? fallback
                : default;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Catch the obvious authoring slip: two entries for the same class.
            if (_entries == null) return;
            for (int i = 0; i < _entries.Length; i++)
            for (int j = i + 1; j < _entries.Length; j++)
            {
                if (_entries[i].Class == _entries[j].Class)
                {
                    Debug.LogWarning(
                        $"[ChickenClassRegistry] Duplicate entry for {_entries[i].Class} at indices {i} and {j}.",
                        this);
                }
            }
        }
#endif
    }
}
