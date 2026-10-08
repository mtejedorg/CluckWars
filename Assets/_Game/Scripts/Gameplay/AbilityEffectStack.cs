using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Phase 6 chunk 3: THE place same-kind ability effects are resolved. Abilities may now run
    /// concurrently, but effects never stack: for every effect kind only the strongest currently
    /// running value applies, and each source (an ability slot) adds its own modifier on activate and
    /// removes exactly that modifier on deactivate. Nothing ever writes a hard default.
    /// </summary>
    /// <remarks>
    /// Pure and allocation-free after warm-up, so every rule is pinned by EditMode tests. Owned by
    /// <see cref="ChickenController"/>; mutated on the state authority only (abilities run there), and
    /// <see cref="ChickenController"/> mirrors the networked resolutions (opacity, steal-back, aura).
    ///
    /// Kinds and rules: speed BOOST (&gt;1) = max, speed PENALTY (&lt;1) = min, final = boost * penalty;
    /// opacity = min; movement lock = any; deposit rate = max; steal-back = any / max amount;
    /// aura = any, strongest slow. The Underdog Surge multiplier and the victim-side slow are outside
    /// this rule on purpose.
    /// </remarks>
    public sealed class AbilityEffectStack
    {
        private struct Entry
        {
            public int Source;
            public float Value;
            public float Aux;
        }

        private sealed class Channel
        {
            private readonly List<Entry> _entries = new List<Entry>(4);

            public int Count => _entries.Count;

            public void Set(int source, float value, float aux = 0f)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Source != source) continue;
                    _entries[i] = new Entry { Source = source, Value = value, Aux = aux };
                    return;
                }
                _entries.Add(new Entry { Source = source, Value = value, Aux = aux });
            }

            public void Remove(int source)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Source != source) continue;
                    _entries.RemoveAt(i);
                    return;
                }
            }

            public void Clear() => _entries.Clear();

            public float Max(float none)
            {
                if (_entries.Count == 0) return none;
                float best = float.NegativeInfinity;
                for (int i = 0; i < _entries.Count; i++) best = Mathf.Max(best, _entries[i].Value);
                return best;
            }

            public float Min(float none)
            {
                if (_entries.Count == 0) return none;
                float best = float.PositiveInfinity;
                for (int i = 0; i < _entries.Count; i++) best = Mathf.Min(best, _entries[i].Value);
                return best;
            }

            /// <summary>The entry with the lowest Value (ties: the later one), if any.</summary>
            public bool TryGetMin(out Entry min)
            {
                min = default;
                if (_entries.Count == 0) return false;
                min = _entries[0];
                for (int i = 1; i < _entries.Count; i++)
                    if (_entries[i].Value <= min.Value) min = _entries[i];
                return true;
            }
        }

        /// <summary>Speed-boost sources (every value &gt; 1).</summary>
        private readonly Channel _boost = new Channel();
        /// <summary>Speed-penalty sources (every value &lt; 1).</summary>
        private readonly Channel _penalty = new Channel();
        private readonly Channel _opacity = new Channel();
        private readonly Channel _lock = new Channel();
        private readonly Channel _deposit = new Channel();
        private readonly Channel _stealBack = new Channel();
        private readonly Channel _aura = new Channel();

        // ---- Move speed (kinds A + B) --------------------------------------------

        /// <summary>
        /// Registers <paramref name="source"/>'s speed multiplier. Above 1 is a boost, below 1 a penalty
        /// (the two are different kinds and multiply together); exactly 1 is no effect and just removes it.
        /// </summary>
        public void SetMoveSpeed(int source, float multiplier)
        {
            RemoveMoveSpeed(source);
            if (multiplier > 1f) _boost.Set(source, multiplier);
            else if (multiplier < 1f) _penalty.Set(source, multiplier);
        }

        public void RemoveMoveSpeed(int source)
        {
            _boost.Remove(source);
            _penalty.Remove(source);
        }

        /// <summary>Strongest boost (1 if none) times strongest penalty (1 if none).</summary>
        public float MoveSpeedMultiplier => _boost.Max(1f) * _penalty.Min(1f);

        // ---- Stealth opacity (kind C) --------------------------------------------

        public void SetOpacity(int source, float opacity) => _opacity.Set(source, Mathf.Clamp01(opacity));
        public void RemoveOpacity(int source) => _opacity.Remove(source);

        /// <summary>Lowest active opacity; 1 when none.</summary>
        public float Opacity => _opacity.Min(1f);

        // ---- Movement lock (kind D) ----------------------------------------------

        public void SetMovementLock(int source) => _lock.Set(source, 1f);
        public void RemoveMovementLock(int source) => _lock.Remove(source);
        public bool MovementLocked => _lock.Count > 0;

        // ---- Deposit rate (kind G) -----------------------------------------------

        public void SetDepositRate(int source, float multiplier) => _deposit.Set(source, multiplier);
        public void RemoveDepositRate(int source) => _deposit.Remove(source);

        /// <summary>Strongest active multiplier; 1 when none.</summary>
        public float DepositRateMultiplier => _deposit.Max(1f);

        // ---- Steal-back (kind F) -------------------------------------------------

        public const float DefaultStealBackAmount = 4f;

        public void SetStealBack(int source, float amount) => _stealBack.Set(source, amount);
        public void RemoveStealBack(int source) => _stealBack.Remove(source);
        public bool StealBackActive => _stealBack.Count > 0;

        /// <summary>Largest active amount; the legacy default when none.</summary>
        public float StealBackAmount => _stealBack.Max(DefaultStealBackAmount);

        // ---- Aura emitter (kind I) -----------------------------------------------

        public void SetAura(int source, float slowFactor, float radius) => _aura.Set(source, slowFactor, radius);
        public void RemoveAura(int source) => _aura.Remove(source);
        public bool AuraActive => _aura.Count > 0;

        /// <summary>Strongest (lowest) slow factor and its radius. False when no aura runs.</summary>
        public bool TryGetAura(out float slowFactor, out float radius)
        {
            if (_aura.TryGetMin(out var e)) { slowFactor = e.Value; radius = e.Aux; return true; }
            slowFactor = 1f; radius = 0f;
            return false;
        }

        // ---- Whole-stack -----------------------------------------------------------

        /// <summary>Drops every modifier of one source — what a slot's deactivation does in one call.</summary>
        public void RemoveSource(int source)
        {
            RemoveMoveSpeed(source);
            RemoveOpacity(source);
            RemoveMovementLock(source);
            RemoveDepositRate(source);
            RemoveStealBack(source);
            RemoveAura(source);
        }

        /// <summary>Round reset: nothing is running any more.</summary>
        public void Clear()
        {
            _boost.Clear(); _penalty.Clear(); _opacity.Clear(); _lock.Clear();
            _deposit.Clear(); _stealBack.Clear(); _aura.Clear();
        }
    }

    /// <summary>
    /// Control-immunity (kind E) re-grant rule: a new grant never shortens an open window — whichever expiry
    /// is LATER wins. Pure so it is test-covered without a Fusion timer.
    /// </summary>
    public static class ControlImmunityRules
    {
        /// <param name="openRemainingSeconds">Seconds left on the current window (0 when none).</param>
        /// <param name="grantSeconds">Length of the incoming grant.</param>
        public static bool ShouldReplace(float openRemainingSeconds, float grantSeconds) =>
            grantSeconds > openRemainingSeconds;
    }
}
