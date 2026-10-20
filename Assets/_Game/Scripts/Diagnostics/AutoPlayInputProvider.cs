using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Visuals;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The auto-test's fake player (<c>cw_autoplay 1</c>, test-session item 7): one more <see cref="IInputProvider"/> in
    /// the project's <see cref="CompositeInputProvider"/>, on this peer only. It produces a stick and ability presses
    /// exactly like a device would, so they flow through <c>FusionNetworkService.OnInput</c> into the Fusion input buffer
    /// like a human's; it never writes game state. Dormant (all zero / false) until the auto-test driver activates it.
    /// </summary>
    /// <remarks>
    /// Installed only in a dev-tools build (<see cref="DevTools.Available"/>). Movement comes from
    /// <see cref="AutoPlayBrain"/>; the stick is the brain's world direction rotated into the match camera's frame
    /// (the inverse of <c>AimRules.StickToWorld</c>, which OnInput applies). Abilities: at a pile, Peck (slot 0) is
    /// pressed and released every <see cref="PeckEverySeconds"/>; elsewhere, every <see cref="CastEverySeconds"/> the
    /// next equipped non-Peck slot is pressed, held for <see cref="HoldSeconds"/> (the range preview) and released (the cast).
    /// It reports <see cref="InputDeviceKind.None"/> and a never-used <see cref="LastActiveTime"/>, so it never changes
    /// which real device the HUD and aim assist think is in hand.
    /// </remarks>
    public sealed class AutoPlayInputProvider : IInputProvider
    {
        public const float CastEverySeconds = 3f;
        public const float PeckEverySeconds = 0.8f;
        public const float HoldSeconds = 0.35f;
        private const float PeckHoldSeconds = 0.1f;
        /// <summary>An unread press is dropped after this long (a press is meant for the next input read, not later).</summary>
        public const float PressLatchSeconds = 0.25f;
        private const int MaxPiles = 64;

        private readonly AutoPlayBrain _brain = new AutoPlayBrain();
        private readonly AutoPlayPile[] _piles = new AutoPlayPile[MaxPiles];
        // A press is latched until a reader takes it (FusionNetworkService.Update latches it for OnInput) or it expires:
        // valid on one frame only, it was lost whenever the reader ran before the driver in that frame.
        private readonly bool[] _pressPending = new bool[AbilityController.SlotCount];
        private readonly float[] _pressExpires = new float[AbilityController.SlotCount];
        private readonly float[] _holdUntil = new float[AbilityController.SlotCount];
        private readonly System.Func<float> _clock;
        private Vector2 _stick;
        private float _nextCastAt, _nextPeckAt;
        private int _nextSlot = 1;
        private readonly System.Random _rng = new System.Random(20261020);

        /// <summary>True while the driver wants the fake player to play (a running round on this peer).</summary>
        public bool Active { get; private set; }

        public AutoPlayGoal Goal => _brain.Goal;

        /// <param name="clock">Seconds, unscaled (default <c>Time.unscaledTime</c>); injectable for tests.</param>
        public AutoPlayInputProvider(System.Func<float> clock = null)
        {
            _clock = clock ?? (() => Time.unscaledTime);
        }

        public void SetActive(bool active)
        {
            if (Active == active) return;
            Active = active;
            _stick = Vector2.zero;
            for (int i = 0; i < _holdUntil.Length; i++) { _holdUntil[i] = 0f; _pressPending[i] = false; }
        }

        /// <summary>Called once per rendered frame by the driver: decide the stick and the presses for this frame.</summary>
        public void Tick(ChickenController self, float now)
        {
            if (!Active || self == null) { _stick = Vector2.zero; return; }

            int np = 0;
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count && np < MaxPiles; i++)
            {
                var p = piles[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                var pos = p.transform.position;
                _piles[np++] = new AutoPlayPile { Position = new Vector2(pos.x, pos.z), Amount = p.Amount };
            }

            bool hasBase = false;
            Vector2 basePos = default;
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid || b.CornerIndex != self.HomeCornerIndex) continue;
                var bp = b.transform.position;
                basePos = new Vector2(bp.x, bp.z);
                hasBase = true;
                break;
            }

            var me = self.transform.position;
            float cargo = self.Cargo != null ? self.Cargo.Cargo : 0f;
            float capacity = self.Cargo != null ? self.Cargo.Capacity : 0f;
            var world = _brain.Tick(now, new Vector2(me.x, me.z), cargo, capacity, hasBase, basePos, BaseRadius,
                _piles, np, (float)_rng.NextDouble());

            float yaw = MatchCamera.Instance != null ? MatchCamera.Instance.CurrentYaw : 0f;
            // OnInput turns the stick into world space with StickToWorld(stick, yaw), a rotation: its inverse is the same call at -yaw.
            _stick = world.sqrMagnitude > 0.0001f ? AimRules.StickToWorld(world, -yaw) : Vector2.zero;

            ScheduleAbilities(self, now);
        }

        private float BaseRadius
        {
            get
            {
                var map = MapGeneratorCache.Get();
                return map != null ? map.BaseZoneRadius : 2.5f;
            }
        }

        private void ScheduleAbilities(ChickenController self, float now)
        {
            var abilities = self.Abilities;
            if (abilities == null) return;

            bool peckSlot = abilities.GetSlot(0) is PeckAbilitySO;
            if (peckSlot && _brain.Goal == AutoPlayGoal.AtPile)
            {
                if (now >= _nextPeckAt) { Press(0, PeckHoldSeconds); _nextPeckAt = now + PeckEverySeconds; }
                return;
            }

            if (now < _nextCastAt) return;
            _nextCastAt = now + CastEverySeconds;
            // Round-robin over the equipped slots, skipping Peck (only useful at a pile).
            for (int tries = 0; tries < AbilityController.SlotCount; tries++)
            {
                int slot = _nextSlot;
                _nextSlot = (_nextSlot + 1) % AbilityController.SlotCount;
                var ab = abilities.GetSlot(slot);
                if (ab == null || ab is PeckAbilitySO) continue;
                Press(slot, HoldSeconds);
                return;
            }
        }

        /// <summary>Presses <paramref name="slot"/> now and holds it for <paramref name="hold"/> seconds (release = cast).</summary>
        public void Press(int slot, float hold)
        {
            if (slot < 0 || slot >= _pressPending.Length) return;
            float now = _clock();
            _pressPending[slot] = true;
            _pressExpires[slot] = now + PressLatchSeconds;
            _holdUntil[slot] = now + hold;
        }

        // ---- IInputProvider ---------------------------------------------------------------------------------

        public Vector2 GetMovement() => Active ? _stick : Vector2.zero;
        public bool GetAbility1Pressed() => TakePress(0);
        public bool GetAbility2Pressed() => TakePress(1);
        public bool GetAbility3Pressed() => TakePress(2);
        public bool GetAbility4Pressed() => TakePress(3);
        public bool GetAbilityHeld(int slot) =>
            Active && slot >= 0 && slot < _holdUntil.Length && _clock() < _holdUntil[slot];
        public bool GetAbilityCancelPressed() => false;
        public bool GetBackPressed() => false;
        public bool IsAbilityCancelArmed() => false;

        /// <summary>
        /// The edge, consumed by the first read (the composite reads every provider once per caller read, and
        /// FusionNetworkService latches what it reads until OnInput), dropped once <see cref="PressLatchSeconds"/> old.
        /// Independent of whether the driver or the reader runs first in a frame.
        /// </summary>
        private bool TakePress(int slot)
        {
            if (!_pressPending[slot]) return false;
            _pressPending[slot] = false;
            return Active && _clock() <= _pressExpires[slot];
        }
    }

    /// <summary>One <c>FindFirstObjectByType</c> per Game scene instead of one per frame.</summary>
    internal static class MapGeneratorCache
    {
        private static MapGenerator _map;
        private static int _sceneHandle = -1;

        public static MapGenerator Get()
        {
            int handle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (_map == null || handle != _sceneHandle)
            {
                _sceneHandle = handle;
                _map = Object.FindFirstObjectByType<MapGenerator>();
            }
            return _map;
        }
    }
}
