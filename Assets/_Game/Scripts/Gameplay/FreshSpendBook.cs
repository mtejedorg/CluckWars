using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Which fresh state of each rival an Enforcer already bit (design 2026-11-25, 16.2, BL-226). The replicated <c>BittenStateId</c> only moves after
    /// <c>RPC_MarkBitten</c> came back, so inside that round trip the same state would read as unspent; the hitter's authority keeps this record and spends
    /// a state once. Pure, authority-local, never replicated.
    /// </summary>
    public sealed class FreshSpendBook<TKey>
    {
        private readonly Dictionary<TKey, byte> _spent = new Dictionary<TKey, byte>();

        /// <summary>Spends state <paramref name="stateId"/> of <paramref name="victim"/>; false when this very state was already spent here.</summary>
        public bool TrySpend(TKey victim, byte stateId)
        {
            if (_spent.TryGetValue(victim, out byte last) && last == stateId) return false;
            _spent[victim] = stateId;
            return true;
        }

        /// <summary>Drops the record of <paramref name="victim"/> (it left, or its debts were cleared).</summary>
        public void Forget(TKey victim) => _spent.Remove(victim);

        public void Clear() => _spent.Clear();
    }
}
