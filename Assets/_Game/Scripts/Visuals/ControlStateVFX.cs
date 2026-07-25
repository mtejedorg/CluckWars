using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Makes the four control states (GDD §6.4) visibly readable on every affected
    /// chicken — the single biggest "abilities do nothing visible" gap. A coloured
    /// ground ring marks the dominant active state, and a one-shot white shockwave
    /// fires the instant a knockback lands:
    ///
    /// <list type="bullet">
    ///   <item><b>Stunned</b> → pulsing yellow ring (on top of the existing stun orbit).</item>
    ///   <item><b>Rooted</b> → green ring (you can see why they're stuck).</item>
    ///   <item><b>Slowed</b> → cyan ring (collision / pile / ability slow).</item>
    ///   <item><b>Knocked back</b> → white shockwave burst at the moment of impact.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Driven entirely by <b>replicated</b> state so it's correct on every peer:
    /// <c>ChickenController.ControlFlags</c> (Slowed/Rooted), <c>KnockbackEventId</c>
    /// (the one-shot knockback event), and the already-networked <c>IsStunned</c>.
    /// The particles / LineRenderers themselves are 100% local — only those compact
    /// triggers cross the wire. Same LineRenderer pattern as
    /// <see cref="AbilityRangeIndicator"/>.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ControlStateVFX : MonoBehaviour
    {
        private const int   Segments = 40;
        private const float GroundY  = 0.05f;
        private const float RingRadius = 0.62f;

        private static readonly Color StunColor = new Color(1.00f, 0.85f, 0.20f, 1f);
        private static readonly Color RootColor = new Color(0.35f, 0.90f, 0.25f, 1f);
        private static readonly Color SlowColor = new Color(0.30f, 0.75f, 1.00f, 1f);
        private static readonly Color KnockColor = new Color(1f, 1f, 1f, 1f);

        private ChickenController _controller;
        private ChickenCombat     _combat;

        private LineRenderer _ring;       // persistent status ring
        private LineRenderer _knockFlash; // one-shot knockback shockwave

        private Vector3[] _dirs;
        private Vector3[] _ringBuf;
        private Vector3[] _flashBuf;

        private float _knockTimer = -1f;
        private const float KnockDuration = 0.3f;
        private byte  _lastKnockEventId;
        private bool  _knockInitialized;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();

            _dirs     = new Vector3[Segments];
            _ringBuf  = new Vector3[Segments];
            _flashBuf = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float a = (i / (float)Segments) * Mathf.PI * 2f;
                _dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            var mat = BuildLineMaterial();
            _ring       = BuildRing("ControlStateRing", mat, 0.07f);
            _knockFlash = BuildRing("KnockbackFlash", mat, 0.12f);
            _ring.enabled = false;
            _knockFlash.enabled = false;
        }

        private void LateUpdate()
        {
            if (_controller == null) return;
            UpdateStatusRing();
            UpdateKnockback();
        }

        private void UpdateStatusRing()
        {
            // Priority: stun > root > slow (most-incapacitating wins the ring colour).
            // All three read replicated state, so the ring is correct on every peer.
            var flags    = _controller.ControlFlags;
            bool stunned = _combat != null && _combat.IsRemoved;
            bool rooted  = (flags & ControlVfx.Rooted) != 0;
            bool slowed  = (flags & ControlVfx.Slowed) != 0;

            Color c;
            if (stunned)      c = StunColor;
            else if (rooted)  c = RootColor;
            else if (slowed)  c = SlowColor;
            else { if (_ring.enabled) _ring.enabled = false; return; }

            // Gentle pulse so it reads as "active effect", not a static decal.
            float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 9f);
            c.a = pulse;
            _ring.startColor = _ring.endColor = c;

            WriteCircle(_ring, _ringBuf, _controller.transform.position, RingRadius);
            if (!_ring.enabled) _ring.enabled = true;
        }

        private void UpdateKnockback()
        {
            // Replicated one-shot: a changed KnockbackEventId means a knockback was
            // applied (on any peer). Fire the shockwave once. Seed the baseline on the
            // first frame so a non-zero starting id (late join) doesn't false-trigger.
            byte id = _controller.KnockbackEventId;
            if (!_knockInitialized)
            {
                _knockInitialized = true;
                _lastKnockEventId = id;
            }
            else if (id != _lastKnockEventId)
            {
                _lastKnockEventId = id;
                _knockTimer = 0f;
            }

            if (_knockTimer < 0f) return;

            _knockTimer += Time.deltaTime;
            float t = _knockTimer / KnockDuration;
            if (t >= 1f)
            {
                _knockTimer = -1f;
                if (_knockFlash.enabled) _knockFlash.enabled = false;
                return;
            }

            float eased  = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0.3f, 1.3f, eased);
            Color c = KnockColor; c.a = (1f - t) * 0.8f;
            _knockFlash.startColor = _knockFlash.endColor = c;
            WriteCircle(_knockFlash, _flashBuf, transform.position, radius);
            if (!_knockFlash.enabled) _knockFlash.enabled = true;
        }

        // ---- Helpers (mirror AbilityRangeIndicator) ---------------------------

        private void WriteCircle(LineRenderer lr, Vector3[] buf, Vector3 center, float radius)
        {
            center.y = GroundY;
            for (int i = 0; i < Segments; i++)
                buf[i] = center + _dirs[i] * radius;
            lr.SetPositions(buf);
        }

        private LineRenderer BuildRing(string goName, Material mat, float width)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.positionCount     = Segments;
            lr.widthMultiplier   = width;
            lr.numCapVertices    = 0;
            lr.numCornerVertices = 0;
            lr.alignment         = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.textureMode       = LineTextureMode.Stretch;
            if (mat != null) lr.material = mat;
            return lr;
        }

        private static Material BuildLineMaterial()
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            return sh != null ? new Material(sh) : null;
        }
    }
}
