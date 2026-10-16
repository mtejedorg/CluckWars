using System.Collections.Generic;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Phase 8 E2: the see-through fence's three FIXED targets for the local player — slot 0 the own deposit pad, slots
    /// 1-2 the two home-side piles — owned and fed by <see cref="FenceFadeDriver"/> next to the chicken holes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A target opens only while an OUTER fence covers it (<see cref="FenceFadeRules.IsTargetOccluded"/>, the same ray
    /// test as a chicken, centre + near-rim probe) AND the local chicken carries food, is within
    /// <see cref="FenceFadeRules.FixedTargetNearMetres"/> of it, or the round is in GET READY
    /// (<see cref="FenceFadeRules.FixedTargetWanted"/>). Strengths ease at <see cref="FenceFadeRules.FadeSeconds"/> both ways
    /// (<see cref="FenceFadeRules.StepStrengthToward"/>, the chicken holes' rate). Each target has its own radius
    /// (<see cref="FenceFadeRules.TargetRadiusShift"/>: the pad's ring and a pile's footprint are bigger than a chicken).
    /// </para>
    /// <para>
    /// They feed their OWN shader array (<c>_CW_FenceTargets</c>), so a four-player match's eight chicken slots can never
    /// starve them or be starved by them, and they are read only by outer-fence materials (<c>_CW_FENCE_ALL_BIRDS</c>): the
    /// inner arm walls and the coops stay local-bird-only. Local presentation only: no networked state is written, no RPC.
    /// </para>
    /// <para>
    /// Cost per frame: nothing while no target is in range (the occlusion rays are skipped); with all three in range,
    /// 3 targets x 2 probes x 3 rays x ~52 outer AABBs, a few hundred slab tests, no allocation.
    /// </para>
    /// </remarks>
    internal sealed class FenceFixedTargets
    {
        private static readonly int TargetsId = Shader.PropertyToID("_CW_FenceTargets");
        private static readonly int ShiftId = Shader.PropertyToID("_CW_FenceTargetShift");
        private static readonly int CountId = Shader.PropertyToID("_CW_FenceTargetCount");

        private const int Max = FenceFadeRules.MaxFixedTargets;
        private const int PadSlot = 0;

        private readonly float[] _strength = new float[Max];
        private readonly float[] _shift = new float[Max];
        private readonly Vector3[] _holeCentre = new Vector3[Max];
        private readonly Vector4[] _slots = new Vector4[Max];

        // Scratch for choosing the home-side piles (reused: no per-frame allocation).
        private readonly List<Vector3> _pilePositions = new List<Vector3>(16);
        private readonly List<bool> _pileIsCentre = new List<bool>(16);
        private readonly List<FoodPile> _pileRefs = new List<FoodPile>(16);
        private readonly int[] _home = new int[FenceFadeRules.HomePileCount];

        private ChickenController _cargoOwner;
        private ChickenCargo _cargo;
        private Vector4 _shiftVector;
        private int _uploaded = -1;

        /// <summary>Targets uploaded this frame (captures / debugging).</summary>
        public int Count => Mathf.Max(0, _uploaded);

        /// <summary>Strongest on-screen target hole this frame; keeps the fade keyword on.</summary>
        public float MaxVisibleStrength { get; private set; }

        /// <summary>Eased strength of target <paramref name="slot"/> (0 pad, 1-2 home piles); 0 outside the range.</summary>
        public float StrengthOf(int slot) => slot >= 0 && slot < Max ? _strength[slot] : 0f;

        /// <summary>
        /// Advances the three targets one frame. <paramref name="local"/> null or <paramref name="active"/> false (no fence,
        /// effect off) ease every hole out where it last was.
        /// </summary>
        public void Update(Camera cam, ChickenController local, bool active, IReadOnlyList<Bounds> outer, float dt)
        {
            bool live = active && cam != null && local != null && local.Object != null && local.Object.IsValid;
            var wanted = default(Wanted);
            if (live) wanted = Resolve(cam, local, outer);

            MaxVisibleStrength = 0f;
            _shiftVector = Vector4.zero;
            int n = 0;
            for (int t = 0; t < Max; t++)
            {
                _strength[t] = FenceFadeRules.StepStrengthToward(_strength[t], wanted.Get(t) ? 1f : 0f, dt);
                if (_strength[t] <= 0f || cam == null) continue;
                var vp = cam.WorldToViewportPoint(_holeCentre[t]);
                if (!FenceFadeRules.TargetHoleOnScreen(vp, _shift[t], cam.aspect)) continue;
                if (_strength[t] > MaxVisibleStrength) MaxVisibleStrength = _strength[t];
                var c = _holeCentre[t];
                _slots[n] = new Vector4(c.x, c.y, c.z, _strength[t]);
                _shiftVector[n] = _shift[t];
                n++;
            }
            Upload(n);
        }

        /// <summary>Zeroes every target and the shader globals (the driver is disabled / the match ends).</summary>
        public void Clear()
        {
            for (int t = 0; t < Max; t++) _strength[t] = 0f;
            _cargoOwner = null;
            _cargo = null;
            MaxVisibleStrength = 0f;
            _uploaded = -1; // force the zero upload
            Upload(0);
        }

        private struct Wanted
        {
            private byte _bits;
            public void Set(int t) => _bits |= (byte)(1 << t);
            public bool Get(int t) => (_bits & (1 << t)) != 0;
        }

        private Wanted Resolve(Camera cam, ChickenController local, IReadOnlyList<Bounds> outer)
        {
            var wanted = default(Wanted);
            var pad = FindOwnPad(local);
            if (pad == null || outer == null || outer.Count == 0) return wanted;

            bool carrying = IsCarrying(local);
            bool getReady = YouMarkerRules.IsGetReadyNow();
            var localPos = local.transform.position;
            var padPos = pad.transform.position;

            // Slot 0: the own deposit pad (a flat disc of DepositRadius, plus its ring).
            Evaluate(PadSlot, ref wanted, cam, localPos, padPos, padPos + Vector3.up * 0.05f,
                pad.DepositRadius, pad.DepositRadius, FenceFadeRules.PadProbeHeight, carrying, getReady, outer, true);

            // Slots 1-2: the home-side piles that still hold food.
            CollectPiles();
            int homes = FenceFadeRules.PickHomePiles(padPos, _pilePositions, _pileIsCentre, _home);
            for (int k = 0; k < FenceFadeRules.HomePileCount; k++)
            {
                int slot = PadSlot + 1 + k;
                var pile = k < homes ? _pileRefs[_home[k]] : null;
                if (pile == null) continue;
                var fp = pile.CurrentFootprint;
                var pos = pile.transform.position;
                var toward = cam.orthographic ? -cam.transform.forward : cam.transform.position - pos;
                float rimRadius = PileFootprintShape.RadiusToward(new Vector2(toward.x, toward.z), pile.transform.eulerAngles.y, fp);
                Evaluate(slot, ref wanted, cam, localPos, pos, pos + Vector3.up * (0.5f * FenceFadeRules.PileProbeHeight),
                    0.5f * Mathf.Max(fp.x, fp.y), rimRadius, FenceFadeRules.PileProbeHeight, carrying, getReady, outer, pile.HasCollectableFood);
            }
            return wanted;
        }

        private void Evaluate(int slot, ref Wanted wanted, Camera cam, Vector3 localPos, Vector3 ground, Vector3 holeCentre,
            float holeRadius, float probeRadius, float probeHeight, bool carrying, bool getReady, IReadOnlyList<Bounds> outer, bool eligible)
        {
            _holeCentre[slot] = holeCentre;
            float dx = localPos.x - ground.x, dz = localPos.z - ground.z;
            if (eligible && FenceFadeRules.FixedTargetInRange(carrying, Mathf.Sqrt(dx * dx + dz * dz), getReady))
            {
                var camT = cam.transform;
                Vector3 toward = cam.orthographic ? -camT.forward : camT.position - ground;
                float maxDist = Vector3.Distance(camT.position, holeCentre);
                if (FenceFadeRules.IsTargetOccluded(ground, probeRadius, probeHeight, toward, maxDist, outer)) wanted.Set(slot);
            }
            float viewHeight = cam.orthographic
                ? 2f * cam.orthographicSize
                : 2f * Vector3.Distance(cam.transform.position, holeCentre) * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
            _shift[slot] = FenceFadeRules.TargetRadiusShift(holeRadius, viewHeight);
        }

        /// <summary>The base this chicken owns: matched by the chicken's stamped home corner (what deposits use).</summary>
        private static PlayerBase FindOwnPad(ChickenController local)
        {
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b != null && BaseDepositRules.IsAllegianceMatch(local.HomeCornerIndex, b.CornerIndex)) return b;
            }
            return null;
        }

        private void CollectPiles()
        {
            _pilePositions.Clear();
            _pileIsCentre.Clear();
            _pileRefs.Clear();
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null) continue;
                _pilePositions.Add(p.transform.position);
                _pileIsCentre.Add(p.IsCentre);
                _pileRefs.Add(p);
            }
        }

        private bool IsCarrying(ChickenController local)
        {
            if (!ReferenceEquals(_cargoOwner, local))
            {
                _cargoOwner = local;
                _cargo = local.GetComponent<ChickenCargo>();
            }
            return _cargo != null && _cargo.Object != null && _cargo.Object.IsValid && _cargo.Cargo > 0f;
        }

        private void Upload(int count)
        {
            // Nothing live and nothing uploaded last frame: skip the globals (the common case).
            if (count == 0 && _uploaded == 0) return;
            for (int k = count; k < _slots.Length; k++) _slots[k] = Vector4.zero;
            if (count == 0) _shiftVector = Vector4.zero;
            // Always the full fixed-size array: the first SetGlobalVectorArray call fixes the global's length.
            Shader.SetGlobalVectorArray(TargetsId, _slots);
            Shader.SetGlobalVector(ShiftId, _shiftVector);
            Shader.SetGlobalFloat(CountId, count);
            _uploaded = count;
        }
    }
}
