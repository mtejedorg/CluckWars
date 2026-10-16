using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Phase 8 chunk C R3: the class model's rendered lateral half-width, used to size the player-colour foot rings
    /// (see <see cref="GroundRingStyle"/>). Measured from the model's mesh bind-pose bounds (so it never changes with the
    /// animation), at the model's scale, in chicken-root-local metres.
    /// </summary>
    /// <remarks>
    /// Lateral (X) only: the models face +Z, and the Z extent includes beak, tail and crest, which would make the ring a
    /// long way wider than the belly it has to clear.
    /// </remarks>
    public static class ChickenFootprint
    {
        /// <summary>Half of the model's width across the body, root-local; 0 when the model has no mesh.</summary>
        public static float HalfWidth(Transform root, Transform model)
        {
            if (root == null || model == null) return 0f;

            float half = 0f;
            var toRoot = root.worldToLocalMatrix;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                          : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                if (mesh == null) continue;

                var b = mesh.bounds;
                var m = toRoot * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    half = Mathf.Max(half, Mathf.Abs(m.MultiplyPoint3x4(corner).x));
                }
            }
            return half;
        }

        /// <summary>Measures the freshly attached model and tells every ground ring owner.</summary>
        public static void Apply(Component chicken, Transform model)
        {
            float half = HalfWidth(chicken.transform, model);
            chicken.GetComponent<ChickenWorldBars>()?.SetBodyFootprint(half);
            chicken.GetComponent<RivalIndicator>()?.SetBodyFootprint(half);
        }
    }
}
