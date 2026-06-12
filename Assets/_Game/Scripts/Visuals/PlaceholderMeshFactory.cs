using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Builds the placeholder "testing models" by combining Unity's built-in
    /// primitive meshes (sphere / cube / cylinder) into a single cached mesh per
    /// kind. Programmer-art stand-ins for the real artwork: chunky chicken
    /// silhouette per GDD §4 (big head, round body, tiny limbs), a grain-mound
    /// food pile, and a nest pad for player bases. Single mesh + single material
    /// so the existing per-class / per-owner tint paths (MaterialPropertyBlock on
    /// the renderer) keep working unchanged.
    /// </summary>
    public static class PlaceholderMeshFactory
    {
        public enum Kind : byte { Chicken = 0, FoodMound = 1, BaseNest = 2 }

        private static readonly Dictionary<Kind, Mesh> Cache = new Dictionary<Kind, Mesh>();

        public static Mesh Get(Kind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;

            Mesh built = kind switch
            {
                Kind.Chicken   => BuildChicken(),
                Kind.FoodMound => BuildFoodMound(),
                Kind.BaseNest  => BuildBaseNest(),
                _              => null,
            };
            if (built != null)
            {
                built.name = $"Placeholder_{kind}";
                Cache[kind] = built;
            }
            return built;
        }

        // ---- Builders ---------------------------------------------------------
        // All shapes are authored in the local space of the mesh they replace:
        // the chicken replaces the prefab's capsule (local bounds roughly -1..+1
        // on Y, feet at the bottom, facing +Z); mound and nest replace a unit
        // cube child (bounds -0.5..+0.5).

        private static Mesh BuildChicken()
        {
            var parts = new List<CombineInstance>();

            // Round body — slightly egg-shaped, sits low.
            AddPart(parts, Primitive.Sphere, new Vector3(0f, -0.30f, -0.05f), new Vector3(1.00f, 0.95f, 1.05f));

            // Big head — chunky proportions per GDD §4 (reference: Hei Hei).
            AddPart(parts, Primitive.Sphere, new Vector3(0f, 0.42f, 0.22f), new Vector3(0.62f, 0.62f, 0.60f));

            // Comb — three small boxes along the top of the head.
            AddPart(parts, Primitive.Cube, new Vector3(0f, 0.78f, 0.14f), new Vector3(0.10f, 0.16f, 0.14f));
            AddPart(parts, Primitive.Cube, new Vector3(0f, 0.82f, 0.27f), new Vector3(0.10f, 0.20f, 0.13f));
            AddPart(parts, Primitive.Cube, new Vector3(0f, 0.76f, 0.39f), new Vector3(0.10f, 0.14f, 0.12f));

            // Beak — small box poking forward from the head.
            AddPart(parts, Primitive.Cube, new Vector3(0f, 0.40f, 0.58f), new Vector3(0.14f, 0.10f, 0.22f));

            // Wings — flattened spheres tucked against the body sides.
            AddPart(parts, Primitive.Sphere, new Vector3(+0.46f, -0.28f, -0.08f), new Vector3(0.22f, 0.55f, 0.70f));
            AddPart(parts, Primitive.Sphere, new Vector3(-0.46f, -0.28f, -0.08f), new Vector3(0.22f, 0.55f, 0.70f));

            // Tail — flattened sphere angled up at the back.
            AddPart(parts, Primitive.Sphere, new Vector3(0f, 0.02f, -0.55f), new Vector3(0.35f, 0.50f, 0.25f),
                Quaternion.Euler(-30f, 0f, 0f));

            // Tiny legs — two short cylinders down to the capsule's foot line.
            AddPart(parts, Primitive.Cylinder, new Vector3(+0.18f, -0.85f, 0f), new Vector3(0.08f, 0.18f, 0.08f));
            AddPart(parts, Primitive.Cylinder, new Vector3(-0.18f, -0.85f, 0f), new Vector3(0.08f, 0.18f, 0.08f));

            return Combine(parts);
        }

        private static Mesh BuildFoodMound()
        {
            var parts = new List<CombineInstance>();

            // Three flattened spheres stacked into a grain mound. Replaces a unit
            // cube, so everything fits inside -0.5..+0.5 with the base at y=-0.5.
            AddPart(parts, Primitive.Sphere, new Vector3(0f, -0.38f, 0f), new Vector3(1.00f, 0.42f, 1.00f));
            AddPart(parts, Primitive.Sphere, new Vector3(0f, -0.16f, 0f), new Vector3(0.72f, 0.40f, 0.72f));
            AddPart(parts, Primitive.Sphere, new Vector3(0f, 0.06f, 0f), new Vector3(0.44f, 0.36f, 0.44f));

            // A few stray grains around the rim for texture.
            AddPart(parts, Primitive.Sphere, new Vector3(+0.38f, -0.48f, +0.22f), Vector3.one * 0.14f);
            AddPart(parts, Primitive.Sphere, new Vector3(-0.34f, -0.48f, -0.30f), Vector3.one * 0.12f);
            AddPart(parts, Primitive.Sphere, new Vector3(+0.10f, -0.48f, -0.42f), Vector3.one * 0.10f);

            return Combine(parts);
        }

        private static Mesh BuildBaseNest()
        {
            var parts = new List<CombineInstance>();

            // Flat pad replacing the unit cube footprint.
            AddPart(parts, Primitive.Cylinder, new Vector3(0f, -0.42f, 0f), new Vector3(1.05f, 0.08f, 1.05f));

            // Raised rim — a ring of small spheres approximating a nest's twigs.
            const int rimCount = 10;
            for (int i = 0; i < rimCount; i++)
            {
                float a = (i / (float)rimCount) * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a) * 0.46f, -0.28f, Mathf.Sin(a) * 0.46f);
                AddPart(parts, Primitive.Sphere, pos, new Vector3(0.22f, 0.18f, 0.22f));
            }

            // A couple of eggs resting inside.
            AddPart(parts, Primitive.Sphere, new Vector3(+0.10f, -0.30f, +0.06f), new Vector3(0.18f, 0.24f, 0.18f));
            AddPart(parts, Primitive.Sphere, new Vector3(-0.12f, -0.30f, -0.08f), new Vector3(0.16f, 0.22f, 0.16f));

            return Combine(parts);
        }

        // ---- Primitive mesh plumbing -------------------------------------------

        private enum Primitive { Sphere, Cube, Cylinder }

        private static Mesh _sphere, _cube, _cylinder;

        private static Mesh GetPrimitiveMesh(Primitive p)
        {
            switch (p)
            {
                case Primitive.Sphere:   return _sphere   != null ? _sphere   : (_sphere   = ExtractPrimitive(PrimitiveType.Sphere));
                case Primitive.Cube:     return _cube     != null ? _cube     : (_cube     = ExtractPrimitive(PrimitiveType.Cube));
                case Primitive.Cylinder: return _cylinder != null ? _cylinder : (_cylinder = ExtractPrimitive(PrimitiveType.Cylinder));
                default:                 return null;
            }
        }

        /// <summary>
        /// Grabs the shared mesh off a temporary primitive. Works in players and
        /// the Editor alike (GetBuiltinResource paths differ per pipeline; this
        /// route is version-proof).
        /// </summary>
        private static Mesh ExtractPrimitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }

        private static void AddPart(List<CombineInstance> parts, Primitive primitive,
            Vector3 position, Vector3 scale, Quaternion? rotation = null)
        {
            var mesh = GetPrimitiveMesh(primitive);
            if (mesh == null) return;
            parts.Add(new CombineInstance
            {
                mesh = mesh,
                transform = Matrix4x4.TRS(position, rotation ?? Quaternion.identity, scale),
            });
        }

        private static Mesh Combine(List<CombineInstance> parts)
        {
            var mesh = new Mesh();
            mesh.CombineMeshes(parts.ToArray(), mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
