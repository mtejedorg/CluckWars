using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Presentation's view of a chicken: the indicator, nameplate and badge stack cached at spawn, and where a
    /// floating pop over the bird starts. These were members of <c>ChickenController</c> until audit batch 4; they
    /// read the holder <see cref="ChickenModelPresenter.Bind"/> stored on <c>ChickenController.Presentation</c>,
    /// so a call is a field read and a cast: no <c>GetComponent</c>, safe every frame.
    /// </summary>
    /// <remarks>Before <c>Spawned</c>, or with no presenter bound, the holder is null: the three accessors return
    /// null and the pop anchors use a clearance of 0 (the floor alone), as a bird with no badge stack always did.</remarks>
    public static class ChickenPresentationAccess
    {
        private static ChickenPresentation Of(ChickenController chicken) => chicken.Presentation as ChickenPresentation;

        /// <summary>This chicken's rival indicator (cached at spawn, presentation readers only; may be null).</summary>
        public static RivalIndicator Indicator(this ChickenController chicken) => Of(chicken)?.Indicator;

        /// <summary>This chicken's nameplate (cached at spawn, presentation readers only; may be null).</summary>
        public static ChickenNameplate Nameplate(this ChickenController chicken) => Of(chicken)?.Nameplate;

        /// <summary>This bird's badge stack (cached at spawn; may be null).</summary>
        public static ChickenStatusBadges StatusBadges(this ChickenController chicken) => Of(chicken)?.StatusBadges;

        /// <summary>Where a floating pop over this bird starts: its pivot plus the pop height, above any shown badge rows
        /// (cached sibling, no per-call lookup; the floor alone when the bird has no badge stack).</summary>
        public static Vector3 PopAnchor(this ChickenController chicken) =>
            chicken.PopAnchorEntering(ChickenStatusBadges.Badge.None);

        /// <summary><see cref="PopAnchor"/> for a state-entry pop: clear of the badge row <paramref name="entering"/> that appears this frame.</summary>
        public static Vector3 PopAnchorEntering(this ChickenController chicken, ChickenStatusBadges.Badge entering)
        {
            var badges = Of(chicken)?.StatusBadges;
            return FloatingCombatText.PopSpawnPosition(chicken.transform.position, badges != null ? badges.PopClearHeight(entering) : 0f);
        }
    }
}
