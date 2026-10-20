using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// A pile as the auto-play brain sees it. <see cref="Position"/> is the centre (pile choice); a pile is a SOLID
    /// blocker with an elliptical footprint, so the brain walks to <see cref="Approach"/> (the walkable point on its rim,
    /// <c>FoodPile.SurfaceApproachPoint</c>) and is "at the pile" when <see cref="InReach"/> (<c>FoodPile.IsWithinCollectRange</c>,
    /// the surface-relative test Peck itself uses). Without <see cref="HasApproach"/> the centre and
    /// <see cref="AutoPlayBrain.ArriveRadius"/> are used (a point-sized pile, the tests' simple case).
    /// </summary>
    public struct AutoPlayPile
    {
        public Vector2 Position;
        public float Amount;
        public bool HasApproach;
        public Vector2 Approach;
        public bool InReach;
    }

    /// <summary>Another chicken as the auto-play brain sees it (world XZ and what it carries).</summary>
    public struct AutoPlayRival
    {
        public Vector2 Position;
        public float Cargo;
    }

    /// <summary>The loop the fake player is in.</summary>
    public enum AutoPlayState : byte { Forage, Deposit, Attack }

    /// <summary>What the brain wants this frame.</summary>
    public enum AutoPlayGoal : byte { Idle, ToPile, AtPile, ToBase, AtBase, ToRival }

    /// <summary>Everything the brain reads in one frame (world XZ).</summary>
    public struct AutoPlayView
    {
        public Vector2 Self;
        public float Cargo, Capacity;
        /// <summary>The chicken has a Peck slot (the Assassin does not: it can only steal / hunt).</summary>
        public bool CanForage;
        public bool HasBase;
        public Vector2 BasePos;
        /// <summary>The own base's <c>PlayerBase.DepositRadius</c> (centre distance that banks).</summary>
        public float BaseRadius;
        public AutoPlayPile[] Piles;
        public int PileCount;
        public AutoPlayRival[] Rivals;
        public int RivalCount;
    }

    /// <summary>
    /// How one auto-test client plays, seeded from its PlayerId so the clients of one session differ: an
    /// <c>aggressive</c> bird that hunts loaded rivals with its Steal / Control moves, a <c>runner</c> that only forages
    /// and banks, and a <c>balanced</c> one in between.
    /// </summary>
    public readonly struct AutoPlayPersonality
    {
        public readonly string Name;
        /// <summary>Head home once cargo reaches this fraction of capacity.</summary>
        public readonly float ReturnFraction;
        /// <summary>Hunt a loaded rival within this many metres (0 = never hunts).</summary>
        public readonly float AttackRange;
        /// <summary>Seconds between hunts.</summary>
        public readonly float AttackEverySeconds;
        /// <summary>Whether the periodic casts may use Steal / Control moves (a runner keeps to Utility / Defense).</summary>
        public readonly bool CastsOffense;

        public AutoPlayPersonality(string name, float returnFraction, float attackRange, float attackEverySeconds, bool castsOffense)
        {
            Name = name;
            ReturnFraction = returnFraction;
            AttackRange = attackRange;
            AttackEverySeconds = attackEverySeconds;
            CastsOffense = castsOffense;
        }

        public bool Hunts => AttackRange > 0f && AttackEverySeconds > 0f;

        public static readonly AutoPlayPersonality Aggressive = new AutoPlayPersonality("aggressive", 0.8f, 9f, 6f, true);
        public static readonly AutoPlayPersonality Runner = new AutoPlayPersonality("runner", 0.6f, 0f, 0f, false);
        public static readonly AutoPlayPersonality Balanced = new AutoPlayPersonality("balanced", 0.7f, 5f, 12f, true);

        /// <summary>PlayerId mod 3: 0 aggressive, 1 runner, 2 balanced (so three clients in a session get three styles).</summary>
        public static AutoPlayPersonality For(int playerId)
        {
            switch (((playerId % 3) + 3) % 3)
            {
                case 0:  return Aggressive;
                case 1:  return Runner;
                default: return Balanced;
            }
        }
    }

    /// <summary>
    /// The auto-test fake player's decisions (<c>cw_autoplay 1</c>): the real match loop.
    /// <b>Forage</b>: walk to the rim of the best non-empty pile (<see cref="BotTactics.PileCost"/>, the bots' own pile
    /// choice) and stand there while it is in Peck's reach (the provider pecks) until cargo reaches the personality's return
    /// fraction (~70 %) or the pile runs dry. <b>Deposit</b>: walk to the OWN base and stand inside its deposit radius
    /// until cargo is 0. <b>Attack</b>: hunting personalities close on the nearest loaded rival in range every few seconds
    /// and ask for one offensive cast; a bird that cannot forage (no Peck: the Assassin) hunts all round, banking whatever
    /// it steals. Unstick: when the bird moved less than <see cref="StuckMoveMetres"/> in <see cref="StuckSeconds"/> while it
    /// still has somewhere to go, it strafes, then backs off, then gives the pile up for a while; when it moves but never
    /// gets closer (orbiting) for <see cref="NoProgressSeconds"/>, the pile is given up too. Pure: world positions in, a
    /// world-XZ direction out; EditMode tests pin it, including a walk against the real pile footprint geometry.
    /// </summary>
    public sealed class AutoPlayBrain
    {
        public const float ArriveRadius = 1.2f;
        public const float StuckSeconds = 2f;
        public const float StuckMoveMetres = 0.3f;
        public const float NoProgressSeconds = 4f;
        public const float NoProgressMetres = 0.5f;
        public const float SkipPileSeconds = 8f;
        public const float StrafeSeconds = 0.8f;
        public const float BackOffSeconds = 1f;
        public const float WeaveAmplitude = 0.25f;
        /// <summary>Stand this far inside the deposit radius (the bank test uses the centre distance).</summary>
        public const float DepositStandMargin = 0.6f;
        /// <summary>A hunt gives up after this long.</summary>
        public const float AttackSeconds = 3f;
        /// <summary>Within this distance of the hunted rival the brain asks for the offensive cast.</summary>
        public const float CastRange = 3f;
        /// <summary>A bird that cannot forage casts at most this often while hunting.</summary>
        public const float HunterCastEverySeconds = 2.5f;
        /// <summary>Same weight a bot gives the walk home when choosing a pile (BotTactics: 1 = full round trip).</summary>
        public const float RoundTripBias = 0.5f;

        public AutoPlayPersonality Personality { get; }
        public AutoPlayState State { get; private set; } = AutoPlayState.Forage;
        public AutoPlayGoal Goal { get; private set; }
        public int TargetPile { get; private set; } = -1;
        public int TargetRival { get; private set; } = -1;
        /// <summary>Distance to the current goal (0 when standing at it).</summary>
        public float GoalDistance { get; private set; }
        /// <summary>Consecutive 2 s windows without real movement while there was somewhere to go.</summary>
        public int StuckTicks { get; private set; }
        /// <summary>How many times the unstick routine fired since the brain was made.</summary>
        public int Unsticks { get; private set; }

        private bool _wantsAttackCast;
        private float _nextAttackAt, _attackUntil, _nextHunterCastAt;
        private int _peckPile = -1;
        private int _skipPile = -1;
        private float _skipUntil, _escapeUntil;
        private Vector2 _escapeDir;
        // Movement sampling (position-based stuck) and progress tracking (orbit guard).
        private Vector2 _samplePos;
        private float _sampleAt = float.NegativeInfinity;
        private int _progressKey = int.MinValue;
        private float _bestDist, _progressAt;

        public AutoPlayBrain(AutoPlayPersonality personality)
        {
            Personality = personality;
            _nextAttackAt = personality.AttackEverySeconds;
        }

        /// <summary>True once after the hunt reached cast range: the provider presses an offensive move.</summary>
        public bool TakeAttackCast()
        {
            bool v = _wantsAttackCast;
            _wantsAttackCast = false;
            return v;
        }

        public bool ShouldDeposit(in AutoPlayView v, bool anyFood) =>
            v.HasBase && v.Cargo > 0.01f &&
            (!v.CanForage || v.Cargo >= Personality.ReturnFraction * v.Capacity - 0.001f || !anyFood);

        /// <param name="random01">A uniform 0..1 sample (the escape side); injected so tests are deterministic.</param>
        public Vector2 Tick(float time, in AutoPlayView v, float random01)
        {
            bool anyFood = false;
            for (int i = 0; i < v.PileCount; i++) if (v.Piles[i].Amount > 0f) { anyFood = true; break; }

            UpdateState(time, v, anyFood);

            // ---- goal ------------------------------------------------------------------------------------------
            Vector2 target;
            int targetKey;
            switch (State)
            {
                case AutoPlayState.Deposit:
                {
                    TargetPile = -1;
                    target = v.BasePos;
                    targetKey = -2;
                    float d = (v.Self - v.BasePos).magnitude;
                    if (d <= Mathf.Max(ArriveRadius, v.BaseRadius - DepositStandMargin)) return Arrive(AutoPlayGoal.AtBase, time, v.Self);
                    Goal = AutoPlayGoal.ToBase;
                    break;
                }

                case AutoPlayState.Attack:
                    if (TargetRival < 0 || TargetRival >= v.RivalCount) return Arrive(AutoPlayGoal.Idle, time, v.Self);
                    target = v.Rivals[TargetRival].Position;
                    targetKey = -3 - TargetRival;
                    Goal = AutoPlayGoal.ToRival;
                    break;

                default:
                {
                    TargetPile = PickPile(time, v);
                    if (TargetPile < 0) { _peckPile = -1; return Arrive(AutoPlayGoal.Idle, time, v.Self); }
                    var pile = v.Piles[TargetPile];
                    target = pile.HasApproach ? pile.Approach : pile.Position;
                    targetKey = TargetPile;
                    bool arrived = pile.HasApproach ? pile.InReach : (v.Self - pile.Position).magnitude <= ArriveRadius;
                    if (arrived)
                    {
                        _peckPile = TargetPile;
                        return Arrive(AutoPlayGoal.AtPile, time, v.Self);
                    }
                    Goal = AutoPlayGoal.ToPile;
                    break;
                }
            }

            float dist = (target - v.Self).magnitude;
            GoalDistance = dist;
            Unstick(time, v.Self, target, targetKey, dist, random01);
            if (time < _escapeUntil) return _escapeDir;

            var dir = (target - v.Self) / Mathf.Max(dist, 0.0001f);
            // Weave only on a long walk: close to the rim a sideways component is what made the birds circle a pile.
            float weave = dist > 4f ? Mathf.Sin(time * 1.3f) * WeaveAmplitude : 0f;
            return (dir + new Vector2(-dir.y, dir.x) * weave).normalized;
        }

        private void UpdateState(float time, in AutoPlayView v, bool anyFood)
        {
            if (State == AutoPlayState.Deposit)
            {
                if (v.Cargo <= 0.01f || !v.HasBase) State = v.CanForage ? AutoPlayState.Forage : AutoPlayState.Attack;
                return;
            }

            // The pile being pecked ran dry with food on board: bank it.
            bool pileDry = _peckPile >= 0 && _peckPile < v.PileCount && v.Piles[_peckPile].Amount <= 0f;
            if (ShouldDeposit(v, anyFood) || (pileDry && v.HasBase && v.Cargo > 0.01f))
            {
                State = AutoPlayState.Deposit;
                _peckPile = -1;
                TargetRival = -1;
                return;
            }

            if (!v.CanForage)
            {
                // No Peck: hunt all round - the nearest loaded rival, else the nearest one - and cast when close.
                State = AutoPlayState.Attack;
                int loaded = NearestRival(v, float.MaxValue, loadedOnly: true);
                TargetRival = loaded >= 0 ? loaded : NearestRival(v, float.MaxValue, loadedOnly: false);
                if (TargetRival >= 0 && time >= _nextHunterCastAt && (v.Rivals[TargetRival].Position - v.Self).magnitude <= CastRange)
                {
                    _wantsAttackCast = true;
                    _nextHunterCastAt = time + HunterCastEverySeconds;
                }
                return;
            }

            if (State == AutoPlayState.Forage && Personality.Hunts && time >= _nextAttackAt)
            {
                int rival = NearestRival(v, Personality.AttackRange, loadedOnly: true);
                if (rival >= 0)
                {
                    State = AutoPlayState.Attack;
                    TargetRival = rival;
                    _attackUntil = time + AttackSeconds;
                    _peckPile = -1;
                }
                _nextAttackAt = time + Personality.AttackEverySeconds;
            }
            else if (State == AutoPlayState.Attack)
            {
                if (time >= _attackUntil || TargetRival < 0 || TargetRival >= v.RivalCount) EndAttack();
                else if ((v.Rivals[TargetRival].Position - v.Self).magnitude <= CastRange)
                {
                    _wantsAttackCast = true;
                    EndAttack();
                }
            }
        }

        /// <summary>
        /// Position-based stuck (moved &lt; 0.3 m in 2 s): strafe, then back off, then give the pile up for a while.
        /// Progress-based (moving but no 0.5 m closer in 4 s: circling): give the pile up.
        /// </summary>
        private void Unstick(float time, Vector2 self, Vector2 target, int targetKey, float dist, float random01)
        {
            if (targetKey != _progressKey) { _progressKey = targetKey; _bestDist = dist; _progressAt = time; }
            else if (dist < _bestDist - NoProgressMetres) { _bestDist = dist; _progressAt = time; }
            else if (targetKey >= 0 && time - _progressAt >= NoProgressSeconds)
            {
                SkipPile(targetKey, time);
                _progressAt = time;
                _bestDist = dist;
            }

            if (float.IsNegativeInfinity(_sampleAt)) { _sampleAt = time; _samplePos = self; return; }
            if (time - _sampleAt < StuckSeconds) return;
            bool stuck = (self - _samplePos).magnitude < StuckMoveMetres && time >= _escapeUntil;
            _sampleAt = time;
            _samplePos = self;
            if (!stuck) { StuckTicks = 0; return; }

            StuckTicks++;
            Unsticks++;
            var to = (target - self).sqrMagnitude > 1e-6f ? (target - self).normalized : Vector2.right;
            var side = random01 < 0.5f ? new Vector2(-to.y, to.x) : new Vector2(to.y, -to.x);
            switch (StuckTicks)
            {
                case 1:
                    _escapeDir = side;
                    _escapeUntil = time + StrafeSeconds;
                    break;
                case 2:
                    _escapeDir = (-to + side * 0.5f).normalized;
                    _escapeUntil = time + BackOffSeconds;
                    break;
                default:
                    if (targetKey >= 0) SkipPile(targetKey, time);
                    _escapeDir = -side;
                    _escapeUntil = time + StrafeSeconds;
                    break;
            }
        }

        private void SkipPile(int pile, float time)
        {
            _skipPile = pile;
            _skipUntil = time + SkipPileSeconds;
        }

        private void EndAttack()
        {
            State = AutoPlayState.Forage;
            TargetRival = -1;
        }

        private Vector2 Arrive(AutoPlayGoal goal, float time, Vector2 self)
        {
            Goal = goal;
            GoalDistance = 0f;
            _progressKey = int.MinValue;
            // Standing still on purpose is not being stuck.
            StuckTicks = 0;
            _sampleAt = time;
            _samplePos = self;
            return Vector2.zero;
        }

        private static int NearestRival(in AutoPlayView v, float range, bool loadedOnly)
        {
            int best = -1;
            float bestDist = range;
            for (int i = 0; i < v.RivalCount; i++)
            {
                if (loadedOnly && v.Rivals[i].Cargo <= 0f) continue;
                float d = (v.Rivals[i].Position - v.Self).magnitude;
                if (d <= bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private int PickPile(float time, in AutoPlayView v)
        {
            // A food pile already in reach wins outright: peck where you stand.
            for (int i = 0; i < v.PileCount; i++)
                if (v.Piles[i].HasApproach && v.Piles[i].InReach && v.Piles[i].Amount > 0f && !(i == _skipPile && time < _skipUntil))
                    return i;

            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < v.PileCount; i++)
            {
                if (v.Piles[i].Amount <= 0f) continue;
                if (i == _skipPile && time < _skipUntil) continue;
                float toPile = (v.Piles[i].Position - v.Self).magnitude;
                float toBase = v.HasBase ? (v.Piles[i].Position - v.BasePos).magnitude : 0f;
                float cost = BotTactics.PileCost(toPile, toBase, v.Piles[i].Amount, RoundTripBias);
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            // Every non-empty pile is the skipped one: go back to it rather than stand still.
            if (best < 0 && _skipPile >= 0 && _skipPile < v.PileCount && v.Piles[_skipPile].Amount > 0f) best = _skipPile;
            return best;
        }
    }
}
