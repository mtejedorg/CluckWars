using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

// v0.3 ability feedback: ground range ring + cast-flash (see PR / STATE.md).
namespace CluckWars.Visuals
{
    /// <summary>
    /// Local, on-ground feedback for ability range and usability. Two LineRenderer
    /// rings on the flat arena floor:
    ///
    /// <list type="bullet">
    ///   <item><b>Range ring</b> (local player only): a persistent circle around the
    ///   chicken at the reach of its range-gated abilities (Peck, Cluck Shock,
    ///   Sneaky Steal). Dim while no valid target is inside; brightens + thickens
    ///   the instant an enemy enters range — the same ready/usable signal the HUD
    ///   button shows, but in the world so the player understands *why* the button
    ///   is grey.</item>
    ///   <item><b>Cast flash</b> (every peer): an expanding, fading ring at the
    ///   ability's actual radius the moment it fires, tinted to the ability accent.
    ///   Confirms the launch and shows exactly how far it reached.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Purely local VFX driven by polling networked state (<c>ActiveSlot</c>,
    /// cooldowns, rival positions) in <c>LateUpdate</c> — no RPCs, consistent with
    /// <see cref="ChickenVFX"/>. <b>Maestro</b>: add to the Chicken prefab; no
    /// Inspector wiring needed (rings are built procedurally in <c>Awake</c>).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class AbilityRangeIndicator : MonoBehaviour
    {
        private const int   Segments = 48;
        private const float GroundY  = 0.06f; // just above the y=0 arena plane (avoid z-fighting)

        [Tooltip("Seconds the cast-flash ring takes to expand and fade out.")]
        [Min(0.05f)] [SerializeField] private float _flashDuration = 0.35f;

        private ChickenController _controller;
        private AbilityController _abilities;

        private LineRenderer _ring;   // persistent range ring (local player only)
        private LineRenderer _flash;  // expanding cast-confirm ring (all peers)

        private Vector3[] _dirs;       // precomputed unit circle directions
        private Vector3[] _ringBuf;
        private Vector3[] _flashBuf;

        // Cast-flash animation state.
        private float _flashTimer = -1f;
        private float _flashTargetRadius;
        private Color _flashColor = Color.white;
        private int   _lastActiveSlot = AbilityController.InvalidSlot;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _abilities  = GetComponent<AbilityController>();

            // Precompute the unit circle once; both rings reuse the directions.
            _dirs     = new Vector3[Segments];
            _ringBuf  = new Vector3[Segments];
            _flashBuf = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float a = (i / (float)Segments) * Mathf.PI * 2f;
                _dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            var mat = BuildLineMaterial();
            _ring  = BuildRing("AbilityRangeRing", mat, 0.06f);
            _flash = BuildRing("AbilityCastFlash", mat, 0.11f);
            _ring.enabled  = false;
            _flash.enabled = false;
        }

        private void LateUpdate()
        {
            UpdateRangeRing();
            ObserveCast();
            AnimateFlash();
        }

        // ---- Persistent range ring (local player only) ------------------------

        private void UpdateRangeRing()
        {
            // Only the controlling player gets the aiming aid; rivals/bots don't.
            if (_abilities == null || _controller == null || !_controller.HasInputAuthority)
            {
                if (_ring.enabled) _ring.enabled = false;
                return;
            }

            // Pick the ability whose range to draw: prefer a ready, range-gated
            // ability that currently has a target inside (ring lights up); else the
            // widest ready range-gated ability (dim, "get closer"); else nothing.
            float bestRange = 0f;
            Color bestAccent = Color.white;
            bool  targetInRange = false;

            for (int slot = 0; slot < 3; slot++)
            {
                var ab = SlotAbility(slot);
                if (ab == null || !ab.RequiresEnemyInRange || ab.IndicatorRange <= 0f) continue;
                if (!_abilities.IsReady(slot)) continue;

                bool hasTarget = ab.IsUsable(_controller);
                // A ready ability with a target always wins; otherwise track the widest.
                if (hasTarget && !targetInRange)
                {
                    targetInRange = true;
                    bestRange = ab.IndicatorRange;
                    bestAccent = ab.AccentColor;
                }
                else if (hasTarget && ab.IndicatorRange > bestRange)
                {
                    bestRange = ab.IndicatorRange;
                    bestAccent = ab.AccentColor;
                }
                else if (!targetInRange && ab.IndicatorRange > bestRange)
                {
                    bestRange = ab.IndicatorRange;
                    bestAccent = ab.AccentColor;
                }
            }

            if (bestRange <= 0f)
            {
                if (_ring.enabled) _ring.enabled = false;
                return;
            }

            // In range → bright + thicker; out of range → dim + thin. Mirrors the
            // HUD button colour/grey state so the relationship is learnable.
            Color c = bestAccent;
            c.a = targetInRange ? 0.60f : 0.16f;
            _ring.startColor = _ring.endColor = c;
            _ring.widthMultiplier = targetInRange ? 0.10f : 0.05f;

            WriteCircle(_ring, _ringBuf, _controller.transform.position, bestRange);
            if (!_ring.enabled) _ring.enabled = true;
        }

        // ---- Cast flash (all peers) -------------------------------------------

        private void ObserveCast()
        {
            if (_abilities == null) return;

            int activeSlot = _abilities.ActiveSlot;
            if (activeSlot != AbilityController.InvalidSlot &&
                _lastActiveSlot == AbilityController.InvalidSlot)
            {
                var ability = _abilities.ActiveAbility;
                if (ability != null && ability.IndicatorRange > 0f)
                {
                    _flashTimer        = 0f;
                    _flashTargetRadius = ability.IndicatorRange;
                    _flashColor        = ability.AccentColor;
                }
            }
            _lastActiveSlot = activeSlot;
        }

        private void AnimateFlash()
        {
            if (_flashTimer < 0f) return;

            _flashTimer += Time.deltaTime;
            float t = _flashTimer / _flashDuration;
            if (t >= 1f)
            {
                _flashTimer = -1f;
                if (_flash.enabled) _flash.enabled = false;
                return;
            }

            // Ease-out expansion from 30% to full radius; alpha fades 0.7 → 0.
            float eased  = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(_flashTargetRadius * 0.3f, _flashTargetRadius, eased);
            Color c = _flashColor;
            c.a = (1f - t) * 0.7f;
            _flash.startColor = _flash.endColor = c;

            WriteCircle(_flash, _flashBuf, transform.position, radius);
            if (!_flash.enabled) _flash.enabled = true;
        }

        // ---- Helpers ----------------------------------------------------------

        private AbilityBaseSO SlotAbility(int slot) =>
            slot == 0 ? _abilities.Slot0 : (slot == 1 ? _abilities.Slot1 : _abilities.Slot2);

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
            lr.useWorldSpace   = true;     // points are absolute, so chicken rotation never warps the circle
            lr.loop            = true;
            lr.positionCount   = Segments;
            lr.widthMultiplier = width;
            lr.numCapVertices  = 0;
            lr.numCornerVertices = 0;
            lr.alignment       = LineAlignment.View; // ribbon faces camera — readable on the iso view
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows  = false;
            lr.textureMode     = LineTextureMode.Stretch;
            if (mat != null) lr.material = mat;
            return lr;
        }

        /// <summary>
        /// Unlit, vertex-coloured, transparent material for the rings. Sprites/Default
        /// is present in every project and honours per-vertex alpha, so the rings work
        /// on URP and Built-in without manual asset setup.
        /// </summary>
        private static Material BuildLineMaterial()
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            return sh != null ? new Material(sh) : null;
        }
    }
}
