using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Logging;
using CluckWars.Settings;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The caster-only half of the Hide beats (D9, vfx.md 2.8): a rotating cream dashed ring (<c>Fx_DashedRing</c>, radius 0.62,
    /// order 0) under the LOCAL bird for the whole time it is faded, 0.25 lap/s (static under Reduced Motion), that blinks twice in
    /// the last 0.25 s of the Hide (a steady dip under Reduced Motion). Drawn only for a bird this peer owns (never a decoy, never
    /// a remote peer: it would mark the hidden bird), from replicated state only: <c>VisualOpacity</c> for "faded", the Hide slot's
    /// replicated active timer for the expiry. The bird itself is held at the A1 opacity floor by <see cref="ChickenVisuals"/>.
    /// The ring turns by rotating its transform and shares the Slow aura's one dashed material (no per-instance material).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class HideLocalCue : MonoBehaviour
    {
        private ChickenController _controller;
        private ChickenCombat _combat;
        private AbilityController _abilities;
        private ILogService _log;

        private LineRenderer _ring;
        private Transform _root, _ringT;
        private Vector3 _appliedParentScale = Vector3.zero;
        private bool _shown, _wiringChecked;
        private float _lastAlpha = -1f;

        [Inject]
        public void Construct(ILogService log) { _log = log; }

        /// <summary>True while the ring is drawn.</summary>
        public bool IsShown => _shown;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _abilities = GetComponent<AbilityController>();
        }

        private void OnDisable()
        {
            _shown = false;
            if (_ring != null) _ring.enabled = false;
        }

        private void Build()
        {
            var root = new GameObject("HideLocalRing");
            root.transform.SetParent(transform, worldPositionStays: false);
            _root = root.transform;

            var ring = new GameObject("HideDashedRing");
            ring.transform.SetParent(_root, worldPositionStays: false);
            ring.transform.localPosition = new Vector3(0f, FeedbackTuning.HideRingY, 0f);
            _ringT = ring.transform;
            _ring = ring.AddComponent<LineRenderer>();
            _ring.useWorldSpace = false;
            _ring.loop = true;
            int n = FeedbackTuning.HideRingSegments;
            _ring.positionCount = n;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                pts[i] = new Vector3(Mathf.Cos(a) * FeedbackTuning.SelfRingRadius, 0f, Mathf.Sin(a) * FeedbackTuning.SelfRingRadius);
            }
            _ring.SetPositions(pts);
            _ring.widthMultiplier = FeedbackTuning.HideRingWidth;
            _ring.numCapVertices = 0;
            _ring.numCornerVertices = 0;
            _ring.alignment = LineAlignment.View;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            _ring.textureMode = LineTextureMode.Tile;
            _ring.sortingOrder = FeedbackTuning.HideRingSortingOrder;
            var mat = SlowAuraFeedback.SharedDashedMaterial;
            if (mat != null) _ring.sharedMaterial = mat;
            else _log?.Error("HideLocalCue", "Shared dashed ring material is missing (SlowAuraFeedback wiring): the Hide ring will not draw.");
            _ring.enabled = false;
        }

        private void LateUpdate()
        {
            if (_controller == null) return;
            var obj = _controller.Object;
            if (obj == null || !obj.IsValid) return;

            if (!_wiringChecked)
            {
                _wiringChecked = true;
                SelfInject.Ensure(this, _log != null);
            }

            bool faded = ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity);
            bool removed = _combat != null && _combat.IsRemoved;
            bool show = HideBeatRules.ShowRing(FloatingCombatText.IsSelfPopTarget(_controller), faded, removed);

            if (!show)
            {
                if (_shown) { _shown = false; if (_ring != null) _ring.enabled = false; }
                return;
            }

            if (_ring == null) Build();
            if (_ring.sharedMaterial == null) return;

            bool reduced = PlayerPreferences.ReducedMotionEnabled;
            Vector3 p = transform.position;
            _root.position = new Vector3(p.x, 0f, p.z);
            _root.rotation = Quaternion.identity;
            NeutralizeParentScale();
            _ringT.localRotation = Quaternion.Euler(0f, HideBeatRules.RingAngle(Time.unscaledTime, reduced), 0f);

            float alpha = FeedbackTuning.HideRingAlpha * HideBeatRules.BlinkAlpha(HideRemainingSeconds(), reduced);
            if (!Mathf.Approximately(alpha, _lastAlpha))
            {
                _lastAlpha = alpha;
                Color c = FeedbackPalette.Current.CreamColor;
                c.a = alpha;
                _ring.startColor = _ring.endColor = c;
            }
            if (!_shown) { _shown = true; _ring.enabled = true; }
        }

        /// <summary>Seconds left on the running Hide slot (Invisibility or Smoke Roost), from its replicated active timer; negative when no
        /// such slot runs (the ring then stays lit).</summary>
        private float HideRemainingSeconds()
        {
            if (_abilities == null) return -1f;
            var ao = _abilities.Object;
            if (ao == null || !ao.IsValid) return -1f;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                var a = _abilities.GetSlot(i);
                if ((a is InvisibilityAbilitySO || a is SmokeRoostAbilitySO) && _abilities.IsSlotActive(i))
                    return _abilities.ActiveRemaining01For(i) * a.ResolveActiveDuration(_controller);
            }
            return -1f;
        }

        private void NeutralizeParentScale()
        {
            Vector3 ps = transform.lossyScale;
            if (ps == _appliedParentScale) return;
            _appliedParentScale = ps;
            _root.localScale = new Vector3(
                Mathf.Approximately(ps.x, 0f) ? 1f : 1f / ps.x,
                Mathf.Approximately(ps.y, 0f) ? 1f : 1f / ps.y,
                Mathf.Approximately(ps.z, 0f) ? 1f : 1f / ps.z);
        }
    }
}
