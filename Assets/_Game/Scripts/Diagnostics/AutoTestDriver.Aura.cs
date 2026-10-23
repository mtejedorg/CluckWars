using System.Collections.Generic;
using System.Globalization;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// <c>cw_autoaura</c> (WP4): the remote-victim aura check. Kept in its own file so the driver's other hooks are not
    /// touched by it; the driver's <c>Update</c> calls <see cref="TickAutoAura"/> once per frame, before the fake player.
    /// </summary>
    public sealed partial class AutoTestDriver
    {
        private enum AutoAuraPhase { Idle, Armed, Chasing, Pressed, Casting, Done, Unavailable }
        private AutoAuraPhase _auraPhase = AutoAuraPhase.Idle;
        private int _auraSlot = -1;
        private string _auraAbility;
        private float _auraPhaseSince;
        private ChickenController _auraVictim;
        private readonly List<AbilityBaseSO> _auraSlots = new List<AbilityBaseSO>(AbilityController.SlotCount);

        /// <summary>
        /// Idle -> Armed when a round is playable (the aura slot is reserved from the fake player's rotation) -> Chasing at
        /// <see cref="AutoAuraRules.ChaseAtRemaining"/> s left (the stick walks at the nearest rival) -> Pressed once the
        /// rival is well inside the radius -> Casting while the slot is active (still walking into the same rival, so it
        /// stays deep inside for the probe) -> Done, until the round ends. Every outcome is logged once per round; a
        /// loadout without an Aura says so once (Warn) and the hook stays off for the run.
        /// </summary>
        private void TickAutoAura(GameManager gm)
        {
            if (!_cfg.AutoAura || _autoPlay == null || _auraPhase == AutoAuraPhase.Unavailable) return;
            if (gm == null) { EndAutoAuraRound(); return; }   // left the match: the next manager starts from Idle

            var self = LocalChicken();
            var abilities = self != null ? self.Abilities : null;
            if (!gm.IsMatchRunning || abilities == null)
            {
                if (_auraPhase != AutoAuraPhase.Idle && gm.State == MatchState.Ended) EndAutoAuraRound();
                return;
            }

            float now = Time.realtimeSinceStartup;
            switch (_auraPhase)
            {
                case AutoAuraPhase.Idle:
                    _auraSlots.Clear();
                    for (int s = 0; s < AbilityController.SlotCount; s++) _auraSlots.Add(abilities.GetSlot(s));
                    _auraSlot = AutoAuraRules.PickAuraSlot(_auraSlots);
                    if (_auraSlot < 0)
                    {
                        _auraPhase = AutoAuraPhase.Unavailable;
                        _log.Warn(Source, "AutoTest autoaura: no Aura in this loadout (Feather Aura is Speedy's); nothing is checked " +
                            "this run. Use cw_class Speedy.");
                        return;
                    }
                    _auraAbility = _auraSlots[_auraSlot].name;
                    _autoPlay.ReservedSlot = _auraSlot;
                    _auraPhase = AutoAuraPhase.Armed;
                    _log.Info(Source, $"AutoTest autoaura: slot {_auraSlot} ({_auraAbility}) reserved; one cast into a rival per round.");
                    return;

                case AutoAuraPhase.Armed:
                    if (!AutoAuraRules.ShouldChase(gm.TimeRemaining)) return;
                    _auraPhase = AutoAuraPhase.Chasing;
                    _auraPhaseSince = now;
                    return;

                case AutoAuraPhase.Chasing:
                {
                    var rival = NearestRival(self);
                    _autoPlay.SteerOverride = rival != null ? Xz(rival.transform.position) : (Vector2?)null;
                    if (now - _auraPhaseSince > AutoAuraRules.ChaseGiveUpSeconds)
                    {
                        _log.Warn(Source, Inv($"AutoTest autoaura: never got within reach of a rival in {AutoAuraRules.ChaseGiveUpSeconds:0} s; ") +
                            $"{_auraAbility} not cast this round.");
                        FinishAutoAura();
                        return;
                    }
                    if (rival == null) return;
                    float d = PlanarDistance(self.transform.position, rival.transform.position);
                    var aura = abilities.GetSlot(_auraSlot);
                    if (aura == null || !AutoAuraRules.ShouldCast(d, aura.AimRadius, abilities.IsReady(_auraSlot))) return;
                    _autoPlay.Press(_auraSlot, AutoPlayInputProvider.HoldSeconds);
                    _auraVictim = rival;
                    _auraPhase = AutoAuraPhase.Pressed;
                    _auraPhaseSince = now;
                    return;
                }

                case AutoAuraPhase.Pressed:
                    SteerAtVictim(self);
                    if (abilities.IsSlotActive(_auraSlot))
                    {
                        // merge.ps1 keys on this line: once it is logged, some peer must prove a remote victim snared.
                        _log.Info(Source, Inv($"AutoTest autoaura: cast {_auraAbility} (slot {_auraSlot}) into {VictimLabel()} at ") +
                            Inv($"d={PlanarDistance(self.transform.position, _auraVictim != null ? _auraVictim.transform.position : self.transform.position):0.0} m, ") +
                            Inv($"{gm.TimeRemaining:0.0} s left."));
                        _auraPhase = AutoAuraPhase.Casting;
                        _auraPhaseSince = now;
                    }
                    else if (now - _auraPhaseSince > AutoAuraRules.PressTimeoutSeconds)
                    {
                        _log.Warn(Source, $"AutoTest autoaura: {_auraAbility} was pressed but did not cast (stunned / rooted?); " +
                            "this round proves nothing about a remote victim.");
                        FinishAutoAura();
                    }
                    return;

                case AutoAuraPhase.Casting:
                    SteerAtVictim(self);
                    if (abilities.IsSlotActive(_auraSlot)) return;
                    _log.Info(Source, Inv($"AutoTest autoaura: {_auraAbility} ended after {now - _auraPhaseSince:0.0} s; ") +
                        "the Invariant probe's 'Invariant aura' lines carry the verdict.");
                    FinishAutoAura();
                    return;

                default:   // Done: the brain steers again until the round ends
                    return;
            }
        }

        /// <summary>This round's attempt is over: the brain steers again; the reservation holds until the round ends.</summary>
        private void FinishAutoAura()
        {
            _autoPlay.SteerOverride = null;
            _auraVictim = null;
            _auraPhase = AutoAuraPhase.Done;
        }

        private void EndAutoAuraRound()
        {
            if (_auraPhase == AutoAuraPhase.Armed)
                _log.Warn(Source, Inv($"AutoTest autoaura: the round ended before the {AutoAuraRules.ChaseAtRemaining:0} s mark; ") +
                    $"{_auraAbility} was not cast this round.");
            else if (_auraPhase == AutoAuraPhase.Chasing || _auraPhase == AutoAuraPhase.Pressed)
                _log.Warn(Source, $"AutoTest autoaura: the round ended mid-attempt ({_auraPhase}); {_auraAbility} was not cast this round.");
            if (_autoPlay != null) { _autoPlay.SteerOverride = null; _autoPlay.ReservedSlot = -1; }
            _auraVictim = null;
            _auraSlot = -1;
            _auraPhase = AutoAuraPhase.Idle;
        }

        private void SteerAtVictim(ChickenController self)
        {
            var target = _auraVictim != null && _auraVictim.Object != null && _auraVictim.Object.IsValid ? _auraVictim : NearestRival(self);
            _autoPlay.SteerOverride = target != null ? Xz(target.transform.position) : (Vector2?)null;
        }

        private string VictimLabel()
        {
            var v = _auraVictim;
            if (v == null || v.Object == null || !v.Object.IsValid) return "?";
            return v.IsBot ? "B" + v.HomeCornerIndex : "P" + v.Object.InputAuthority.PlayerId;
        }

        private static ChickenController NearestRival(ChickenController self)
        {
            ChickenController best = null;
            float bestD = float.PositiveInfinity;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == self || c.IsDecoy || c.Object == null || !c.Object.IsValid) continue;
                if (c.Combat != null && c.Combat.IsRemoved) continue;
                float d = PlanarDistance(self.transform.position, c.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static Vector2 Xz(Vector3 p) => new Vector2(p.x, p.z);

        private static string Inv(System.FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary><c>cw_autoaura</c>'s decisions (pure, tested).</summary>
    public static class AutoAuraRules
    {
        /// <summary>Playable seconds left when the chase starts: ~7 s into a 45 s round, the intro and the first rush are over.</summary>
        public const float ChaseAtRemaining = 38f;

        /// <summary>Cast with the rival this far inside the radius (0.4 x 5.4 m = 2.2 m), so it stays in the probe's deep core.</summary>
        public const float CastFraction = 0.4f;

        public const float ChaseGiveUpSeconds = 15f;

        /// <summary>A press that has not made the slot active within this did not cast.</summary>
        public const float PressTimeoutSeconds = 1.5f;

        public static bool ShouldChase(float timeRemaining) => timeRemaining <= ChaseAtRemaining;

        public static bool ShouldCast(float planarDistance, float radius, bool ready) =>
            ready && planarDistance <= radius * CastFraction;

        /// <summary>The first slot holding an Aura (<see cref="AreaEffectKind.Aura"/>: Feather Aura); -1 = none.</summary>
        public static int PickAuraSlot(IReadOnlyList<AbilityBaseSO> slots)
        {
            if (slots == null) return -1;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].AreaKind == AreaEffectKind.Aura) return i;
            return -1;
        }
    }
}
