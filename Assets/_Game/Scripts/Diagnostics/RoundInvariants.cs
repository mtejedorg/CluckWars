using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>One chicken as the round invariants see it (plain data, any peer).</summary>
    public struct InvariantChicken
    {
        public string Id;
        public int Home;
        public Vector2 Position;
        public float Cargo;
        /// <summary>Longest remaining cooldown over its ability slots (s).</summary>
        public float MaxCooldown;
        /// <summary>Any control state other than Free (slowed, rooted, stunned).</summary>
        public bool ControlActive;
        public int ActiveSlots;
        public bool IsBot;
    }

    /// <summary>A violated invariant: <c>Invariant FAIL &lt;name&gt; expected=.. actual=.. who=..</c>.</summary>
    public readonly struct InvariantFailure
    {
        public readonly string Name, Expected, Actual, Who;

        public InvariantFailure(string name, string expected, string actual, string who)
        {
            Name = name; Expected = expected; Actual = actual; Who = who;
        }

        public string Line() => "Invariant FAIL " + Name + " expected=" + Expected + " actual=" + Actual + " who=" + Who;
    }

    /// <summary>
    /// What must hold when a round starts and ends, as pure rules over plain data so EditMode tests pin them; the
    /// in-game <c>InvariantProbe</c> feeds them and logs every failure. Read-only: nothing here touches the game.
    /// </summary>
    public static class RoundInvariants
    {
        /// <summary>A chicken further than this from its home corner's spawn point at round start did not get reset.</summary>
        public const float SpawnToleranceMetres = 2.5f;
        public const float TimerToleranceSeconds = 0.75f;
        public const int ExpectedSoloBots = 3;

        private static string F(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Round start (+0.5 s): every chicken near its home corner, no cargo, no cooldowns, no control state, no running
        /// ability; every base at 0; every pile at its authored amount; the match timer at the configured duration; 3 bots
        /// in solo and none in a shared session; and nothing the last round placed is still alive (B10): no ability zone
        /// (<paramref name="zones"/>), no chicken with an aura on (<paramref name="auras"/>), no Doppelganger decoy
        /// (<paramref name="decoys"/>). The check runs during the intro, when nobody can cast, so any of these is a
        /// leftover. Returns how many checks were evaluated; failures are appended.
        /// </summary>
        public static int CheckRoundStart(IReadOnlyList<InvariantChicken> chickens, IReadOnlyList<Vector3> spawnPoints,
            IReadOnlyList<(int corner, float food)> bases, IReadOnlyList<(float amount, float initial)> piles,
            float timeRemaining, float configuredDuration, bool singleMode, List<InvariantFailure> fails,
            int zones = 0, int auras = 0, int decoys = 0)
        {
            int n = 0, bots = 0;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c.IsBot) bots++;
                n++;
                if (spawnPoints == null || c.Home < 0 || c.Home >= spawnPoints.Count)
                    fails.Add(new InvariantFailure("home-corner", "0.." + ((spawnPoints?.Count ?? 0) - 1), c.Home.ToString(), c.Id));
                else
                {
                    var s = spawnPoints[c.Home];
                    float d = Vector2.Distance(c.Position, new Vector2(s.x, s.z));
                    if (d > SpawnToleranceMetres)
                        fails.Add(new InvariantFailure("respawn", "<=" + F(SpawnToleranceMetres) + "m", F(d) + "m", c.Id));
                }
                n += 4;
                if (c.Cargo > 0.01f) fails.Add(new InvariantFailure("cargo-zero", "0", F(c.Cargo), c.Id));
                if (c.MaxCooldown > 0.05f) fails.Add(new InvariantFailure("cooldowns-zero", "0", F(c.MaxCooldown) + "s", c.Id));
                if (c.ControlActive) fails.Add(new InvariantFailure("control-free", "Free", "not Free", c.Id));
                if (c.ActiveSlots > 0) fails.Add(new InvariantFailure("no-active-ability", "0", c.ActiveSlots.ToString(), c.Id));
            }

            for (int i = 0; i < bases.Count; i++)
            {
                n++;
                if (bases[i].food > 0.01f)
                    fails.Add(new InvariantFailure("base-zero", "0", F(bases[i].food), "base" + bases[i].corner));
            }

            for (int i = 0; i < piles.Count; i++)
            {
                n++;
                if (Mathf.Abs(piles[i].amount - piles[i].initial) > 0.01f)
                    fails.Add(new InvariantFailure("pile-full", F(piles[i].initial), F(piles[i].amount), "pile" + i));
            }

            n++;
            if (Mathf.Abs(timeRemaining - configuredDuration) > TimerToleranceSeconds)
                fails.Add(new InvariantFailure("timer", F(configuredDuration) + "s", F(timeRemaining) + "s", "match"));

            n++;
            int expectedBots = singleMode ? ExpectedSoloBots : 0;
            if (bots != expectedBots)
                fails.Add(new InvariantFailure("bots", expectedBots.ToString(), bots.ToString(), singleMode ? "solo" : "shared"));

            n += 3;
            if (zones > 0) fails.Add(new InvariantFailure("zones-zero", "0", zones.ToString(), "world"));
            if (auras > 0) fails.Add(new InvariantFailure("auras-zero", "0", auras.ToString(), "world"));
            if (decoys > 0) fails.Add(new InvariantFailure("decoys-zero", "0", decoys.ToString(), "world"));
            return n;
        }

        /// <summary>
        /// The <c> zn=.. au=.. dc=..</c> suffix the probe prints on every round-start <c>Invariant ok|FAIL</c> line (B10);
        /// merge.ps1's round-start leftover rule parses exactly this.
        /// </summary>
        public static string StartCounts(int zones, int auras, int decoys) =>
            " zn=" + zones + " au=" + auras + " dc=" + decoys;

        /// <summary>
        /// Round end: a named winner (corner >= 0) holds the most banked food (ties allowed); no winner means nobody leads
        /// alone. Returns the number of checks; failures are appended.
        /// </summary>
        public static int CheckRoundEnd(int winnerCorner, IReadOnlyList<(int corner, float food)> bases, List<InvariantFailure> fails)
        {
            float top = float.MinValue, winnerFood = float.NaN;
            int leaders = 0;
            for (int i = 0; i < bases.Count; i++)
            {
                if (bases[i].corner == winnerCorner) winnerFood = bases[i].food;
                if (bases[i].food > top + 0.01f) { top = bases[i].food; leaders = 1; }
                else if (Mathf.Abs(bases[i].food - top) <= 0.01f) leaders++;
            }
            if (bases.Count == 0) return 0;

            if (winnerCorner >= 0)
            {
                if (float.IsNaN(winnerFood))
                    fails.Add(new InvariantFailure("winner-has-base", "base" + winnerCorner, "none", "match"));
                else if (winnerFood < top - 0.01f)
                    fails.Add(new InvariantFailure("winner-leads", ">=" + F(top), F(winnerFood), "base" + winnerCorner));
            }
            else if (leaders == 1 && top > 0.01f)
                fails.Add(new InvariantFailure("winner-named", "a winner", "none while one base leads with " + F(top), "match"));
            return 1;
        }

        /// <summary>
        /// Order-independent hash of (object type, position quantised to 0.5 m): the sum of per-object FNV-1a hashes, so two
        /// peers that see the same objects in the same places print the same value whatever their list order.
        /// </summary>
        public static uint WorldHash(IEnumerable<(string type, Vector2 position)> objects, out int count)
        {
            uint sum = 0;
            count = 0;
            foreach (var (type, p) in objects)
            {
                count++;
                uint h = 2166136261u;
                h = Fnv(h, type ?? string.Empty);
                h = Fnv(h, Mathf.RoundToInt(p.x / 0.5f));
                h = Fnv(h, Mathf.RoundToInt(p.y / 0.5f));
                unchecked { sum += h; }
            }
            return sum;
        }

        /// <summary>One live <c>AbilityZone</c> as the World probe sees it (pure input of <see cref="SplitZones"/>).</summary>
        public struct WorldZone
        {
            /// <summary>Anchor == Caster (an Aura): its position is a chicken's, which peers see up to ~3 m apart.</summary>
            public bool CasterAnchored;
            public Vector2 Position;
            /// <summary>The owner chicken's <c>NetworkBehaviourId</c> raw value: the aura's identity on every peer.</summary>
            public uint OwnerRaw;
            public float Radius;
            public byte Effect;
            public byte Channels;
        }

        /// <summary>Radius bucket of the aura hash: 0.1 m, finer than any authored difference and coarser than float noise.</summary>
        public const float AuraRadiusBucket = 0.1f;

        /// <summary>
        /// Splits the live zones for the World line (WP3, B1): World-anchored zones go to <paramref name="statics"/>
        /// (hashed by position like every static object); Caster-anchored zones go to <paramref name="auras"/>, keyed by
        /// what is the same on every peer - owner id, radius bucket, effect, channels - and never by position, which
        /// follows a moving chicken (it would make the static hash differ on every heartbeat).
        /// </summary>
        public static void SplitZones(IEnumerable<WorldZone> zones, List<(string type, Vector2 position)> statics,
            List<(uint owner, int radiusBucket, byte effect, byte channels)> auras)
        {
            foreach (var z in zones)
            {
                if (z.CasterAnchored) auras.Add((z.OwnerRaw, Mathf.RoundToInt(z.Radius / AuraRadiusBucket), z.Effect, z.Channels));
                else statics.Add(("AbilityZone", z.Position));
            }
        }

        /// <summary>Order-independent hash of the Caster-anchored zones (owner, radius bucket, effect, channels): <c>auras=</c>.</summary>
        public static uint AuraHash(IEnumerable<(uint owner, int radiusBucket, byte effect, byte channels)> auras, out int count)
        {
            uint sum = 0;
            count = 0;
            foreach (var (owner, radius, effect, channels) in auras)
            {
                count++;
                uint h = 2166136261u;
                h = Fnv(h, "Aura");
                h = Fnv(h, unchecked((int)owner));
                h = Fnv(h, radius);
                h = Fnv(h, effect);
                h = Fnv(h, channels);
                unchecked { sum += h; }
            }
            return sum;
        }

        private static uint Fnv(uint h, string s)
        {
            unchecked
            {
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
                h ^= '|'; h *= 16777619u;
            }
            return h;
        }

        private static uint Fnv(uint h, int v)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++) { h ^= (uint)((v >> (8 * i)) & 0xFF); h *= 16777619u; }
            }
            return h;
        }
    }
}
