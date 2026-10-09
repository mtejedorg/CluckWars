using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// Combines multiple <see cref="IInputProvider"/>s so keyboard + touch can both
    /// drive the local player at the same time. Movement returns whichever provider
    /// has the larger magnitude; held / pressed booleans OR across all providers.
    /// </summary>
    /// <remarks>
    /// Edge-triggered <c>GetAbilityXPressed</c> / <c>GetAbilityCancelPressed</c> calls
    /// all underlying providers each tick — be aware they may have one-shot semantics
    /// that consume the press, so don't read them more than once per simulation tick.
    /// <c>GetAbilityHeld</c> is level-triggered, not edge-triggered, so — unlike the
    /// press getters — it is safe to read repeatedly within the same tick; it has no
    /// latch to consume.
    /// </remarks>
    public sealed class CompositeInputProvider : IInputProvider
    {
        private readonly IInputProvider[] _providers;

        public CompositeInputProvider(params IInputProvider[] providers)
        {
            _providers = providers ?? new IInputProvider[0];
        }

        public Vector2 GetMovement()
        {
            var best = Vector2.zero;
            float bestSqr = 0f;
            for (int i = 0; i < _providers.Length; i++)
            {
                var v = _providers[i].GetMovement();
                var sqr = v.sqrMagnitude;
                if (sqr > bestSqr)
                {
                    bestSqr = sqr;
                    best = v;
                }
            }
            return best;
        }

        public bool GetAbility1Pressed()
        {
            // Read all so each provider's edge-state is consumed once this tick.
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetAbility1Pressed();
            return any;
        }

        public bool GetAbility2Pressed()
        {
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetAbility2Pressed();
            return any;
        }

        public bool GetAbility3Pressed()
        {
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetAbility3Pressed();
            return any;
        }

        public bool GetAbility4Pressed()
        {
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetAbility4Pressed();
            return any;
        }

        /// <summary>
        /// Level-triggered (see the interface doc) — reading every provider every
        /// tick has no consumption side effect here, unlike the edge-triggered
        /// getters, but every provider is still read for consistency with them.
        /// </summary>
        public bool GetAbilityHeld(int slot)
        {
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetAbilityHeld(slot);
            return any;
        }

        public bool GetAbilityCancelPressed()
        {
            // Edge-triggered — read all so each provider's latch is consumed once
            // this tick, same contract as the AbilityXPressed getters above.
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetAbilityCancelPressed();
            return any;
        }

        public bool IsAbilityCancelArmed()
        {
            for (int i = 0; i < _providers.Length; i++)
                if (_providers[i].IsAbilityCancelArmed()) return true;
            return false;
        }

        /// <summary>The device family of the most recently used provider (see <see cref="IInputProvider.LastActiveTime"/>).</summary>
        public InputDeviceKind Device
        {
            get
            {
                int idx = MostRecentProvider(requireAim: false, groundY: 0f);
                // A provider that has never been used is not "the device in hand".
                return idx >= 0 && !float.IsNegativeInfinity(_providers[idx].LastActiveTime)
                    ? _providers[idx].Device
                    : InputDeviceKind.None;
            }
        }

        public float LastActiveTime
        {
            get
            {
                float best = float.NegativeInfinity;
                for (int i = 0; i < _providers.Length; i++)
                    best = Mathf.Max(best, _providers[i].LastActiveTime);
                return best;
            }
        }

        /// <summary>The aim of the most recently used provider that has one (a resting mouse does not beat a stick in use).</summary>
        public AimInput GetAim(float groundY)
        {
            int idx = MostRecentProvider(requireAim: true, groundY);
            return idx >= 0 ? _providers[idx].GetAim(groundY) : AimInput.None;
        }

        private int MostRecentProvider(bool requireAim, float groundY)
        {
            int best = -1;
            float bestTime = float.NegativeInfinity;
            for (int i = 0; i < _providers.Length; i++)
            {
                if (requireAim && _providers[i].GetAim(groundY).Kind == AimInputKind.None) continue;
                float t = _providers[i].LastActiveTime;
                if (best < 0 || t > bestTime || (t == bestTime && TiePriority(_providers[i].Device) > TiePriority(_providers[best].Device)))
                {
                    best = i;
                    bestTime = t;
                }
            }
            return best;
        }

        /// <summary>
        /// Who wins when two devices were last used in the same frame: a finger beats a pad beats the keyboard / mouse.
        /// A touchscreen laptop raises mouse events alongside a touch, and the touch layout must win that tie.
        /// </summary>
        private static int TiePriority(InputDeviceKind kind) => kind switch
        {
            InputDeviceKind.Touch         => 3,
            InputDeviceKind.Gamepad       => 2,
            InputDeviceKind.KeyboardMouse => 1,
            _                             => 0,
        };

        public bool GetBackPressed()
        {
            // Edge-triggered: read every provider, same contract as the getters above.
            bool any = false;
            for (int i = 0; i < _providers.Length; i++)
                any |= _providers[i].GetBackPressed();
            return any;
        }
    }
}
