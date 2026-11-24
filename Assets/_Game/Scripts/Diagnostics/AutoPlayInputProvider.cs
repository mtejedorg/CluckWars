using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Visuals;
using Fusion;
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
    /// Installed only in a dev-tools build (<see cref="DevTools.Available"/>). Decisions come from
    /// <see cref="AutoPlayBrain"/> (forage -> deposit at the OWN base -> repeat, hunts for aggressive personalities; the
    /// personality is seeded from the local PlayerId by <see cref="Configure"/>). The stick is the brain's world direction
    /// rotated into the match camera's frame (the inverse of <c>AimRules.StickToWorld</c>, which OnInput applies).
    /// Abilities: standing at a pile, Peck (slot 0) is pressed and released every <see cref="PeckEverySeconds"/>; a hunt
    /// that reaches its rival presses the first Steal / Control move; otherwise every <see cref="CastEverySeconds"/> the
    /// next equipped non-Peck move the personality allows is pressed, held for <see cref="HoldSeconds"/> (the range
    /// preview) and released (the cast). It reports <see cref="InputDeviceKind.None"/> and a never-used
    /// <see cref="LastActiveTime"/>, so it never changes which real device the HUD and aim assist think is in hand.
    /// </remarks>
    public sealed class AutoPlayInputProvider : IInputProvider
    {
        public const float CastEverySeconds = 3f;
        public const float PeckEverySeconds = 0.8f;
        public const float HoldSeconds = 0.35f;
        private const float PeckHoldSeconds = 0.1f;
        /// <summary>An unread press is dropped after this long (a press is meant for the next input read, not later).</summary>
        public const float PressLatchSeconds = 0.25f;
        private const int MaxPiles = 64, MaxRivals = 16;
        /// <summary>
        /// The decision step, in SIMULATED seconds (two ticks at the 32 Hz tick rate): the stick, the presses and the brain
        /// run once per step whatever the frame rate or <c>cw_timescale</c> (BL-208), so a time scale changes how fast the
        /// match runs, not what the bird decides per second of it.
        /// </summary>
        public const float DecisionStepSeconds = 0.0625f;
        /// <summary>After a fighter press, the shortest gap before the next one (the hold plus the cast settling).</summary>
        public const float FighterPressGapSeconds = 0.3f;

        private AutoPlayBrain _brain = new AutoPlayBrain(AutoPlayPersonality.Balanced);
        private readonly AutoPlayPile[] _piles = new AutoPlayPile[MaxPiles];
        private readonly AutoPlayRival[] _rivals = new AutoPlayRival[MaxRivals];
        // A press is latched until a reader takes it (FusionNetworkService.Update latches it for OnInput) or it expires:
        // valid on one frame only, it was lost whenever the reader ran before the driver in that frame.
        private readonly bool[] _pressPending = new bool[AbilityController.SlotCount];
        private readonly float[] _pressExpires = new float[AbilityController.SlotCount];
        private readonly float[] _holdUntil = new float[AbilityController.SlotCount];
        private readonly System.Func<float> _clock, _frameSeconds;
        private Vector2 _stick;
        private float _nextCastAt, _nextPeckAt;
        private int _nextSlot = 1;
        private readonly System.Random _rng = new System.Random(20261020);
        private TickCadence _cadence;
        // The fighter personality scratch (FighterTactics): rivals with the extra facts, the equipped attacks.
        private readonly FighterRival[] _fRivals = new FighterRival[MaxRivals];
        private readonly FighterAttack[] _fAttacks = new FighterAttack[AbilityController.SlotCount];
        private float _fighterNextPressAt;
        private bool _chasing;

        /// <summary>True while the driver wants the fake player to play (a running round on this peer).</summary>
        public bool Active { get; private set; }

        /// <summary>
        /// A slot the fake player never casts by itself (-1 = none): <c>cw_autozone</c> keeps its zone ability off cooldown
        /// for the one cast it makes near the round end (<see cref="Press"/> still works for it).
        /// </summary>
        public int ReservedSlot { get; set; } = -1;

        /// <summary>
        /// <c>cw_autoaura</c> (WP4): while set, the stick walks straight at this world XZ point instead of following the
        /// brain (the driver points it at the rival the aura is meant for). Null = the brain steers, as always.
        /// </summary>
        public Vector2? SteerOverride { get; set; }

        public AutoPlayGoal Goal => _brain.Goal;
        public AutoPlayState State => _brain.State;
        public AutoPlayPersonality Personality => _brain.Personality;

        /// <summary>Banked food of the own base seen on the last <see cref="Tick"/>; negative when no own base was found.</summary>
        public float OwnBaseFood { get; private set; } = -1f;

        /// <summary>Cargo of the local chicken seen on the last <see cref="Tick"/>.</summary>
        public float Cargo { get; private set; }

        /// <summary>World XZ of the local chicken on the last <see cref="Tick"/>.</summary>
        public Vector2 Position { get; private set; }

        public float GoalDistance => _brain.GoalDistance;
        public int StuckTicks => _brain.StuckTicks;
        public int Unsticks => _brain.Unsticks;
        public bool CanForage { get; private set; }
        /// <summary>Peck presses made this round (a forager standing at a pile should rack these up).</summary>
        public int PeckPresses { get; private set; }

        /// <param name="clock">Seconds of SIMULATED time (default <see cref="SimClock.Now()"/>: the runner simulation time, so
        /// holds and press latches follow <c>cw_timescale</c>); injectable for tests.</param>
        /// <param name="frameSeconds">Simulated seconds one rendered frame spans (default <c>Time.deltaTime</c>, which is scaled);
        /// with an injected clock and no value it is 0, so a test controls time through the clock alone.</param>
        public AutoPlayInputProvider(System.Func<float> clock = null, System.Func<float> frameSeconds = null)
        {
            _clock = clock ?? SimClock.Now;
            _frameSeconds = frameSeconds ?? (clock == null ? (System.Func<float>)(() => Time.deltaTime) : () => 0f);
        }

        /// <summary>
        /// Picks the personality for this client: <paramref name="forced"/> (<c>cw_personality</c>) when given, else
        /// <see cref="AutoPlayPersonality.For"/> of its PlayerId.
        /// </summary>
        public void Configure(int playerId, AutoPlayPersonality? forced = null)
        {
            _brain = new AutoPlayBrain(forced ?? AutoPlayPersonality.For(playerId));
            PeckPresses = 0;
            _fighterNextPressAt = 0f;
            _cadence.Reset();
        }

        public void SetActive(bool active)
        {
            if (Active == active) return;
            Active = active;
            _stick = Vector2.zero;
            for (int i = 0; i < _holdUntil.Length; i++) { _holdUntil[i] = 0f; _pressPending[i] = false; }
            _cadence.Reset();
            _chasing = false;
        }

        /// <summary>Called once per rendered frame by the driver: decide the stick and the presses for this frame.</summary>
        public void Tick(ChickenController self, float now)
        {
            if (!Active || self == null || self.Object == null || !self.Object.IsValid) { _stick = Vector2.zero; return; }
            // One decision per step of simulated time: the stick and presses of the last decision stand in between.
            if (!_cadence.TryAdvance(now, DecisionStepSeconds, out now)) return;

            var mine3 = self.transform.position;
            var view = new AutoPlayView { Piles = _piles, Rivals = _rivals };
            var abilities = self.Abilities;
            // Foraging needs a Peck slot (slot 0); the Assassin has none and hunts instead.
            view.CanForage = abilities != null && abilities.GetSlot(0).Is(AbilityJob.Peck);

            // Piles are solid blockers with an elliptical footprint: walk to the rim point (what the bots do) and count
            // as "at the pile" with Peck's own surface-relative reach test - never the centre (unreachable).
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count && view.PileCount < MaxPiles; i++)
            {
                var p = piles[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                var pos = p.transform.position;
                var approach = p.SurfaceApproachPoint(mine3, PileStandoff);
                _piles[view.PileCount++] = new AutoPlayPile
                {
                    Position = new Vector2(pos.x, pos.z),
                    Amount = p.Amount,
                    HasApproach = true,
                    Approach = new Vector2(approach.x, approach.z),
                    InReach = p.IsWithinCollectRange(mine3),
                };
            }

            // The OWN base: the one at this chicken's home corner (the deposit rule: BaseDepositRules allegiance),
            // else the one this player owns.
            var me = self.Object.InputAuthority;
            PlayerBase own = null;
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) continue;
                if (b.CornerIndex == self.HomeCornerIndex) { own = b; break; }
                if (own == null && b.Owner == me) own = b;
            }
            if (own != null)
            {
                var bp = own.transform.position;
                view.HasBase = true;
                view.BasePos = new Vector2(bp.x, bp.z);
                view.BaseRadius = own.DepositRadius;
                OwnBaseFood = own.FoodTotal;
            }
            else OwnBaseFood = -1f;

            bool fights = _brain.Personality.Fights;
            var execute = fights ? self.GetComponent<AssassinExecute>() : null;
            NetworkBehaviourId marked = execute != null ? execute.MarkedTarget : NetworkBehaviourId.None;
            var loot = fights ? self.Loot : null;
            NetworkBehaviourId lootMarked = loot != null && loot.MarkLive ? loot.MarkedTarget : NetworkBehaviourId.None;
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count && view.RivalCount < MaxRivals; i++)
            {
                var c = chickens[i];
                if (c == null || c == self || c.IsDecoy || c.Object == null || !c.Object.IsValid) continue;
                var cp = c.transform.position;
                if (fights) _fRivals[view.RivalCount] = FighterFacts(c, mine3, marked, lootMarked);
                _rivals[view.RivalCount++] = new AutoPlayRival
                {
                    Position = new Vector2(cp.x, cp.z),
                    Cargo = c.Cargo != null ? c.Cargo.Cargo : 0f,
                };
            }

            view.Self = new Vector2(mine3.x, mine3.z);
            Position = view.Self;
            Cargo = view.Cargo = self.Cargo != null ? self.Cargo.Cargo : 0f;
            CanForage = view.CanForage;
            view.Capacity = self.Cargo != null ? self.Cargo.Capacity : 0f;
            var world = _brain.Tick(now, view, (float)_rng.NextDouble());
            _chasing = false;
            if (fights)
            {
                var decision = FighterChoose(self, execute, view);
                if (decision.Slot >= 0 && now >= _fighterNextPressAt)
                {
                    Press(decision.Slot, HoldSeconds);
                    _fighterNextPressAt = now + HoldSeconds + FighterPressGapSeconds;
                }
                // Banking comes first: a full bird walks home, it does not turn round for a rival.
                if (decision.Chase >= 0 && _brain.State != AutoPlayState.Deposit)
                {
                    var to = view.Rivals[decision.Chase].Position - view.Self;
                    world = to.sqrMagnitude > 0.01f ? to.normalized : Vector2.zero;
                    _chasing = true;
                }
            }
            if (SteerOverride.HasValue)
            {
                var toTarget = SteerOverride.Value - view.Self;
                world = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector2.zero;   // full stick, into the target
            }

            float yaw = MatchCamera.Instance != null ? MatchCamera.Instance.CurrentYaw : 0f;
            // OnInput turns the stick into world space with StickToWorld(stick, yaw), a rotation: its inverse is the same call at -yaw.
            _stick = world.sqrMagnitude > 0.0001f ? AimRules.StickToWorld(world, -yaw) : Vector2.zero;

            ScheduleAbilities(self, now);
        }

        /// <summary>Same rim standoff the bots walk to (Chicken.prefab BotController._pileStandoff).</summary>
        public const float PileStandoff = 0.7f;

        private void ScheduleAbilities(ChickenController self, float now)
        {
            var abilities = self.Abilities;
            if (abilities == null) return;

            // The fighter presses come from FighterTactics; it keeps only the Peck here, none of the periodic casts.
            if (_brain.Personality.Fights)
            {
                _brain.TakeAttackCast();
                if (!_chasing && abilities.GetSlot(0).Is(AbilityJob.Peck) && _brain.Goal == AutoPlayGoal.AtPile &&
                    _brain.State == AutoPlayState.Forage && now >= _nextPeckAt)
                {
                    Press(0, PeckHoldSeconds); _nextPeckAt = now + PeckEverySeconds; PeckPresses++;
                }
                return;
            }

            if (_brain.TakeAttackCast())
            {
                int slot = FirstSlot(abilities, offensiveOnly: true, ReservedSlot);
                if (slot >= 0) { Press(slot, HoldSeconds); _nextCastAt = now + CastEverySeconds; return; }
            }

            bool peckSlot = abilities.GetSlot(0).Is(AbilityJob.Peck);
            if (peckSlot && _brain.Goal == AutoPlayGoal.AtPile && _brain.State == AutoPlayState.Forage)
            {
                if (now >= _nextPeckAt) { Press(0, PeckHoldSeconds); _nextPeckAt = now + PeckEverySeconds; PeckPresses++; }
                return;
            }
            // Banking: stand still, no casts (a dash out of the deposit zone would only delay the deposit).
            if (_brain.Goal == AutoPlayGoal.AtBase) return;

            if (now < _nextCastAt) return;
            _nextCastAt = now + CastEverySeconds;
            // Round-robin over the equipped slots the personality allows, skipping Peck (only useful at a pile).
            for (int tries = 0; tries < AbilityController.SlotCount; tries++)
            {
                int slot = _nextSlot;
                _nextSlot = (_nextSlot + 1) % AbilityController.SlotCount;
                var ab = abilities.GetSlot(slot);
                if (ab == null || ab.Is(AbilityJob.Peck) || slot == ReservedSlot) continue;
                if (!AutoPlayCasting.Allowed(ab.Category, _brain.Personality.CastsOffense)) continue;
                Press(slot, HoldSeconds);
                return;
            }
        }

        private static int FirstSlot(AbilityController abilities, bool offensiveOnly, int reserved)
        {
            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
            {
                var ab = abilities.GetSlot(slot);
                if (ab == null || ab.Is(AbilityJob.Peck) || slot == reserved) continue;
                if (!offensiveOnly || AutoPlayCasting.IsOffensive(ab.Category)) return slot;
            }
            return -1;
        }

        private static FighterRival FighterFacts(ChickenController c, Vector3 selfPos, NetworkBehaviourId marked, NetworkBehaviourId lootMarked)
        {
            var abilities = c.Abilities;
            int peck = abilities != null ? abilities.FindPeckSlot() : AbilityController.InvalidSlot;
            var cp = c.transform.position;
            return new FighterRival
            {
                Position = new Vector2(cp.x, cp.z),
                Cargo = c.Cargo != null ? c.Cargo.Cargo : 0f,
                CastingPeck = peck != AbilityController.InvalidSlot && abilities.IsSlotActive(peck),
                Stunned = c.CurrentControlState == ControlState.Stunned,
                Untouchable = c.IsStealProof || PlayerBase.WallBetween(selfPos, cp),
                Marked = marked != NetworkBehaviourId.None && c.Id == marked,
                LootMarked = lootMarked != NetworkBehaviourId.None && c.Id == lootMarked,
            };
        }

        private FighterDecision FighterChoose(ChickenController self, AssassinExecute execute, in AutoPlayView view)
        {
            var abilities = self.Abilities;
            if (abilities == null) return FighterDecision.None;
            bool canCast = ControlRules.CanCast(self.CurrentControlState);
            var fv = new FighterView
            {
                Self = view.Self, Rivals = _fRivals, RivalCount = view.RivalCount, Attacks = _fAttacks, MarkSlot = -1,
            };
            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
            {
                var ab = abilities.GetSlot(slot);
                if (ab == null) continue;
                bool ready = canCast && !abilities.IsSlotActive(slot) && abilities.IsReady(slot) && ab.IsUsable(self);
                if (ab.Is(AbilityJob.DeathMark))
                {
                    fv.MarkSlot = slot;
                    if (execute != null && execute.MarkedTarget != NetworkBehaviourId.None)
                    {
                        fv.Mark = FighterMarkPhase.Marked;
                        fv.CanKillNow = canCast && execute.CanKillNow;
                        fv.ArmRemaining = execute.ArmTimer.RemainingTime(execute.Runner) ?? 0f;
                        fv.WindowRemaining = execute.WindowTimer.RemainingTime(execute.Runner) ?? 0f;
                    }
                    else if (ready) fv.Mark = FighterMarkPhase.Ready;
                    continue;
                }
                if (ab.Job != AbilityJob.Stun && ab.Job != AbilityJob.Shove && ab.Job != AbilityJob.Steal) continue;
                _fAttacks[fv.AttackCount++] = new FighterAttack
                {
                    Slot = slot, Job = ab.Job, Ready = ready, Reach = ab.IndicatorRange, ControlSeconds = ab.EmittedControl.Seconds,
                };
            }
            return FighterTactics.Choose(fv);
        }

        /// <summary>Presses <paramref name="slot"/> now and holds it for <paramref name="hold"/> seconds (release = cast).</summary>
        public void Press(int slot, float hold)
        {
            if (slot < 0 || slot >= _pressPending.Length) return;
            float now = _clock();
            _pressPending[slot] = true;
            // Never shorter than two frames of simulated time: at x20 a frame is 0.3 s of the match, and a press must outlive the read.
            _pressExpires[slot] = now + Mathf.Max(PressLatchSeconds, 2f * _frameSeconds());
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

    /// <summary>Which moves a personality casts (pure, tested).</summary>
    public static class AutoPlayCasting
    {
        public static bool IsOffensive(AbilityCategory category) =>
            category == AbilityCategory.Steal || category == AbilityCategory.Control;

        /// <summary>Everything for an offensive personality; Utility / Defense only for a pure runner.</summary>
        public static bool Allowed(AbilityCategory category, bool castsOffense) => castsOffense || !IsOffensive(category);
    }
}
