using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Fatty alternative passive (ADR 0003): <b>Barge shoves chickens as well as terrain.</b>
    /// While the owner is mid-Barge, rivals it ploughs into are pushed aside.
    /// </summary>
    [CreateAssetMenu(fileName = "Juggernaut", menuName = "Cluck Wars/Passive/Juggernaut", order = 10)]
    public sealed class JuggernautPassiveSO : PassiveAbilitySO
    {
        [Header("Juggernaut")]
        [Tooltip("Horizontal radius (metres) within which a barging Juggernaut shoves rivals aside.")]
        [Min(0.1f)] public float ShoveRadius = 1.4f;

        [Tooltip("Per-tick shove impulse. Applied continuously while barging rather than once " +
                 "per victim — see the note in OnTraversalTick about ScriptableObjects having no " +
                 "per-owner state. Keep modest; it integrates over the whole Barge window.")]
        [Min(0f)] public float ShoveImpulsePerTick = 0.9f;

        public JuggernautPassiveSO()
        {
            DisplayName = "Juggernaut";
            ShortLabel = "JUGG";
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        protected override string DefaultIcon => "🪵";

        public override void OnActivate(AbilityContext ctx)
        {
            // Nothing to arm — the effect is expressed per-tick in OnTraversalTick.
        }

        /// <summary>
        /// Shoves nearby rivals aside while the owner is barging.
        /// </summary>
        /// <remarks>
        /// Applied as a small <b>continuous</b> impulse rather than one larger hit per victim,
        /// because this asset is shared by every chicken equipping it and so cannot track
        /// "already shoved this one" per owner. A continuous push also reads better for a
        /// plough-through: victims slide out of the way instead of being punted once.
        /// Routed through <c>RPC_ApplyKnockback</c> (RpcSources.All → the target's
        /// StateAuthority), which is the sanctioned cross-authority write.
        /// </remarks>
        public override void OnTraversalTick(ChickenController self, TerrainTraversal tier)
        {
            if (tier != TerrainTraversal.Barge || self == null) return;

            var all = ChickenController.ActiveControllers;
            Vector3 origin = self.transform.position;
            float radiusSqr = ShoveRadius * ShoveRadius;

            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                // Decoys are shovable like any other body — a Barge that ploughed straight
                // through a Doppelganger would be a free way to identify one.
                if (other == null || other == self) continue;
                if (other.Combat == null || other.Combat.IsDead) continue;

                Vector3 delta = other.transform.position - origin;
                delta.y = 0f;                                   // XZ only — never shove upward
                if (delta.sqrMagnitude > radiusSqr) continue;

                // Degenerate overlap: fall back to the barger's facing so the impulse is never zero.
                Vector3 dir = delta.sqrMagnitude > 0.0001f
                    ? delta.normalized
                    : self.transform.forward;

                other.RPC_ApplyKnockback(dir * ShoveImpulsePerTick);
            }
        }
    }
}
