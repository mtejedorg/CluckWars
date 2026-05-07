using CluckWars.Gameplay;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Argument bundle passed to <see cref="AbilityBaseSO.OnActivate"/> /
    /// <see cref="AbilityBaseSO.OnDeactivate"/>. Keeps ability ScriptableObjects free
    /// of <c>GetComponent</c> calls — the controller has already resolved everything.
    /// </summary>
    public sealed class AbilityContext
    {
        public readonly ChickenController Controller;

        public AbilityContext(ChickenController controller)
        {
            Controller = controller;
        }
    }
}
