using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// Combines multiple <see cref="IInputProvider"/>s so keyboard + touch can both
    /// drive the local player at the same time. Movement returns whichever provider
    /// has the larger magnitude; held / pressed booleans OR across all providers.
    /// </summary>
    /// <remarks>
    /// Edge-triggered <c>GetAbilityXPressed</c> calls all underlying providers each
    /// tick — be aware they may have one-shot semantics that consume the press, so
    /// don't read them more than once per simulation tick.
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
    }
}
