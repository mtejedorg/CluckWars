using System.Collections.Generic;
using CluckWars.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Feeds the see-through-fence shader (<see cref="FenceFadeRules.ShaderName"/>) once per frame: where the
    /// LOCAL bird is and how strongly to open the fence in front of it (Phase 6 chunk 7f), plus one hole slot per
    /// other chicken covered by an OUTER fence (Phase 8 D4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added by <see cref="MatchCamera"/> to its own GameObject and follows the camera's
    /// <see cref="MatchCamera.LocalChicken"/> (input authority, never a Doppelganger decoy). Purely local
    /// presentation: a few <c>Shader.SetGlobal*</c> calls a frame (plus the global <c>_CW_FENCE_FADE</c>
    /// keyword, set only when the strongest hole crosses 0 — Phase 8 A10), no networked state, no RPC.
    /// </para>
    /// <para>
    /// <b>Local bird</b> (<c>_CW_LocalBirdPos</c>): unchanged — tested against every fence renderer; its hole opens
    /// on every fence material (outer runs, inner arm walls, base coops).
    /// </para>
    /// <para>
    /// <b>Every other chicken</b> in <see cref="ChickenController.ActiveControllers"/> — rivals and Doppelganger
    /// decoys through one identical path (nothing here reads <c>IsDecoy</c> or the owner) — is tested against the
    /// OUTER fence renderers only (material keyword <see cref="FenceFadeRules.AllBirdsKeywordName"/>), eased with
    /// the same <see cref="FenceFadeRules.FadeSeconds"/> timing toward <see cref="FenceFadeRules.TargetStrength"/>
    /// (scaled by its networked <c>VisualOpacity</c>, so a ghost's hole is as faint as the ghost), and the strongest
    /// <see cref="FenceFadeRules.MaxBirdSlots"/> are uploaded as <c>_CW_FenceBirds[]</c> + <c>_CW_FenceBirdCount</c>.
    /// Only outer-fence materials compile the slot loop, so inner arm walls and coops never open for a rival.
    /// </para>
    /// <para>
    /// The fence renderers are found by their material's shader, not by name, so the guard in
    /// <c>FenceFadeTests</c> (every fence renderer in <c>Map.unity</c> uses the fade shader) is the single
    /// place that ties "is a fence" to the art. Their bounds are cached: the map props are static. A scene
    /// without fence (the Ability Lab) is a legitimate no-op: strength stays 0 and the shader early-outs.
    /// </para>
    /// <para>
    /// Cost: the local bird's 3 sample rays x every fence AABB (~136) plus, per other chicken, 3 rays x the outer
    /// AABBs (~52): under 2 000 slab tests a frame with 8 chickens, tens of microseconds, no allocation. GPU: fence
    /// fragments only; with no hole open the keyword is off and the fence runs the stock Lit variant.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FenceFadeDriver : MonoBehaviour
    {
        private static readonly int BirdPosId = Shader.PropertyToID("_CW_LocalBirdPos");
        private static readonly int ParamsId  = Shader.PropertyToID("_CW_FenceFadeParams");
        private static readonly int RimParamsId = Shader.PropertyToID("_CW_FenceRimParams");
        private static readonly int RimColorId  = Shader.PropertyToID("_CW_FenceRimColor");
        private static readonly int BirdsId     = Shader.PropertyToID("_CW_FenceBirds");
        private static readonly int BirdCountId = Shader.PropertyToID("_CW_FenceBirdCount");

        /// <summary>While no fence renderer is known, how often to look again (seconds, unscaled).</summary>
        private const float RescanInterval = 1f;

        /// <summary>Other chickens tracked at once (3 rivals + decoys + ones still fading out after a despawn).</summary>
        private const int MaxTracked = 16;

        private MatchCamera _matchCamera;
        private readonly List<Renderer> _fenceRenderers = new List<Renderer>();
        private readonly List<Bounds> _fenceBounds = new List<Bounds>();
        private float _nextScanTime;
        private float _strength;
        private Vector3 _lastBirdCentre;
        private GlobalKeyword _fadeKeyword;
        private bool _keywordOn;

        // Phase 8 D4: outer fence occluders + per-chicken hole state (parallel fixed arrays: no per-frame GC).
        private readonly List<Bounds> _outerBounds = new List<Bounds>();
        private readonly ChickenController[] _tracked = new ChickenController[MaxTracked];
        private readonly CharacterController[] _trackedBody = new CharacterController[MaxTracked];
        private readonly float[] _trackedStrength = new float[MaxTracked];
        private readonly float[] _trackedPriority = new float[MaxTracked];
        private readonly float[] _trackedVisible = new float[MaxTracked]; // strength, or 0 while the hole is off screen
        private readonly Vector3[] _trackedCentre = new Vector3[MaxTracked];
        private readonly bool[] _trackedSeen = new bool[MaxTracked];
        private int _trackedCount;
        private readonly int[] _chosen = new int[FenceFadeRules.MaxBirdSlots];
        private readonly Vector4[] _slots = new Vector4[FenceFadeRules.MaxBirdSlots];
        private int _uploadedSlotCount = -1;
        private float _maxSlotStrength;

        /// <summary>Current eased strength 0..1 of the LOCAL bird's hole (for captures / debugging).</summary>
        public float Strength => _strength;

        /// <summary>Hole slots uploaded this frame for chickens other than the local bird (captures / debugging).</summary>
        public int SlotCount => Mathf.Max(0, _uploadedSlotCount);

        /// <summary>Strongest other-chicken hole this frame (captures / debugging).</summary>
        public float MaxSlotStrength => _maxSlotStrength;

        /// <summary>Test / capture seam: false pins the strength at 0 (the "before" look).</summary>
        public static bool EffectEnabled { get; set; } = true;

        private void Awake()
        {
            _matchCamera = GetComponent<MatchCamera>();
            _fadeKeyword = GlobalKeyword.Create(FenceFadeRules.KeywordName);
            PushParams();
        }

        private void OnEnable()
        {
            PushParams();
            // Phase 8 A10: the fade variant follows the strength; start from the keyword's real state.
            _keywordOn = Shader.IsKeywordEnabled(_fadeKeyword);
            SyncKeyword();
        }

        /// <summary>Globals outlive the scene: leave the fence solid (and on the plain Lit variant) for whatever
        /// renders next (menus, lab).</summary>
        private void OnDisable()
        {
            _strength = 0f;
            Shader.SetGlobalVector(BirdPosId, Vector4.zero);
            for (int i = 0; i < _trackedCount; i++) { _tracked[i] = null; _trackedBody[i] = null; }
            _trackedCount = 0;
            _maxSlotStrength = 0f;
            _uploadedSlotCount = -1; // force the zero upload
            UploadSlots(0);
            SyncKeyword();
        }

        private static void PushParams()
        {
            Shader.SetGlobalVector(ParamsId, FenceFadeRules.ShaderParams);
            Shader.SetGlobalVector(RimParamsId, FenceFadeRules.RimParams);
            Shader.SetGlobalColor(RimColorId, FenceFadeRules.RimColor);
        }

        /// <summary>Sets the global <c>_CW_FENCE_FADE</c> keyword only when the strongest hole (local or any slot)
        /// crosses 0 (no per-frame churn).</summary>
        private void SyncKeyword()
        {
            if (!FenceFadeRules.KeywordToggle(_keywordOn, Mathf.Max(_strength, _maxSlotStrength), out bool wantOn)) return;
            Shader.SetKeyword(_fadeKeyword, wantOn);
            _keywordOn = wantOn;
        }

        private void LateUpdate()
        {
            var cam = _matchCamera != null ? _matchCamera.Camera : null;
            var chicken = _matchCamera != null ? _matchCamera.LocalChicken : null;
            bool hasBird = chicken != null && chicken.Object != null && chicken.Object.IsValid;
            bool active = cam != null && EffectEnabled && EnsureFence();
            float dt = Time.unscaledDeltaTime;

            bool occluded = false;
            if (hasBird && active)
            {
                // The pivot sits at the capsule's mid-height, not the feet (ChickenController.AttachModel), so
                // measure the body from its CharacterController rather than assuming an offset.
                MeasureBody(chicken.GetComponent<CharacterController>(), chicken.transform, out var feet, out float height);
                _lastBirdCentre = feet + Vector3.up * (0.5f * height);
                occluded = IsCovered(cam, feet, height, _lastBirdCentre, _fenceBounds);
            }

            _strength = FenceFadeRules.StepStrength(_strength, occluded, dt);
            // While fading out after the bird is gone, keep the hole where it last was rather than at the origin.
            Shader.SetGlobalVector(BirdPosId, new Vector4(_lastBirdCentre.x, _lastBirdCentre.y, _lastBirdCentre.z, _strength));

            UpdateOtherBirds(cam, chicken, active, dt);
            SyncKeyword();
        }

        private static bool IsCovered(Camera cam, Vector3 feet, float height, Vector3 centre, IReadOnlyList<Bounds> occluders)
        {
            var camT = cam.transform;
            Vector3 toward = cam.orthographic ? -camT.forward : camT.position - centre;
            float maxDist = Vector3.Distance(camT.position, centre);
            return FenceFadeRules.IsOccluded(feet, height, toward, maxDist, occluders);
        }

        /// <summary>
        /// Phase 8 D4: every chicken except the local bird — rivals and decoys through the identical path — gets a
        /// smoothed strength from the OUTER fence test; the strongest <see cref="FenceFadeRules.MaxBirdSlots"/> go
        /// to the shader.
        /// </summary>
        private void UpdateOtherBirds(Camera cam, ChickenController local, bool active, float dt)
        {
            for (int i = 0; i < _trackedCount; i++) _trackedSeen[i] = false;

            var all = ChickenController.ActiveControllers;
            for (int c = 0; c < all.Count; c++)
            {
                var other = all[c];
                if (other == null || ReferenceEquals(other, local) || other.Object == null || !other.Object.IsValid) continue;
                int slot = IndexOfTracked(other);
                if (slot < 0)
                {
                    if (_trackedCount >= MaxTracked) continue; // more live chickens than any match spawns
                    slot = _trackedCount++;
                    _tracked[slot] = other;
                    _trackedBody[slot] = other.GetComponent<CharacterController>();
                    _trackedStrength[slot] = 0f;
                }
                _trackedSeen[slot] = true;

                bool covered = false;
                if (active && _outerBounds.Count > 0)
                {
                    MeasureBody(_trackedBody[slot], other.transform, out var feet, out float height);
                    _trackedCentre[slot] = feet + Vector3.up * (0.5f * height);
                    covered = IsCovered(cam, feet, height, _trackedCentre[slot], _outerBounds);
                }
                float target = FenceFadeRules.TargetStrength(covered, other.VisualOpacity);
                _trackedStrength[slot] = FenceFadeRules.StepStrengthToward(_trackedStrength[slot], target, dt);
            }

            // Gone (despawned / destroyed): fade out where it was last seen, then forget it.
            for (int i = _trackedCount - 1; i >= 0; i--)
            {
                if (_trackedSeen[i]) continue;
                _trackedStrength[i] = FenceFadeRules.StepStrengthToward(_trackedStrength[i], 0f, dt);
                if (_trackedStrength[i] <= 0f) RemoveTracked(i);
            }

            // Only holes that can touch the screen compete for slots and keep the fade variant on.
            _maxSlotStrength = 0f;
            for (int i = 0; i < _trackedCount; i++)
            {
                var vp = cam != null ? cam.WorldToViewportPoint(_trackedCentre[i]) : new Vector3(0.5f, 0.5f, 1f);
                float dx = vp.x - 0.5f, dy = vp.y - 0.5f;
                _trackedPriority[i] = dx * dx + dy * dy;
                _trackedVisible[i] = FenceFadeRules.HoleOnScreen(vp) ? _trackedStrength[i] : 0f;
                if (_trackedVisible[i] > _maxSlotStrength) _maxSlotStrength = _trackedVisible[i];
            }

            int n = FenceFadeRules.SelectSlots(_trackedVisible, _trackedPriority, _trackedCount, _chosen,
                FenceFadeRules.MaxBirdSlots);
            for (int k = 0; k < n; k++)
            {
                int i = _chosen[k];
                var p = _trackedCentre[i];
                _slots[k] = new Vector4(p.x, p.y, p.z, _trackedStrength[i]);
            }
            UploadSlots(n);
        }

        private void UploadSlots(int count)
        {
            // Nothing to show and nothing shown last frame: skip the uploads (the common case).
            if (count == 0 && _uploadedSlotCount == 0) return;
            for (int k = count; k < _slots.Length; k++) _slots[k] = Vector4.zero;
            // Always the full fixed-size array: the first SetGlobalVectorArray call fixes the global's length.
            Shader.SetGlobalVectorArray(BirdsId, _slots);
            Shader.SetGlobalFloat(BirdCountId, count);
            _uploadedSlotCount = count;
        }

        private int IndexOfTracked(ChickenController c)
        {
            for (int i = 0; i < _trackedCount; i++) if (ReferenceEquals(_tracked[i], c)) return i;
            return -1;
        }

        private void RemoveTracked(int i)
        {
            int last = --_trackedCount;
            _tracked[i] = _tracked[last];
            _trackedBody[i] = _trackedBody[last];
            _trackedStrength[i] = _trackedStrength[last];
            _trackedPriority[i] = _trackedPriority[last];
            _trackedCentre[i] = _trackedCentre[last];
            _trackedSeen[i] = _trackedSeen[last];
            _tracked[last] = null;
            _trackedBody[last] = null;
        }

        private static void MeasureBody(CharacterController cc, Transform fallback, out Vector3 feet, out float height)
        {
            if (cc != null && cc.enabled)
            {
                var b = cc.bounds;
                feet = new Vector3(b.center.x, b.min.y, b.center.z);
                height = b.size.y;
                return;
            }
            feet = fallback.position;
            height = FenceFadeRules.FallbackBodyHeight;
        }

        /// <summary>
        /// Makes sure the fence renderer cache is populated and alive. A destroyed entry means the map was
        /// unloaded (a scene change), so the cache is dropped and rebuilt on the next scan.
        /// </summary>
        private bool EnsureFence()
        {
            if (_fenceRenderers.Count > 0)
            {
                if (_fenceRenderers[0] != null) return true;
                _fenceRenderers.Clear();
                _fenceBounds.Clear();
                _outerBounds.Clear();
            }

            if (Time.unscaledTime < _nextScanTime) return false;
            _nextScanTime = Time.unscaledTime + RescanInterval;

            var all = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (!r.enabled || !UsesFenceShader(r)) continue;
                _fenceRenderers.Add(r);
                _fenceBounds.Add(r.bounds);
                if (FenceFadeRules.FadesForAllBirds(r.sharedMaterial)) _outerBounds.Add(r.bounds);
            }
            return _fenceRenderers.Count > 0;
        }

        private static bool UsesFenceShader(Renderer r)
        {
            var m = r.sharedMaterial;
            return m != null && m.shader != null && m.shader.name == FenceFadeRules.ShaderName;
        }
    }
}
