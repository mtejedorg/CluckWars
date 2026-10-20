using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>A pile as the auto-play brain sees it (world XZ and food left).</summary>
    public struct AutoPlayPile
    {
        public Vector2 Position;
        public float Amount;
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
        public bool HasBase;
        public Vector2 BasePos;
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
    /// <b>Forage</b>: walk to the best non-empty pile (<see cref="BotTactics.PileCost"/>, the bots' own pile choice) and
    /// stand there (the provider pecks) until cargo reaches the personality's return fraction (~70 %) or the pile runs dry.
    /// <b>Deposit</b>: walk to the OWN base and stand inside its deposit zone until cargo is 0. <b>Attack</b> (hunting
    /// personalities, every few seconds): close on the nearest loaded rival in range and ask for one offensive cast.
    /// Movement weaves a little, and re-targets + strafes out after 2 s without 0.5 m of progress. Pure: world positions in,
    /// a world-XZ direction out; EditMode tests pin it.
    /// </summary>
    public sealed class AutoPlayBrain
    {
        public const float ArriveRadius = 1.2f;
        public const float StuckSeconds = 2f;
        public const float StuckProgress = 0.5f;
        public const float SkipPileSeconds = 6f;
        public const float StrafeSeconds = 0.8f;
        public const float WeaveAmplitude = 0.25f;
        /// <summary>Inside this fraction of the deposit-zone radius the bird stands still to bank.</summary>
        public const float DepositStandFraction = 0.5f;
        /// <summary>A hunt gives up after this long.</summary>
        public const float AttackSeconds = 3f;
        /// <summary>Within this distance of the hunted rival the brain asks for the offensive cast.</summary>
        public const float CastRange = 3f;
        /// <summary>Same weight a bot gives the walk home when choosing a pile (BotTactics: 1 = full round trip).</summary>
        public const float RoundTripBias = 0.5f;

        public AutoPlayPersonality Personality { get; }
        public AutoPlayState State { get; private set; } = AutoPlayState.Forage;
        public AutoPlayGoal Goal { get; private set; }
        public int TargetPile { get; private set; } = -1;
        public int TargetRival { get; private set; } = -1;

        private bool _wantsAttackCast;
        private float _nextAttackAt, _attackUntil;
        private int _peckPile = -1;
        private float _bestDist = float.MaxValue, _progressAt;
        private int _skipPile = -1;
        private float _skipUntil, _strafeUntil;
        private Vector2 _strafeDir;
        private int _lastTargetKey = int.MinValue;

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
            (v.Cargo >= Personality.ReturnFraction * v.Capacity - 0.001f || !anyFood);

        /// <param name="random01">A uniform 0..1 sample (the strafe side); injected so tests are deterministic.</param>
        public Vector2 Tick(float time, in AutoPlayView v, float random01)
        {
            bool anyFood = false;
            for (int i = 0; i < v.PileCount; i++) if (v.Piles[i].Amount > 0f) { anyFood = true; break; }

            // ---- transitions -----------------------------------------------------------------------------------
            if (State == AutoPlayState.Deposit)
            {
                if (v.Cargo <= 0.01f || !v.HasBase) State = AutoPlayState.Forage;
            }
            else
            {
                // The pile being pecked ran dry with food on board: bank it.
                bool pileDry = _peckPile >= 0 && _peckPile < v.PileCount && v.Piles[_peckPile].Amount <= 0f;
                if (ShouldDeposit(v, anyFood) || (pileDry && v.HasBase && v.Cargo > 0.01f))
                {
                    State = AutoPlayState.Deposit;
                    _peckPile = -1;
                }
                else if (State == AutoPlayState.Forage && Personality.Hunts && time >= _nextAttackAt)
                {
                    int rival = NearestLoadedRival(v, Personality.AttackRange);
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

            // ---- movement --------------------------------------------------------------------------------------
            Vector2 target;
            int targetKey;
            switch (State)
            {
                case AutoPlayState.Deposit:
                    TargetPile = -1;
                    target = v.BasePos;
                    targetKey = -2;
                    if ((v.Self - v.BasePos).magnitude <= Mathf.Max(ArriveRadius, v.BaseRadius * DepositStandFraction))
                        return Arrive(AutoPlayGoal.AtBase);
                    Goal = AutoPlayGoal.ToBase;
                    break;

                case AutoPlayState.Attack:
                    target = v.Rivals[TargetRival].Position;
                    targetKey = -3 - TargetRival;
                    Goal = AutoPlayGoal.ToRival;
                    break;

                default:
                    TargetPile = PickPile(time, v);
                    if (TargetPile < 0) { _peckPile = -1; return Arrive(AutoPlayGoal.Idle); }
                    target = v.Piles[TargetPile].Position;
                    targetKey = TargetPile;
                    if ((v.Self - target).magnitude <= ArriveRadius)
                    {
                        _peckPile = TargetPile;
                        return Arrive(AutoPlayGoal.AtPile);
                    }
                    Goal = AutoPlayGoal.ToPile;
                    break;
            }

            float dist = (target - v.Self).magnitude;
            if (targetKey != _lastTargetKey) { _lastTargetKey = targetKey; _bestDist = dist; _progressAt = time; }
            else if (dist < _bestDist - StuckProgress) { _bestDist = dist; _progressAt = time; }
            else if (time - _progressAt >= StuckSeconds)
            {
                // No progress: skip this pile for a while (a base or a rival cannot be skipped) and strafe out sideways.
                if (targetKey >= 0) { _skipPile = targetKey; _skipUntil = time + SkipPileSeconds; }
                var to = (target - v.Self).normalized;
                _strafeDir = random01 < 0.5f ? new Vector2(-to.y, to.x) : new Vector2(to.y, -to.x);
                _strafeUntil = time + StrafeSeconds;
                _progressAt = time;
                _bestDist = dist;
            }

            if (time < _strafeUntil) return _strafeDir;

            var dir = (target - v.Self) / Mathf.Max(dist, 0.0001f);
            var side = new Vector2(-dir.y, dir.x) * (Mathf.Sin(time * 1.3f) * WeaveAmplitude);
            return (dir + side).normalized;
        }

        private void EndAttack()
        {
            State = AutoPlayState.Forage;
            TargetRival = -1;
        }

        private Vector2 Arrive(AutoPlayGoal goal)
        {
            Goal = goal;
            _lastTargetKey = int.MinValue;
            return Vector2.zero;
        }

        private static int NearestLoadedRival(in AutoPlayView v, float range)
        {
            int best = -1;
            float bestDist = range;
            for (int i = 0; i < v.RivalCount; i++)
            {
                if (v.Rivals[i].Cargo <= 0f) continue;
                float d = (v.Rivals[i].Position - v.Self).magnitude;
                if (d <= bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private int PickPile(float time, in AutoPlayView v)
        {
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
