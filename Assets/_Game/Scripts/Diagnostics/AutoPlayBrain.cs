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

    /// <summary>What the brain wants this frame.</summary>
    public enum AutoPlayGoal : byte { Idle, ToPile, AtPile, ToBase, AtBase }

    /// <summary>
    /// Bot-like movement for the auto-test's fake input (test-session item 7, <c>cw_autoplay 1</c>): head for the best
    /// non-empty pile while the beak has room (<see cref="BotTactics.PileCost"/>, the bots' own pile choice), home when
    /// carrying a full load (or anything once the piles are dry), a slow side-to-side weave so two peers on the same line
    /// do not stay stuck in each other, and a re-target when 2 s pass without getting 0.5 m closer (the pile is skipped
    /// for a while and the bird strafes out). Pure: world positions in, a world-XZ direction out; tests pin it.
    /// </summary>
    public sealed class AutoPlayBrain
    {
        public const float ArriveRadius = 1.2f;
        public const float StuckSeconds = 2f;
        public const float StuckProgress = 0.5f;
        public const float SkipPileSeconds = 6f;
        public const float StrafeSeconds = 0.8f;
        public const float WeaveAmplitude = 0.25f;
        /// <summary>Same weight a Speedy bot gives the walk home when choosing a pile (BotTactics: 1 = full round trip).</summary>
        public const float RoundTripBias = 0.5f;

        public AutoPlayGoal Goal { get; private set; }
        public int TargetPile { get; private set; } = -1;

        private float _bestDist = float.MaxValue, _progressAt;
        private int _skipPile = -1;
        private float _skipUntil, _strafeUntil;
        private Vector2 _strafeDir;
        private int _lastTargetKey = int.MinValue;

        /// <param name="baseRadius">Radius of the own base's deposit zone; inside 60 % of it the bird stands still.</param>
        /// <param name="random01">A uniform 0..1 sample (the strafe side); injected so tests are deterministic.</param>
        public Vector2 Tick(float time, Vector2 self, float cargo, float capacity, bool hasBase, Vector2 basePos, float baseRadius,
            AutoPlayPile[] piles, int pileCount, float random01)
        {
            bool anyFood = false;
            for (int i = 0; i < pileCount; i++) if (piles[i].Amount > 0f) { anyFood = true; break; }

            bool full = capacity > 0f && cargo >= capacity - 0.001f;
            bool goHome = hasBase && cargo > 0f && (full || !anyFood);

            Vector2 target;
            int targetKey;
            if (goHome)
            {
                TargetPile = -1;
                target = basePos;
                targetKey = -2;
                if ((self - basePos).magnitude <= Mathf.Max(ArriveRadius, baseRadius * 0.6f)) return Arrive(AutoPlayGoal.AtBase);
                Goal = AutoPlayGoal.ToBase;
            }
            else
            {
                TargetPile = PickPile(time, self, hasBase, basePos, piles, pileCount);
                if (TargetPile < 0) return Arrive(AutoPlayGoal.Idle);
                target = piles[TargetPile].Position;
                targetKey = TargetPile;
                if ((self - target).magnitude <= ArriveRadius) return Arrive(AutoPlayGoal.AtPile);
                Goal = AutoPlayGoal.ToPile;
            }

            float dist = (target - self).magnitude;
            if (targetKey != _lastTargetKey) { _lastTargetKey = targetKey; _bestDist = dist; _progressAt = time; }
            else if (dist < _bestDist - StuckProgress) { _bestDist = dist; _progressAt = time; }
            else if (time - _progressAt >= StuckSeconds)
            {
                // No progress: skip this pile for a while (a base cannot be skipped) and strafe out sideways.
                if (targetKey >= 0) { _skipPile = targetKey; _skipUntil = time + SkipPileSeconds; }
                var to = (target - self).normalized;
                _strafeDir = random01 < 0.5f ? new Vector2(-to.y, to.x) : new Vector2(to.y, -to.x);
                _strafeUntil = time + StrafeSeconds;
                _progressAt = time;
                _bestDist = dist;
            }

            if (time < _strafeUntil) return _strafeDir;

            var dir = (target - self) / Mathf.Max(dist, 0.0001f);
            var side = new Vector2(-dir.y, dir.x) * (Mathf.Sin(time * 1.3f) * WeaveAmplitude);
            return (dir + side).normalized;
        }

        private Vector2 Arrive(AutoPlayGoal goal)
        {
            Goal = goal;
            _lastTargetKey = int.MinValue;
            return Vector2.zero;
        }

        private int PickPile(float time, Vector2 self, bool hasBase, Vector2 basePos, AutoPlayPile[] piles, int count)
        {
            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (piles[i].Amount <= 0f) continue;
                if (i == _skipPile && time < _skipUntil) continue;
                float toPile = (piles[i].Position - self).magnitude;
                float toBase = hasBase ? (piles[i].Position - basePos).magnitude : 0f;
                float cost = BotTactics.PileCost(toPile, toBase, piles[i].Amount, RoundTripBias);
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            // Every non-empty pile is the skipped one: go back to it rather than stand still.
            if (best < 0 && _skipPile >= 0 && _skipPile < count && piles[_skipPile].Amount > 0f) best = _skipPile;
            return best;
        }
    }
}
