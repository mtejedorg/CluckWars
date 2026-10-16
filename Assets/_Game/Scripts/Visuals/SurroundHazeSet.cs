using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Mobile-tier baked haze (Phase 8 C F1 stage 2): which surround renderer gets which hazed material copies. Lives
    /// on the <c>MapSurroundings</c> root in <c>Map.unity</c> and is written ONLY by the Editor baker
    /// (<c>Cluck Wars/Map/Bake Mobile Surround Haze</c>, <c>SurroundHazeBaker</c>). Data only: the tier swap that
    /// assigns <see cref="Entry.Hazed"/> on the Mobile tier (and leaves / puts back <see cref="Entry.Original"/> on
    /// PC) is a separate runtime component.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a scene component and not only an SO.</b> A ScriptableObject asset cannot reference scene objects,
    /// so the renderer side of the mapping lives in the scene it belongs to, as direct references: the swap is a
    /// plain loop (<c>e.Renderer.sharedMaterials = e.Hazed</c>), with no name or path search at runtime. The
    /// asset-level list of copies (original material, band, amount, copy) is <see cref="SurroundHazeMapSO"/>.</para>
    /// <para><b>Static batching.</b> These renderers are <c>BatchingStatic</c>. Swapping a static-batched renderer's
    /// MATERIALS is fine (materials are per renderer; only its mesh is merged), so no twin renderers are needed,
    /// unlike <see cref="FencePerformanceSwap"/>, which swaps meshes.</para>
    /// <para><b>Re-bake order.</b> <c>GroundDressingBaker</c> destroys and rebuilds <c>MapSurroundings/Vignettes</c>;
    /// after running it, re-run the haze baker or the vignette entries point at destroyed renderers (null).</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SurroundHazeSet : MonoBehaviour
    {
        /// <summary>One surround renderer and its two material sets (same length and slot order).</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("A renderer under MapSurroundings whose whole footprint is outside the fence.")]
            [SerializeField] private Renderer _renderer;
            [Tooltip("Its sharedMaterials as authored (what PC draws, and what the scene keeps on disk).")]
            [SerializeField] private Material[] _original;
            [Tooltip("The Mobile-tier copies, slot for slot (Assets/_Game/Art/Materials/Hazed/).")]
            [SerializeField] private Material[] _hazed;
            [Tooltip("Haze amount baked into the copies (HazeSettings.AmountAt at the band's representative distance). " +
                     "The surround grass is a per-texel ramp: its amount here is the ramp maximum.")]
            [SerializeField] private float _amount;
            [Tooltip("Distance band index (0 = nearest); -1 for the per-texel ramp of the surround grass.")]
            [SerializeField] private int _band;

            public Entry(Renderer renderer, Material[] original, Material[] hazed, float amount, int band)
            {
                _renderer = renderer;
                _original = original;
                _hazed = hazed;
                _amount = amount;
                _band = band;
            }

            public Renderer Renderer => _renderer;
            public Material[] Original => _original;
            public Material[] Hazed => _hazed;
            public float Amount => _amount;
            public int Band => _band;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [Tooltip("The asset-level manifest of the copies this set uses (written by the same bake).")]
        [SerializeField] private SurroundHazeMapSO _map;

        public IReadOnlyList<Entry> Entries => _entries;
        public SurroundHazeMapSO Map => _map;

#if UNITY_EDITOR
        /// <summary>Editor baker only: replaces the whole set.</summary>
        public void EditorSetEntries(List<Entry> entries, SurroundHazeMapSO map)
        {
            _entries = entries;
            _map = map;
        }
#endif
    }
}
