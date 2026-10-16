using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Asset-level manifest of the Mobile-tier baked haze (Phase 8 C F1 stage 2): every hazed material copy the bake
    /// made, which original it copies, its distance band and baked amount, plus the band table itself. Written only by
    /// <c>SurroundHazeBaker</c> (<c>Cluck Wars/Map/Bake Mobile Surround Haze</c>); never edit by hand, re-bake.
    /// </summary>
    /// <remarks>
    /// <para><b>How a copy hazes (stock URP Lit, no custom shader).</b> PC blends the lit colour toward the haze in
    /// the colour buffer: <c>lerp(lit, haze, a)</c> (<see cref="CWHazeFeature"/>). URP Lit adds emission after lighting, so a
    /// copy with <c>_BaseColor = original * (1 - a)</c> and <c>_EmissionColor = haze * a</c> (both in colour-buffer space) renders
    /// <c>lit * (1 - a) + haze * a</c>: the same blend, independent of sun, shadow and ambient. The surround grass,
    /// which spans the whole ramp, gets the same thing per texel: <c>_BaseMap</c> = (1 - a) ramp and
    /// <c>_EmissionMap</c> = a / max ramp, so it carries the PC gradient instead of one flat amount.</para>
    /// <para>The renderer -> copy mapping is in the scene (<see cref="SurroundHazeSet"/> on <c>MapSurroundings</c>),
    /// because an asset cannot reference scene objects.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "SurroundHazeMap", menuName = "Cluck Wars/Surround Haze Map")]
    public sealed class SurroundHazeMapSO : ScriptableObject
    {
        /// <summary>One hazed copy of one original material at one band.</summary>
        [Serializable]
        public sealed class Copy
        {
            [SerializeField] private Material _original;
            [SerializeField] private Material _hazed;
            [Tooltip("Band index (0 = nearest); -1 = per-texel ramp (surround grass).")]
            [SerializeField] private int _band;
            [SerializeField] private float _amount;
            [Tooltip("How many surround renderer slots use this copy.")]
            [SerializeField] private int _uses;

            public Copy(Material original, Material hazed, int band, float amount, int uses)
            {
                _original = original;
                _hazed = hazed;
                _band = band;
                _amount = amount;
                _uses = uses;
            }

            public Material Original => _original;
            public Material Hazed => _hazed;
            public int Band => _band;
            public float Amount => _amount;
            public int Uses => _uses;
        }

        [Tooltip("Band k covers square distance [edge k, edge k+1) from the arena centre; the last band runs to infinity.")]
        [SerializeField] private float[] _bandEdges = Array.Empty<float>();
        [Tooltip("Amount baked into band k: HazeSettings.AmountAt at the band's representative (mid) distance.")]
        [SerializeField] private float[] _bandAmounts = Array.Empty<float>();
        [SerializeField] private List<Copy> _copies = new List<Copy>();
        [Tooltip("Number of surround renderers that got copies (SurroundHazeSet entries).")]
        [SerializeField] private int _rendererCount;

        public IReadOnlyList<float> BandEdges => _bandEdges;
        public IReadOnlyList<float> BandAmounts => _bandAmounts;
        public IReadOnlyList<Copy> Copies => _copies;
        public int RendererCount => _rendererCount;

        /// <summary>
        /// What the PC pass does to background (sky / clear-colour) pixels, as a camera background colour for the Mobile
        /// tier: <paramref name="background"/> blended toward the haze vector the PC shader uses
        /// (<see cref="HazeSettings.ShaderColour"/>) by <see cref="HazeSettings.MaxAmount"/>, in colour-buffer space
        /// (camera colours are linearised only in Linear colour space). The bake only hazes geometry; applying this is
        /// up to the tier swap.
        /// </summary>
        public static Color HazedBackground(Color background)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var h = HazeSettings.ShaderColour;
            var c = Color.Lerp(linear ? background.linear : background, new Color(h.x, h.y, h.z), HazeSettings.MaxAmount);
            if (linear) c = c.gamma;
            c.a = background.a;
            return c;
        }

#if UNITY_EDITOR
        /// <summary>Editor baker only: replaces the manifest.</summary>
        public void EditorSet(float[] bandEdges, float[] bandAmounts, List<Copy> copies, int rendererCount)
        {
            _bandEdges = bandEdges;
            _bandAmounts = bandAmounts;
            _copies = copies;
            _rendererCount = rendererCount;
        }
#endif
    }
}
