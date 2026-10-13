using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Feeds the see-through-fence shader (<see cref="FenceFadeRules.ShaderName"/>) once per frame: where the
    /// LOCAL bird is and how strongly to open the fence in front of it (Phase 6 chunk 7f).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added by <see cref="MatchCamera"/> to its own GameObject and follows the camera's
    /// <see cref="MatchCamera.LocalChicken"/> (input authority, never a Doppelganger decoy). Purely local
    /// presentation: two <c>Shader.SetGlobalVector</c> calls a frame, no networked state, no RPC.
    /// </para>
    /// <para>
    /// The fence renderers are found by their material's shader, not by name, so the guard in
    /// <c>FenceFadeTests</c> (every fence renderer in <c>Map.unity</c> uses the fade shader) is the single
    /// place that ties "is a fence" to the art. Their bounds are cached: the map props are static. A scene
    /// without fence (the Ability Lab) is a legitimate no-op: strength stays 0 and the shader early-outs.
    /// </para>
    /// <para>
    /// Cost: up to 3 sample rays x ~84 AABB tests a frame on the CPU (microseconds); on the GPU, fence
    /// fragments only, one uniform branch when nothing is covered, else ~30 ALU + a clip.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FenceFadeDriver : MonoBehaviour
    {
        private static readonly int BirdPosId = Shader.PropertyToID("_CW_LocalBirdPos");
        private static readonly int ParamsId  = Shader.PropertyToID("_CW_FenceFadeParams");

        /// <summary>While no fence renderer is known, how often to look again (seconds, unscaled).</summary>
        private const float RescanInterval = 1f;

        private MatchCamera _matchCamera;
        private readonly List<Renderer> _fenceRenderers = new List<Renderer>();
        private readonly List<Bounds> _fenceBounds = new List<Bounds>();
        private float _nextScanTime;
        private float _strength;
        private Vector3 _lastBirdCentre;

        /// <summary>Current eased strength 0..1 (for captures / debugging).</summary>
        public float Strength => _strength;

        /// <summary>Test / capture seam: false pins the strength at 0 (the "before" look).</summary>
        public static bool EffectEnabled { get; set; } = true;

        private void Awake()
        {
            _matchCamera = GetComponent<MatchCamera>();
            Shader.SetGlobalVector(ParamsId, FenceFadeRules.ShaderParams);
        }

        private void OnEnable() => Shader.SetGlobalVector(ParamsId, FenceFadeRules.ShaderParams);

        /// <summary>Globals outlive the scene: leave the fence solid for whatever renders next (menus, lab).</summary>
        private void OnDisable()
        {
            _strength = 0f;
            Shader.SetGlobalVector(BirdPosId, Vector4.zero);
        }

        private void LateUpdate()
        {
            var cam = _matchCamera != null ? _matchCamera.Camera : null;
            var chicken = _matchCamera != null ? _matchCamera.LocalChicken : null;
            bool hasBird = chicken != null && chicken.Object != null && chicken.Object.IsValid;

            bool occluded = false;
            if (hasBird && cam != null && EffectEnabled && EnsureFence())
            {
                // The pivot sits at the capsule's mid-height, not the feet (ChickenController.AttachModel), so
                // measure the body from its CharacterController rather than assuming an offset.
                MeasureBody(chicken, out var feet, out float height);
                _lastBirdCentre = feet + Vector3.up * (0.5f * height);

                var camT = cam.transform;
                Vector3 toward = cam.orthographic ? -camT.forward : camT.position - _lastBirdCentre;
                float maxDist = Vector3.Distance(camT.position, _lastBirdCentre);
                occluded = FenceFadeRules.IsOccluded(feet, height, toward, maxDist, _fenceBounds);
            }

            _strength = FenceFadeRules.StepStrength(_strength, occluded, Time.unscaledDeltaTime);
            // While fading out after the bird is gone, keep the hole where it last was rather than at the origin.
            Shader.SetGlobalVector(BirdPosId, new Vector4(_lastBirdCentre.x, _lastBirdCentre.y, _lastBirdCentre.z, _strength));
        }

        private static void MeasureBody(Component chicken, out Vector3 feet, out float height)
        {
            var cc = chicken.GetComponent<CharacterController>();
            if (cc != null && cc.enabled)
            {
                var b = cc.bounds;
                feet = new Vector3(b.center.x, b.min.y, b.center.z);
                height = b.size.y;
                return;
            }
            feet = chicken.transform.position;
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
