using CluckWars.Gameplay;
using UnityEngine;

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
    /// system would have had to reimplement every one of those, and the copies would drift.
    ///
    /// <b>Amount and cooldown come from <see cref="ChickenStatsSO"/>, not from this asset.</b>
    /// Foraging cadence is solved against the SCT axiom per class, so it is a class stat.
    /// All four classes currently share <c>PeckAmount = 3</c> and differ only in cooldown —
    /// but the amount is authored per class from day one because with a uniform amount the
    /// cooldown is the only lever, and it barely separates Speedy (0.77s) from Fatty (0.78s).
    ///
    /// <b>The movement lock is a balance requirement, not just feel.</b> The Balance Oracle
    /// charges travel and collection sequentially — walk to the pile, then stand and take
    /// food. A chicken that could peck while walking would beat its modelled SCT and the
    /// axiom would quietly stop describing the game. <see cref="Duration"/> is the window
    /// you are committed for, and it is deliberately shorter than every class's cooldown so
    /// the lock never fully overlaps the next press.
    ///
    /// <b>Food is credited at activation, not at the end of the peck.</b> Crediting on
    /// <see cref="OnDeactivate"/> would read better — the beak reaches the grain — but that
    /// callback also fires when the ability is torn down early (death, match stop), so it
    /// would need to distinguish "finished" from "interrupted" before it could pay out. The
    /// commitment cost is already carried by the lock and the cooldown. Revisit if an
    /// interrupt mechanic is ever added, since that is the point at which the distinction
    /// starts to matter.
    /// </remarks>
    [CreateAssetMenu(fileName = "Peck",
        menuName = "Cluck Wars/Ability/Utility/Peck", order = 4)]
    public sealed class PeckAbilitySO : AbilityBaseSO
    {
        public PeckAbilitySO()
        {
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Common;
            // Every class EXCEPT Assassin, which cannot forage and spends all four
            // slots on its kit. This flag is the single source of truth for "can this
            // class peck" — MatchBootstrapper's loadout sanitiser and the class-stat
            // authoring test both read it rather than hardcoding the class list.
            AllowedClasses = ChickenClassFlags.Warrior | ChickenClassFlags.Speedy | ChickenClassFlags.Fatty;
            BotRole = BotRole.Forage;
            Duration = 0.4f;
            Cooldown = 0.8f; // fallback only — ResolveCooldown reads the caster's class stat
        }

        protected override string DefaultIcon => "🌾";

        /// <summary>Peck targets a pile, never a chicken, so it declares no aim shape and is
        /// exempt from the whiff/hit styling that keys off one.</summary>
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool RequiresEnemyInRange => false;

        /// <summary>Per-class, solved against the SCT axiom. Falls back to the authored
        /// <see cref="AbilityBaseSO.Cooldown"/> only if stats are somehow missing.</summary>
        public override float ResolveCooldown(ChickenController caster)
        {
            var stats = caster != null ? caster.Stats : null;
            return stats != null && stats.PeckCooldown > 0f ? stats.PeckCooldown : Cooldown;
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
            if (cargo.Cargo >= cargo.Capacity) return false;

            return FindPile(caster) != null;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var chicken = ctx.Controller;
            if (chicken == null) return;

            // Commit the chicken in place for Duration. Cleared in OnDeactivate, which
            // AbilityController calls on timer expiry AND on every early teardown, so the
            // lock cannot outlive the peck.
            chicken.MovementLocked = true;

            var cargo = chicken.Cargo;
            var pile = FindPile(chicken);
            if (cargo == null || pile == null) return;

            float amount = ResolveAmount(chicken);
            float spaceLeft = cargo.Capacity - cargo.Cargo;

            // pile.Available, not pile.Amount — the centre island is permanent and holds a
            // floor that is terrain, not food. Crediting against Amount would make it an
            // infinite source.
            float take = Mathf.Min(amount, spaceLeft, pile.Available);
            if (take <= 0f) return;

            // Optimistic credit then drain, the same shape every steal in the game uses:
            // the pile's authority clamps to whatever is actually there, so an over-request
            // from a stale local view is harmless.
            cargo.Cargo += take;
            pile.RPC_Drain(take);
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            if (ctx.Controller != null) ctx.Controller.MovementLocked = false;
        }

        /// <summary>Food taken per press for this caster's class.</summary>
        private static float ResolveAmount(ChickenController caster)
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
