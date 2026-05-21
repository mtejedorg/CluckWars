using CluckWars.Gameplay;
using Fusion;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Argument bundle passed to <see cref="AbilityBaseSO.OnActivate"/> /
    /// <see cref="AbilityBaseSO.OnDeactivate"/>. Keeps ability ScriptableObjects free
    /// of <c>GetComponent</c> calls — the controller has already resolved everything.
    /// </summary>
    /// <remarks>
    /// v0.3 / Part B: <see cref="Runner"/> and <see cref="PrefabRegistry"/> added
    /// so abilities that spawn NetworkObjects (Feather Trap, Root Egg) can do so
    /// without requiring a MonoBehaviour reference. <c>AbilityController</c> updates
    /// both fields at the start of each <c>TryActivate</c> call so they are always
    /// current at the time <c>OnActivate</c> runs.
    /// </remarks>
    public sealed class AbilityContext
    {
        public readonly ChickenController Controller;

        /// <summary>
        /// Fusion NetworkRunner at the time of activation. Updated by
        /// <see cref="AbilityController"/> before each <see cref="AbilityBaseSO.OnActivate"/>
        /// call; null before the first activation.
        /// </summary>
        public NetworkRunner Runner;

        /// <summary>
        /// App-wide prefab registry. Updated by <see cref="AbilityController"/> before
        /// each <see cref="AbilityBaseSO.OnActivate"/> call. Use
        /// <c>ctx.PrefabRegistry?.AbilityZone</c> to spawn placed zones.
        /// </summary>
        public PrefabRegistrySO PrefabRegistry;

        public AbilityContext(ChickenController controller)
        {
            Controller = controller;
        }
    }
}
