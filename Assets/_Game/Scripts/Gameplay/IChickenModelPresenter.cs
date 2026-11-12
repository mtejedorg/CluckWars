using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// What presentation keeps for one chicken (its cached indicator, nameplate and badge stack). Gameplay only
    /// stores the reference on <see cref="ChickenController.Presentation"/> and never looks inside: the concrete
    /// type and its readers live in <c>CluckWars.Visuals</c> (<c>ChickenPresentationAccess</c>).
    /// </summary>
    public interface IChickenPresentation { }

    /// <summary>
    /// The presentation work <see cref="ChickenController"/> triggers while it composes a chicken at spawn: seating
    /// the ground anchor, fitting the foot rings, lifting the head overlays, tinting and animating the class model.
    /// The chicken decides WHEN (the order inside <c>Spawned</c> / <c>AttachClassModel</c> is load-bearing); the
    /// implementation (<c>ChickenModelPresenter</c> in <c>CluckWars.Visuals</c>, bound by <c>ProjectInstaller</c>)
    /// decides HOW. Gameplay references no presentation type (audit batch 4, A4b).
    /// </summary>
    /// <remarks>Stateless: one instance serves every chicken, so each call takes the chicken. A component the
    /// prefab does not have is skipped silently, exactly as the direct calls did (a decoy has no cargo bars).</remarks>
    public interface IChickenModelPresenter
    {
        /// <summary>Looks up the chicken's presentation siblings once and returns them in a holder the chicken keeps.
        /// Called at the start of every <c>Spawned</c>.</summary>
        IChickenPresentation Bind(ChickenController chicken);

        /// <summary>The root scale was just applied: ground overlays hang off an anchor that depends on it.</summary>
        void OnScaleApplied(ChickenController chicken);

        /// <summary>Lifts the overlays above the head by <paramref name="lift"/> root-local metres for a class model
        /// drawn taller than the collider.</summary>
        void ApplyHeadLift(ChickenController chicken, float lift, float classScale);

        /// <summary>The class model is parented and scaled: the foot rings must clear its body.</summary>
        void FitFootprint(ChickenController chicken, Transform model);

        /// <summary>
        /// Points the tint and the animation helper at the freshly attached model: tint target then tint colour,
        /// then the animator's model root and whether the skeletal path came up.
        /// </summary>
        /// <returns>True when the chicken has a tint component and the tint was applied, so the caller records the
        /// body colour only in that case.</returns>
        bool ApplyClassLook(ChickenController chicken, Transform modelRoot, Color tint, float strength, bool skeletal);
    }
}
