using CluckWars.Gameplay;
using CluckWars.Logging;
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
    /// without requiring a MonoBehaviour reference. <c>AbilityController.TryActivate</c>
    /// refreshes both fields immediately after the refusal check and before the cast is
    /// committed — before <see cref="AbilityBaseSO.CanActivate"/> runs, and therefore
    /// before <c>ActiveSlot</c>, the activation timer and the cooldown are written. Both
    /// fields are consequently current for <c>CanActivate</c> and for <c>OnActivate</c>.
    /// The order matters: <c>CanActivate</c> on the zone abilities calls
    /// <see cref="CanSpawnZone"/>, which reads exactly these two fields, so refreshing
    /// any later would make the gate judge a stale context and refuse every first cast.
    /// </remarks>
    public sealed class AbilityContext
    {
        public readonly ChickenController Controller;

        /// <summary>
        /// Fusion NetworkRunner at the time of activation. Updated by
        /// <see cref="AbilityController"/> before each <see cref="AbilityBaseSO.CanActivate"/>
        /// call, and so before the <see cref="AbilityBaseSO.OnActivate"/> that may follow it;
        /// null before the first activation.
        /// </summary>
        public NetworkRunner Runner;

        /// <summary>
        /// App-wide prefab registry. Updated by <see cref="AbilityController"/> before
        /// each <see cref="AbilityBaseSO.CanActivate"/> call, and so before the
        /// <see cref="AbilityBaseSO.OnActivate"/> that may follow it. Use
        /// <c>ctx.PrefabRegistry?.AbilityZone</c> to spawn placed zones.
        /// </summary>
        public PrefabRegistrySO PrefabRegistry;

        /// <summary>
        /// The caster's logger, so an ability can report a failure instead of eating it.
        /// </summary>
        /// <remarks>
        /// Ability ScriptableObjects are assets, not scene objects: Zenject cannot inject
        /// them, and a service locator is ruled out on this project. Handing the already
        /// injected <c>ILogService</c> down through the context is therefore the only way
        /// an ability can say anything at all — do not "simplify" this into a static or
        /// locator lookup.
        ///
        /// Unlike <see cref="Runner"/> and <see cref="PrefabRegistry"/> this is assigned
        /// once, where the context is constructed in <c>AbilityController.Spawned</c>,
        /// because the injected logger never changes for the controller's lifetime — there
        /// is nothing for a per-activation refresh to refresh. Abilities still call
        /// <c>ctx.Log?.</c>: a context built outside the controller (a test, say) carries
        /// a null here.
        /// </remarks>
        public ILogService Log;

        public AbilityContext(ChickenController controller)
        {
            Controller = controller;
        }

        /// <summary>
        /// True when this context carries everything an ability needs to spawn a networked
        /// <c>AbilityZone</c>. When it returns false it has already logged an error naming
        /// the exact reference that is missing, so the caller only has to return.
        /// </summary>
        /// <remarks>
        /// Shared by the two zone-placing abilities (Feather Trap, Root Egg) so both report
        /// the same three distinct causes with the same wording — the three are genuinely
        /// different bugs, and one blanket message would send the reader to the wrong place.
        /// <see cref="ILogService.Error"/> rather than <c>Warn</c>: every one of them is
        /// unassigned wiring, which will never fix itself at runtime.
        /// </remarks>
        /// <param name="source">Log source tag of the calling ability.</param>
        /// <param name="abilityName">Player-facing ability name, for the log line.</param>
        public bool CanSpawnZone(string source, string abilityName)
        {
            if (Runner == null)
            {
                Log?.Error(source, $"{abilityName} cast lost: the ability context has no NetworkRunner. " +
                    "The press was refused before any cooldown was charged.");
                return false;
            }

            if (PrefabRegistry == null)
            {
                Log?.Error(source, $"{abilityName} cast lost: no PrefabRegistrySO was injected into " +
                    "AbilityController. The press was refused before any cooldown was charged.");
                return false;
            }

            if (PrefabRegistry.AbilityZone == null)
            {
                Log?.Error(source, $"{abilityName} cast lost: PrefabRegistrySO.AbilityZone is not assigned — " +
                    "wire the AbilityZone prefab onto the PrefabRegistry asset. The press was refused " +
                    "before any cooldown was charged.");
                return false;
            }

            return true;
        }
    }
}
