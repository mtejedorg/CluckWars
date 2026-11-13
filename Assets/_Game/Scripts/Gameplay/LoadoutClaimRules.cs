using System.Collections.Generic;
using CluckWars.Abilities;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Receiver-side rules for a replicated loadout (<c>AbilityController.LoadoutCodes</c>): may this chicken carry
    /// the passive and the abilities its own peer says it carries? Pure and Fusion-free, like
    /// <see cref="ControlClaimRules"/> and <see cref="StealRules"/>.
    /// </summary>
    /// <remarks>
    /// The codes are written by the chicken's state authority, which in Shared Mode is its owner's client, so on
    /// every other peer they are a claim. An honest spawner passes its selection through
    /// <c>MatchBootstrapper.ResolveLegalLoadout</c>; this is the same legality, asked of the result, and it is
    /// built from the same parts rather than a second copy of them: <see cref="AbilityRegistrySO.IsAllowedFor"/>
    /// (class pool), <see cref="FixedKit.TryResolve"/> (the kit, in <see cref="LoadoutMode.Fixed"/>) and
    /// <see cref="PreEquippedLoadout.Resolve"/> (the Peck and Signature slots, which are not class-filtered).
    /// <para>
    /// Everything it reads is shipped data or replicated state every peer has: the chicken's class, the registry,
    /// and <c>MatchConfigSO.LoadoutMode</c> (an asset value, never changed at runtime).
    /// </para>
    /// <para>
    /// <b>Why it matters beyond presentation.</b> <c>GameManager.AwardMatchEndBonuses</c> runs on the master and
    /// reads every chicken's passive (<c>PassiveAbilitySO.MatchEndBonusFood</c>), so a remote peer's replicated
    /// passive reaches the score. A passive that is not legal for the chicken's class is never applied.
    /// </para>
    /// </remarks>
    public static class LoadoutClaimRules
    {
        /// <summary>True for a passive this class may equip.</summary>
        public static bool IsLegalPassive(PassiveAbilitySO passive, ChickenClass cls) =>
            AbilityRegistrySO.IsAllowedFor(passive, cls);

        /// <summary>
        /// True when <paramref name="ability"/> may sit in <paramref name="slot"/> of a <paramref name="cls"/> chicken
        /// that equips <paramref name="passive"/> (already judged legal). In <see cref="LoadoutMode.Fixed"/> with a
        /// usable kit, only the kit's ability for that slot. Otherwise the Free rules, which a broken kit also falls
        /// back to at spawn: the passive's Peck-slot or Signature pre-equip, or any non-Peck ability in the class pool.
        /// </summary>
        public static bool IsLegalSlot(AbilityBaseSO ability, int slot, ChickenClass cls, PassiveAbilitySO passive,
            LoadoutMode mode, IEnumerable<AbilityBaseSO> pool, AbilityBaseSO plainPeck)
        {
            if (ability == null || ability is PassiveAbilitySO) return false;

            if (FixedKit.TryResolve(mode, passive, out var kit, out _))
                return slot >= 0 && slot < kit.Length && kit[slot] == ability;

            PreEquippedLoadout.Resolve(cls, passive, pool, plainPeck, out var peckSlot, out var signature);
            if (ability == peckSlot || ability == signature) return true;
            return !(ability.Is(AbilityJob.Peck)) && AbilityRegistrySO.IsAllowedFor(ability, cls);
        }

        /// <summary>
        /// What a peer shows in <paramref name="slot"/> when the replicated ability was refused: the kit's ability in
        /// <see cref="LoadoutMode.Fixed"/>, otherwise null ("keep what this peer already holds").
        /// </summary>
        public static AbilityBaseSO SafeSlot(int slot, PassiveAbilitySO passive, LoadoutMode mode) =>
            FixedKit.TryResolve(mode, passive, out var kit, out _) && slot >= 0 && slot < kit.Length ? kit[slot] : null;
    }
}
