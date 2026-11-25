using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Foraging. Takes a fixed bite of food out of the nearest pile you are standing at,
    /// on a per-class cooldown. Replaces the automatic pile drain that used to run every
    /// tick inside <see cref="ChickenCargo"/>.
    /// </summary>
    /// <remarks>
    /// <b>Why this is an ability rather than an interaction system.</b> Living in a real
    /// slot buys the per-slot cooldown timer, the HUD hex with its radial sweep and
    /// countdown, the <c>AbilityRefusalRules</c> precedence table, the denied-press bump,
    /// and bot integration through <c>TryGetReadySlotForRole</c>. A parallel "interact"
    /// system would have had to reimplement every one of those, and the copies drift.
    ///
    /// <b>Amount, cooldown and cast time come from <see cref="ChickenStatsSO"/>, not from this asset.</b>
    /// <c>PeckAmount</c>, <c>PeckCooldown</c> and <c>PeckCastSeconds</c> are class stats, so BalanceBook
    /// deltas reach them. <see cref="Duration"/> is only the fallback for a caster with no stats.
    ///
    /// <b>Peck is an interruptible cast (BL-206).</b> The chicken is rooted for the whole cast (the movement
    /// lock is a balance requirement: the Oracle charges travel and collection sequentially). Food is asked of the
    /// pile when the cast ends naturally (<c>ctx.EndedNaturally</c> in <see cref="OnDeactivate"/>), with the pile's
    /// reach and food re-checked then. A stun, a shove, an execution or the chicken's own next move cuts the cast and
    /// nothing is asked. The pile authority still serves and reports the food (ARCH-NET-07), unchanged.
    /// </remarks>
    [CreateAssetMenu(fileName = "Peck",
        menuName = "Cluck Wars/Ability/Utility/Peck", order = 4)]
    public sealed class PeckAbilitySO : AbilityBaseSO
    {
        public override AbilityJob Job => AbilityJob.Peck;

        public override string DescriptionKey => UiKeys.AbilityPeckDesc;

        public PeckAbilitySO()
        {
            Category = AbilityCategory.Utility;
            // None: Peck is never hand-picked. AllowedClasses only means "selectable in the
            // loadout picker", and Peck is forced into slot 0 through the Peck-slot pre-equip
            // column (AbilityBaseSO.PeckSlotPreEquippedBy), which is what makes a subclass
            // forage. The one hard rule "the Assassin cannot forage" lives in
            // PreEquippedLoadout.ClassMayForage, not in this mask.
            AllowedClasses = ChickenClassFlags.None;
            BotRole = BotRole.Forage;
            Duration = 0.4f;
            Cooldown = 0.8f; // fallback only — ResolveCooldown reads the caster's class stat
        }

        /// <summary>Peck targets a pile, never a chicken, so it declares no aim shape and is
        /// exempt from the whiff/hit styling that keys off one.</summary>
        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.None;
        public override bool RequiresEnemyInRange => false;

        /// <summary>Casting any other move cuts the cast: the chicken rooted itself for it and no food is asked.</summary>
        public override bool EndsOnNextMove => true;

        /// <summary>A pile with no collectable food left in reach (emptied by another chicken, or never there) cuts the cast:
        /// it would land nothing. A pile with only some food left does not cut; the cast lands what remains. Same test as
        /// the landing, so the two cannot disagree.</summary>
        public override bool HasLostItsTarget(ChickenController caster) => caster != null && FindPile(caster) == null;

        /// <summary>The cast length: the class's <see cref="ChickenStatsSO.PeckCastSeconds"/>. Falls back to
        /// <see cref="AbilityBaseSO.Duration"/> only if stats are somehow missing. Every reader (run timer, ring drain,
        /// HUD, animator) goes through this.</summary>
        public override float ResolveActiveDuration(ChickenController caster) =>
            PeckCastRules.CastSeconds(caster != null ? caster.Stats : null, Duration);

        /// <summary>Per-class, solved against the SCT axiom. Falls back to the authored
        /// <see cref="AbilityBaseSO.Cooldown"/> only if stats are somehow missing.</summary>
        public override float ResolveCooldown(ChickenController caster)
        {
            var stats = caster != null ? caster.Stats : null;
            float classCooldown = stats != null && stats.PeckCooldown > 0f ? stats.PeckCooldown : Cooldown;
            var passive = caster != null ? caster.Passive : null;
            return PeckCastRules.Cooldown(classCooldown, passive != null ? passive.PeckCooldownMultiplier : 1f);
        }

        /// <summary>
        /// Usable only when there is a pile in reach with food actually available AND room
        /// in the cargo hold. Both gates matter: without the first the press burns a
        /// cooldown on empty air, and without the second a full chicken would stand at a
        /// pile pecking forever while its cargo never moves.
        /// </summary>
        public override bool IsUsable(ChickenController caster)
        {
            if (caster == null) return false;

            var cargo = caster.Cargo;
            if (cargo == null) return false;
            if (cargo.FreeSpace <= 0f) return false;   // full, or the room is held by a peck still being answered

            return FindPile(caster) != null;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var chicken = ctx.Controller;
            if (chicken == null) return;

            // Root the chicken for the whole cast. Cleared in OnDeactivate, which AbilityController calls on natural
            // end AND on every early teardown, so the lock cannot outlive the cast. No food is asked yet.
            chicken.Effects.SetMovementLock(ctx.Slot);
            chicken.Abilities.NoteFreshStart();   // a new cast is a new, unbitten fresh state for an Enforcer's hit
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            var chicken = ctx.Controller;
            if (chicken == null) return;
            chicken.Effects.RemoveMovementLock(ctx.Slot);
            if (!ctx.EndedNaturally) return;                       // cut or torn down: no food, by design

            var gm = GameManager.Instance;
            if (gm == null)
            {
                ctx.Log?.Warn(LogSource, "Peck landed with no GameManager: wiring fault, no food taken.");
                return;
            }
            if (!gm.IsMatchRunning) return;                        // legal: the round ended on this tick

            var cargo = chicken.Cargo;
            var pile = FindPile(chicken);                          // reach and food re-checked now
            if (cargo == null || pile == null) return;             // legal: pile emptied or chicken moved off it; lands empty

            // pile.Available, not pile.Amount: the centre island is permanent and holds a floor that is terrain, not food.
            float take = PeckCastRules.LandingTake(AmountFor(chicken), cargo.FreeSpace, pile.Available);
            if (take <= 0f) return;                                // legal: the beak filled meanwhile

            // Ask, do not credit: this peer's view of the pile can be stale. The pile's authority removes what is really
            // there and reports it, and only that report credits the cargo (FoodTransferRules).
            cargo.RequestPeck(pile, take);
            chicken.Abilities.NotePeckLanded();
        }

        /// <summary>Food taken per press for this caster's class. Also the bound the pile and the pecker's own
        /// settlement hold a single peck to (<c>FoodTransferRules.ServePeck</c>).</summary>
        public static float AmountFor(ChickenController caster)
        {
            var stats = caster != null ? caster.Stats : null;
            return stats != null && stats.PeckAmount > 0f ? stats.PeckAmount : 3f;
        }

        /// <summary>
        /// Nearest pile whose SURFACE this chicken is close enough to drain, with food
        /// still available. Ranking is by surface distance, matching
        /// <see cref="FoodPile.IsWithinCollectRange"/> — a centre-distance test would rate
        /// a chicken standing on a big island's rim as further away than a small pile
        /// several metres off.
        /// </summary>
        private static FoodPile FindPile(ChickenController caster)
        {
            var piles = FoodPile.ActivePiles;
            var selfPos = caster.transform.position;
            FoodPile best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < piles.Count; i++)
            {
                var pile = piles[i];
                if (pile == null || pile.Object == null || !pile.Object.IsValid) continue;
                if (!pile.HasCollectableFood) continue;
                if (!pile.IsWithinCollectRange(selfPos)) continue;

                float distance = pile.DistanceToSurface(selfPos);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pile;
                }
            }
            return best;
        }
    }
}
