using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Single source of truth for "where is the grass under this chicken". The chicken root sits
    /// at the CharacterController capsule's mid-height (~0.8 m up) and the capsule rests a
    /// skin width (0.08) above the ground, so flat overlays authored at a small local Y floated
    /// ~0.8 m high. Every ground overlay (identity ring, rival ring, state blobs, slow streaks,
    /// wind-up glow, target bracket/glyph) is parented to <see cref="Anchor"/> instead of the
    /// chicken root and keeps its own small lift above the grass as the anchor-local Y.
    /// </summary>
    /// <remarks>
    /// Read from the chicken's own CharacterController component (centre, height and skin are
    /// serialized data, valid whether or not the controller is enabled, and on proxies/bots
    /// alike). The skin width is a world distance, not scaled by the root, so the anchor Y
    /// depends on the root scale; <c>ChickenController.Spawned</c> applies the class scale after
    /// the overlays' Awake, hence <see cref="Refresh"/>.
    /// </remarks>
    public static class ChickenGround
    {
        public const string AnchorName = "GroundAnchor";

        /// <summary>Prefab capsule (height 1.6, centre 0) bottom, used only if the chicken
        /// somehow has no CharacterController. Never falls back to the root (that is the bug).</summary>
        public const float FallbackLocalFeetY = -0.8f;

        /// <summary>Prefab skin width, same fallback rule.</summary>
        public const float FallbackSkinWidth = 0.08f;

        /// <summary>Local Y of the capsule bottom (the surface the capsule is modelled to touch).</summary>
        public static float LocalFeetY(CharacterController cc) =>
            cc != null ? cc.center.y - cc.height * 0.5f : FallbackLocalFeetY;

        /// <summary>Local Y of the grass under the chicken: capsule bottom minus the resting
        /// skin gap, which is a world distance and so is divided by the root scale.</summary>
        public static float LocalGroundY(Component chicken)
        {
            var cc = chicken != null ? chicken.GetComponent<CharacterController>() : null;
            float scaleY = Mathf.Max(0.01f, Mathf.Abs(chicken.transform.lossyScale.y));
            float skin = cc != null ? cc.skinWidth : FallbackSkinWidth;
            return LocalFeetY(cc) - skin / scaleY;
        }

        /// <summary>
        /// World Y of the root pivot at which the chicken STANDS on ground at <paramref name="groundY"/>: the capsule bottom
        /// rests one skin width above it (the same resting gap <see cref="LocalGroundY"/> models). CW-021 / CW-006: chickens
        /// used to be placed at the spawn point's Y (0.05) with the pivot at the capsule centre, i.e. half under the grass, and
        /// only the first <c>CharacterController.Move</c> (at GO) lifted them. Placing them at this height makes that Move a
        /// no-op, with no per-tick settle (which, moving a chicken whose transform had not caught up with a networked
        /// teleport, undid the round-reset teleports).
        /// </summary>
        public static float StandingPivotY(float capsuleCenterY, float capsuleHeight, float skinWidth, float rootScaleY, float groundY = 0f)
        {
            float scaleY = Mathf.Max(0.01f, Mathf.Abs(rootScaleY));
            return groundY + scaleY * (capsuleHeight * 0.5f - capsuleCenterY) + skinWidth;
        }

        /// <summary>World Y of the grass under the chicken right now.</summary>
        public static float GroundWorldY(Component chicken) =>
            chicken.transform.TransformPoint(new Vector3(0f, LocalGroundY(chicken), 0f)).y;

        /// <summary>The child transform that sits on the grass under the chicken; created on
        /// first use so overlay Awakes can run in any order.</summary>
        public static Transform Anchor(Component chicken)
        {
            var t = chicken.transform.Find(AnchorName);
            if (t == null)
            {
                t = new GameObject(AnchorName).transform;
                t.SetParent(chicken.transform, worldPositionStays: false);
            }
            t.localPosition = new Vector3(0f, LocalGroundY(chicken), 0f);
            return t;
        }

        /// <summary>Re-seat the anchor after the root scale changed.</summary>
        public static void Refresh(Component chicken) => Anchor(chicken);
    }
}
