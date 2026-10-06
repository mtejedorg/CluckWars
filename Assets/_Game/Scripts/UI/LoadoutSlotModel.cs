using System;
using System.Collections.Generic;

namespace CluckWars.UI
{
    /// <summary>What one tap on a deck card did to the loadout (<see cref="LoadoutSlotModel{T}.TapCard"/>).</summary>
    public enum CardTapResult
    {
        /// <summary>Nothing changed: the card is a locked starter, or the item is null.</summary>
        Ignored,
        /// <summary>The card was already in the armed slot; that slot is now empty (and stays armed).</summary>
        Cleared,
        /// <summary>The card sat in another slot; it and the armed slot's occupant traded places.</summary>
        Swapped,
        /// <summary>The card was not equipped; it went into the armed slot and arming advanced.</summary>
        Placed,
    }

    /// <summary>
    /// The GEAR UP slot-arming state machine, free of any VisualElement so it can be unit tested
    /// (<c>LoadoutSlotModelTests</c>). The menu copies the four selection slots and their lock
    /// flags in with <see cref="Load"/>, applies one tap, and writes <see cref="Get"/> back.
    /// </summary>
    /// <remarks>
    /// <see cref="Armed"/> is the slot the next card tap acts on. Tapping a slot only re-arms it;
    /// tapping a card resolves against the armed slot. Nothing ever compacts: a slot that goes
    /// empty stays empty until the player fills it. Locked slots (starters) are never armed,
    /// cleared or swapped.
    /// </remarks>
    public sealed class LoadoutSlotModel<T> where T : class
    {
        private readonly T[] _slots;
        private readonly bool[] _locked;

        public LoadoutSlotModel(int slotCount)
        {
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount), "A loadout needs at least one slot.");
            _slots = new T[slotCount];
            _locked = new bool[slotCount];
        }

        public int Count => _slots.Length;

        /// <summary>The slot the next card tap acts on. Always a valid index.</summary>
        public int Armed { get; private set; }

        /// <summary>Slot that shows the NEXT caret: the armed slot, or -1 if it is locked
        /// (only possible when every slot is locked).</summary>
        public int NextCaretSlot => _locked[Armed] ? -1 : Armed;

        public T Get(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        public bool IsLocked(int slot) => slot >= 0 && slot < _locked.Length && _locked[slot];

        /// <summary>Number of empty slots.</summary>
        public int Missing
        {
            get { int n = 0; foreach (var s in _slots) if (s == null) n++; return n; }
        }

        /// <summary>The slot holding <paramref name="item"/>, or -1.</summary>
        public int SlotOf(T item)
        {
            if (item == null) return -1;
            for (int i = 0; i < _slots.Length; i++) if (ReferenceEquals(_slots[i], item)) return i;
            return -1;
        }

        public bool IsPicked(T item) => SlotOf(item) >= 0;

        /// <summary>
        /// Replaces the slot contents and lock flags. Keeps the current arming when it still
        /// points at an unlocked slot, otherwise re-arms (<see cref="AutoArmFirstEmpty"/>).
        /// </summary>
        public void Load(IReadOnlyList<T> slots, IReadOnlyList<bool> locked)
        {
            if (slots == null || slots.Count != _slots.Length)
                throw new ArgumentException($"Expected {_slots.Length} slots.", nameof(slots));
            if (locked == null || locked.Count != _locked.Length)
                throw new ArgumentException($"Expected {_locked.Length} lock flags.", nameof(locked));
            for (int i = 0; i < _slots.Length; i++) { _slots[i] = slots[i]; _locked[i] = locked[i]; }
            if (_locked[Armed]) AutoArmFirstEmpty();
        }

        /// <summary>
        /// Arms the lowest empty unlocked slot; when every slot is full, the first unlocked slot
        /// (slot 0 if all are locked).
        /// </summary>
        public void AutoArmFirstEmpty()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] == null && !_locked[i]) { Armed = i; return; }
            Armed = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (!_locked[i]) { Armed = i; return; }
        }

        /// <summary>Re-arms <paramref name="slot"/>. False (no change) for a locked or invalid slot.</summary>
        public bool ArmSlot(int slot)
        {
            if (slot < 0 || slot >= _slots.Length || _locked[slot]) return false;
            Armed = slot;
            return true;
        }

        /// <summary>
        /// One card tap: clear it from the armed slot, swap it in from another slot, or place it
        /// in the armed slot and advance. <paramref name="itemIsLocked"/> = the item is a locked
        /// starter (its card shows details only).
        /// </summary>
        public CardTapResult TapCard(T item, bool itemIsLocked)
        {
            if (item == null || itemIsLocked || _locked[Armed]) return CardTapResult.Ignored;

            int existing = SlotOf(item);
            if (existing == Armed)
            {
                _slots[Armed] = null;
                return CardTapResult.Cleared;
            }
            if (existing >= 0)
            {
                if (_locked[existing]) return CardTapResult.Ignored;
                _slots[existing] = _slots[Armed];
                _slots[Armed] = item;
                return CardTapResult.Swapped;
            }
            _slots[Armed] = item;
            AutoArmFirstEmpty();
            return CardTapResult.Placed;
        }
    }
}
