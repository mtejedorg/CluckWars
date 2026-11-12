using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// One chicken's presentation siblings, looked up once at spawn by <see cref="ChickenModelPresenter.Bind"/> and
    /// kept on <c>ChickenController.Presentation</c>. Read through <see cref="ChickenPresentationAccess"/>
    /// (<c>chicken.Nameplate()</c>, <c>chicken.PopAnchor()</c>...): cached references, no per-frame lookup. Any of the
    /// three may be null (the prefab does not carry it).
    /// </summary>
    public sealed class ChickenPresentation : IChickenPresentation
    {
        public readonly RivalIndicator Indicator;
        public readonly ChickenNameplate Nameplate;
        public readonly ChickenStatusBadges StatusBadges;

        public ChickenPresentation(RivalIndicator indicator, ChickenNameplate nameplate, ChickenStatusBadges statusBadges)
        {
            Indicator = indicator;
            Nameplate = nameplate;
            StatusBadges = statusBadges;
        }
    }

    /// <summary>
    /// The presentation half of composing a chicken at spawn (see <see cref="IChickenModelPresenter"/>). These are
    /// the calls <c>ChickenController</c> used to make itself; it now makes them through the interface, at the same
    /// points, so gameplay references no Visuals type. Stateless; bound once in <c>ProjectInstaller</c>.
    /// </summary>
    public sealed class ChickenModelPresenter : IChickenModelPresenter
    {
        public IChickenPresentation Bind(ChickenController chicken) =>
            new ChickenPresentation(
                chicken.GetComponent<RivalIndicator>(),
                chicken.GetComponent<ChickenNameplate>(),
                chicken.GetComponent<ChickenStatusBadges>());

        public void OnScaleApplied(ChickenController chicken) => ChickenGround.Refresh(chicken);

        public void ApplyHeadLift(ChickenController chicken, float lift, float classScale)
        {
            chicken.GetComponent<ChickenNameplate>()?.SetHeadLift(lift);
            chicken.GetComponent<ChickenWorldBars>()?.SetHeadLift(lift);
            chicken.GetComponent<ChickenStatusBadges>()?.SetHeadLift(lift);
            chicken.GetComponent<ChickenStateOverlays>()?.SetHeadLift(lift, classScale);
        }

        public void FitFootprint(ChickenController chicken, Transform model) => ChickenFootprint.Apply(chicken, model);

        public bool ApplyClassLook(ChickenController chicken, Transform modelRoot, Color tint, float strength, bool skeletal)
        {
            var visuals = chicken.GetComponent<ChickenVisuals>();
            if (visuals != null)
            {
                // Order matters: point the tint at the model before colouring it. The model
                // did not exist when ChickenVisuals.Awake built its renderer list.
                visuals.SetModelRoot(modelRoot);
                visuals.ApplyTint(tint, strength);
            }

            // Hand the same model root to the animator helper, and tell it whether the
            // skeletal path came up. Without the model root it has nothing to offset;
            // without the skeletal flag it cannot know which of its two paths to run.
            var animator = chicken.GetComponent<ChickenAnimator>();
            if (animator != null)
            {
                animator.SetModelRoot(modelRoot);
                animator.SetSkeletalActive(skeletal);
            }
            return visuals != null;
        }
    }
}
