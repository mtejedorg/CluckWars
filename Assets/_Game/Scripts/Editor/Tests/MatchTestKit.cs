using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Synthetic maps, kits and setups for the match simulator tests. Every number is the caller's: nothing here is read from a game
    /// asset. Maps created here are destroyed by <see cref="Release"/> (call it from a TearDown).
    /// </summary>
    internal static class MatchTestKit
    {
        public const float BaseRadius = 1.5f;
        public const float Reach = 1f;

        private static readonly List<Object> Made = new List<Object>();

        public static void Release()
        {
            foreach (var o in Made) if (o != null) Object.DestroyImmediate(o);
            Made.Clear();
        }

        /// <summary>13 places on a square of half side <paramref name="half"/>, no walls.</summary>
        public static OracleMapSO OpenMap(float half) => Build(half, null, null, 0f);

        /// <summary>The open map plus one wall segment a-b that a chicken centre keeps <paramref name="halfWidth"/> away from.</summary>
        public static OracleMapSO OneWallMap(float half, Vector2 a, Vector2 b, float halfWidth) =>
            Build(half, new[] { a }, new[] { b }, halfWidth);

        private static OracleMapSO Build(float half, Vector2[] wallA, Vector2[] wallB, float halfWidth)
        {
            var map = ScriptableObject.CreateInstance<OracleMapSO>();
            Made.Add(map);
            map.Places = new OraclePlace[OracleMapSO.PlaceCount];
            map.Places[OracleMapSO.CentreIndex] = new OraclePlace { Kind = OraclePlaceKind.Centre, Seat = -1, Pos = Vector2.zero };
            for (int s = 0; s < 4; s++)
            {
                var dir = new Vector2(Mathf.Cos(Mathf.PI * 0.5f * s), Mathf.Sin(Mathf.PI * 0.5f * s));
                var edge = new Vector2(Mathf.Cos(Mathf.PI * 0.5f * (s + 0.5f)), Mathf.Sin(Mathf.PI * 0.5f * (s + 0.5f)));
                map.Places[OracleMapSO.PersonalIndex(s)] = new OraclePlace { Kind = OraclePlaceKind.Personal, Seat = s, Pos = dir * half * 0.45f };
                map.Places[OracleMapSO.ContestedIndex(s)] = new OraclePlace { Kind = OraclePlaceKind.Contested, Seat = s, Pos = edge * half * 0.7f };
                map.Places[OracleMapSO.BaseIndex(s)] = new OraclePlace { Kind = OraclePlaceKind.Base, Seat = s, Pos = dir * half * 0.8f };
            }
            map.WallA = wallA ?? new Vector2[0];
            map.WallB = wallB ?? new Vector2[0];
            map.WallHalfWidth = halfWidth;
            map.BaseRadius = BaseRadius;
            map.CollectReach = Reach;
            map.SceneFingerprint = "synthetic";

            int n = OracleMapSO.PlaceCount;
            map.WalkLegs = new float[n * n];
            map.JumpLegs = new float[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var a = map.Places[i].Pos;
                    var b = map.Places[j].Pos;
                    float path = OraclePaths.ShortestPath(a, b, map.WallA, map.WallB, halfWidth, half);
                    float rims = MatchGeometry.Rim(map, i, b - a) + MatchGeometry.Rim(map, j, a - b);
                    map.WalkLegs[i * n + j] = Mathf.Max(0f, path - rims);
                    map.JumpLegs[i * n + j] = Mathf.Max(0f, Vector2.Distance(a, b) - rims);
                }
            return map;
        }

        public static MatchKit Farmer(string label, float speed, int capacity, float peckAmount, float peckCooldown, float castSeconds, float depositRate) =>
            new MatchKit
            {
                Label = label,
                Class = ChickenClass.Warrior,
                Farm = new OracleKit
                {
                    Label = label, Forages = true, MoveSpeed = speed, CargoCapacity = capacity, PeckAmount = peckAmount,
                    PeckCooldown = peckCooldown, PeckCastSeconds = castSeconds, DepositRate = depositRate,
                },
            };

        /// <summary>
        /// A setup with the given seats (null = empty seat), one policy for every present seat, no reaction delay, a think on every tick,
        /// a 30 s clock, a win at 40, piles of 10 / 15 / 40 and no pile slow. Change <see cref="MatchSetup.Rules"/> after the call to taste.
        /// </summary>
        public static MatchSetup Setup(OracleMapSO map, MatchKit[] seats, PolicyFactory policy, float arenaHalf)
        {
            const float tick = 1f / 30f;
            var policies = new PolicyFactory[4];
            var param = new PolicyParams[4];
            for (int s = 0; s < 4; s++)
            {
                policies[s] = seats[s] != null ? policy : null;
                param[s] = PolicyParams.Default;
                param[s].ReactionDelay = 0f;
            }
            return new MatchSetup
            {
                Map = map,
                Nav = OracleNav.For(map, arenaHalf),
                Food = new OracleFood { Personal = 10f, Contested = 15f, Centre = 40f },
                Rules = new MatchRules
                {
                    Farm = new FarmRules { WinTarget = 40f, ClockSeconds = 30f, TickSeconds = tick, PileSlowFactor = 1f },
                    ArenaHalf = arenaHalf,
                    ThinkSeconds = tick,
                },
                Seats = seats,
                Policies = policies,
                Params = param,
            };
        }

        // ---- abilities, fighters and scripted policies (slice S3) ----

        public static MatchAbility Stun(float radius, float seconds, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Stun, Asset = "test-stun", Cooldown = cooldown, Radius = radius, Control = ControlEmission.Stun(seconds),
        };

        public static MatchAbility Slow(float radius, float seconds, float factor, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Slow, Asset = "test-slow", Cooldown = cooldown, Radius = radius, Control = ControlEmission.Slow(seconds, factor),
        };

        public static MatchAbility Shove(float radius, float knockback, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Shove, Asset = "test-shove", Cooldown = cooldown, Radius = radius, Knockback = knockback,
        };

        public static MatchAbility Steal(float radius, float amount, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Steal, Asset = "test-steal", Cooldown = cooldown, Radius = radius, StealAmount = amount,
        };

        public static MatchAbility SlowArea(float radius, float factor, float duration, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.SlowArea, Asset = "test-slow-area", Cooldown = cooldown, Duration = duration,
            AreaAura = true, AreaRadius = radius, AreaSlowFactor = factor,
        };

        public static MatchAbility Speed(float multiplier, float seconds, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Speed, Asset = "test-speed", Cooldown = cooldown, Duration = seconds, SpeedMultiplier = multiplier,
        };

        public static MatchAbility Jump(float multiplier, float seconds, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Jump, Asset = "test-jump", Cooldown = cooldown, Duration = seconds, SpeedMultiplier = multiplier,
        };

        public static MatchAbility Shield(float walkSeconds, float walkMultiplier, float eggSeconds, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Shield, Asset = "test-shield", Cooldown = cooldown, Duration = walkSeconds + eggSeconds,
            ShieldWalkSeconds = walkSeconds, ShieldWalkSpeedMultiplier = walkMultiplier, ShieldEggSeconds = eggSeconds, StealProof = true,
        };

        public static MatchAbility Hide(float opacity, float seconds, float cooldown) => new MatchAbility
        {
            Job = AbilityJob.Hide, Asset = "test-hide", Cooldown = cooldown, Duration = seconds, HideOpacity = opacity, EndsOnNextMove = true,
        };

        public static MatchAbility DeathMark(float arm, float window, float range, float success, float fail, float removal, float leaderWeight = 1f) => new MatchAbility
        {
            Job = AbilityJob.DeathMark, Asset = "test-death-mark", Cooldown = fail, MarkArmSeconds = arm, MarkWindowSeconds = window,
            MarkKillRange = range, MarkSuccessCooldown = success, MarkFailCooldown = fail, MarkRemovalSeconds = removal,
            MarkLeaderPickWeight = leaderWeight,
        };

        /// <summary>A forager with the given abilities (slot order = array order) and a passive that changes nothing unless the caller passes one.</summary>
        public static MatchKit Fighter(string label, float speed, MatchAbility[] abilities, MatchPassive? passive = null,
            float peckAmount = 3f, float peckCooldown = 1f, float castSeconds = 0.5f, int capacity = 100)
        {
            var slow = new List<float>();
            foreach (var a in abilities)
            {
                if (a.Control.Kind == ControlKind.Slow) slow.Add(a.Control.SlowFactor);
                if (a.AreaAura) slow.Add(a.AreaSlowFactor);
            }
            var kit = Farmer(label, speed, capacity, peckAmount, peckCooldown, castSeconds, 5f);
            kit.Abilities = abilities;
            kit.Passive = passive ?? MatchPassive.Identity(slow.ToArray());
            return kit;
        }

        public delegate MatchIntent Script(MatchView view, int seat);

        private sealed class ScriptPolicy : IMatchPolicy
        {
            private readonly Script _f;
            private readonly int _seat;
            public ScriptPolicy(int seat, Script f) { _seat = seat; _f = f; }
            public MatchIntent Think(MatchView view, ref MatchRng rng) => _f(view, _seat);
        }

        public static PolicyFactory Policy(Script f) => (seat, p) => new ScriptPolicy(seat, f);

        /// <summary>Stand where the chicken is.</summary>
        public static MatchIntent StandAt(MatchView v, int seat) => MatchIntent.Idle(v.Seats[seat].Pos);

        public static readonly PolicyFactory Stand = Policy(StandAt);

        /// <summary>Walk to the pile and peck it whenever the Peck is ready.</summary>
        public static MatchIntent PeckAt(MatchView v, int seat, int pile) =>
            new MatchIntent { Goal = v.Seats[seat].Pos, PeckPile = pile, CastSlot = -1, CastTarget = -1 };

        public static PolicyFactory Pecker(int pile) => Policy((v, seat) => PeckAt(v, seat, pile));

        /// <summary>Stand, and cast <paramref name="slot"/> once on the first think at or after <paramref name="time"/>.</summary>
        public static PolicyFactory CastAt(float time, int slot, int target = -1)
        {
            return (seat, p) =>
            {
                bool done = false;
                return new ScriptPolicy(seat, (v, s) =>
                {
                    var intent = StandAt(v, s);
                    if (!done && v.Time + 1e-4f >= time) { done = true; intent.CastSlot = slot; intent.CastTarget = target; }
                    return intent;
                });
            };
        }

        /// <summary>A two-seat set-up (seats 0 and 1, the others empty) with one policy per seat.</summary>
        public static MatchSetup Duel(OracleMapSO map, MatchKit a, MatchKit b, PolicyFactory pa, PolicyFactory pb, float arenaHalf, float clockSeconds)
        {
            var setup = Setup(map, new[] { a, b, null, null }, pa, arenaHalf);
            setup.Policies[1] = pb;
            setup.Rules.Farm.ClockSeconds = clockSeconds;
            setup.RecordEvents = true;
            return setup;
        }
    }
}
