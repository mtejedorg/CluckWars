using System.Collections.Generic;
using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Owns the "caster ignores terrain it can traverse" window (ADR 0003 Slice 2).
    /// Plain C#, owned and ticked by <see cref="ChickenController"/> — the same
    /// shape as <see cref="ChickenMovement"/>, so it needs no prefab wiring and
    /// cannot be forgotten on a variant.
    /// </summary>
    /// <remarks>
    /// <para><b>Why per-collider <c>Physics.IgnoreCollision</c> and not a layer swap.</b>
    /// Two alternatives were rejected:</para>
    /// <list type="number">
    ///   <item>Moving the caster to a non-colliding layer. The chicken's layer
    ///   (<c>Chickens</c>, 8) is what every ability's <c>SearchMask</c>, and
    ///   <c>ChickenController.CheckCollisionSlow</c>, scan for. A caster that
    ///   changed layer would become untargetable by Peck / Sneaky Steal / Cluck
    ///   Shock for the duration — an invisible combat bug for a movement feature.
    ///   It would also need a 3×3 block of new layers plus collision-matrix rows,
    ///   because boundary walls and interior obstacles currently share one layer.</item>
    ///   <item>Disabling the obstacle's collider. That is global — it would let
    ///   every other chicken through the same rock.</item>
    /// </list>
    /// <para>Per-pair ignoring is local to this caster, needs no project settings, and
    /// (verified on Unity 6000.3) works on a <see cref="CharacterController"/>: the
    /// pair survives toggling either collider's <c>enabled</c> flag, and calling it
    /// against a disabled or deactivated collider neither throws nor logs.</para>
    ///
    /// <para><b>Boundary containment is structural.</b> The ignore set is built by
    /// asking each candidate collider for a <see cref="TerrainObstacle"/> or a
    /// <see cref="FoodPile"/> above it. Boundary walls
    /// (<c>MapGenerator.CreateWall</c>) carry neither, so no tier — present or
    /// future — can put one in the set. See <see cref="IsClearable"/>.</para>
    ///
    /// <para><b>Never restore while overlapping.</b> When the ability window ends the
    /// caster may be standing inside a rock. Restoring collision there would trap
    /// it in geometry, which is a far worse bug than a missed vault. So the window
    /// does not end on the ability's schedule: it downgrades to an <i>unstick</i>
    /// state that keeps the caster phased AND actively pushes it out along the
    /// shortest exit, terminating as soon as nothing overlaps. Both of the options
    /// the task offered (persist, or push out) are used together on purpose —
    /// persisting alone never terminates for a caster that cannot move (dead and
    /// stunned inside a rock; <c>ChickenCombat.Respawn</c> does not teleport), and
    /// pushing out alone would restore collision on the tick the push starts.</para>
    /// </remarks>
    public sealed class ChickenTraversal
    {
        private const string Source = "Traversal";

        /// <summary>
        /// How far ahead of the capsule obstacles are picked up. Without a lookahead
        /// the caster collides for one tick before the pair is ignored, which reads
        /// as a stutter at the wall instead of a clean vault.
        /// </summary>
        private const float ScanLookahead = 1.25f;

        /// <summary>Push-out speed (world-units/sec) while unsticking.</summary>
        private const float EjectSpeed = 3f;

        /// <summary>
        /// Ticks of unsticking after which we start complaining. Not a cap — giving
        /// up would restore collision inside geometry — but a stuck caster must be
        /// visible in the log rather than silently phasing forever.
        /// </summary>
        private const int UnstickWarnTicks = 180; // ~3s at 60Hz

        private static readonly Collider[] ScanHits = new Collider[32];

        private readonly CharacterController _cc;
        private readonly Transform _transform;
        private readonly ILogService _log;

        /// <summary>Colliders this caster is currently ignoring. Every one of these must be restored.</summary>
        private readonly List<Collider> _ignored = new List<Collider>(8);

        private TerrainTraversal _tier;
        private bool _windowOpen;
        private int _unstickTicks;

        public ChickenTraversal(CharacterController characterController, ILogService log)
        {
            _cc = characterController;
            _transform = characterController != null ? characterController.transform : null;
            _log = log;
        }

        /// <summary>The tier currently granted, or <c>None</c> when nothing is phased.</summary>
        public TerrainTraversal Tier => _tier;

        /// <summary>True once the ability window closed but the caster is still inside geometry.</summary>
        public bool IsUnsticking => _tier != TerrainTraversal.None && !_windowOpen;

        // ---- Window lifecycle --------------------------------------------------

        /// <summary>
        /// Opens (or re-opens) a traversal window. Deliberately additive: an existing
        /// ignore set is kept rather than restored, because restoring it here could
        /// re-solidify a wall the caster is standing inside. Everything is released
        /// together once nothing overlaps.
        /// </summary>
        public void Begin(TerrainTraversal tier)
        {
            if (tier == TerrainTraversal.None) return;

            _tier = tier;
            _windowOpen = true;
            _unstickTicks = 0;
            Scan();

            _log?.Debug(Source, $"Traversal window open: {tier} (ignoring {_ignored.Count} collider(s)).");
        }

        /// <summary>
        /// The ability window elapsed. Restores immediately when the caster is clear;
        /// otherwise drops into unstick until it is.
        /// </summary>
        public void End()
        {
            if (_tier == TerrainTraversal.None) return;
            _windowOpen = false;
            _unstickTicks = 0;
            TickUnstick(0f); // release on this very tick when nothing overlaps
        }

        /// <summary>
        /// Hard release, no unstick. Only for exits where the caster's position is
        /// about to become irrelevant — despawn, and the match restart that teleports
        /// every chicken back to its corner in the same routine.
        /// </summary>
        public void Abort()
        {
            if (_tier == TerrainTraversal.None && _ignored.Count == 0) return;
            _log?.Debug(Source, $"Traversal aborted ({_tier}); restoring {_ignored.Count} collider(s).");
            Restore();
        }

        /// <summary>
        /// One <c>FixedUpdateNetwork</c> of bookkeeping. Must be called on the state
        /// authority before any of <see cref="ChickenController"/>'s other early
        /// returns — a caster that is stunned, dead, bot-driven or waiting on a
        /// finished match still has to get its collision back.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_tier == TerrainTraversal.None) return;

            if (_windowOpen) { Scan(); return; }
            TickUnstick(deltaTime);
        }

        // ---- Internals ---------------------------------------------------------

        /// <summary>
        /// Adds every clearable collider near the caster to the ignore set. Runs only
        /// while a window is open, so the per-tick overlap query costs nothing during
        /// normal play.
        /// </summary>
        private void Scan()
        {
            if (_cc == null || _transform == null) return;

            float radius = EffectiveRadius() + ScanLookahead;
            int count = Physics.OverlapSphereNonAlloc(
                CapsuleCentre(), radius, ScanHits, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var col = ScanHits[i];
                if (col == null || col == _cc) continue;
                if (!IsClearable(_tier, col)) continue;
                if (_ignored.Contains(col)) continue;

                Physics.IgnoreCollision(_cc, col, true);
                _ignored.Add(col);
            }
        }

        /// <summary>
        /// Keeps the caster phased while it overlaps anything it was let through, and
        /// pushes it toward the shortest exit. Releases the moment nothing overlaps.
        /// </summary>
        private void TickUnstick(float deltaTime)
        {
            if (!TryComputeExit(out var exit))
            {
                Restore();
                return;
            }

            _unstickTicks++;
            if (_unstickTicks == UnstickWarnTicks)
            {
                // Phasing forever is the failure mode this whole class exists to
                // avoid — never let it happen quietly.
                _log?.Warn(Source, $"Still unsticking after {UnstickWarnTicks} ticks — the caster has been " +
                    $"overlapping traversable geometry since its {_tier} window ended and is still phased. " +
                    "Check the push-out direction against the obstacle it is inside.");
            }

            if (deltaTime > 0f && _cc != null && _cc.enabled)
            {
                // Done here rather than through ChickenMovement on purpose: the cases
                // that need the push most (dead, stunned, rooted, match over) all
                // early-return before ChickenController ever ticks movement.
                _cc.Move(exit * (EjectSpeed * deltaTime));
            }
        }

        /// <summary>
        /// Normalised XZ direction out of the geometry the caster is inside, or
        /// <c>false</c> when it overlaps nothing and the window can close.
        /// </summary>
        private bool TryComputeExit(out Vector3 exit)
        {
            exit = Vector3.zero;
            if (_cc == null || _transform == null) return false;

            var pos = _transform.position;
            var rot = _transform.rotation;
            var deepest = Vector3.zero;
            float deepestDepth = 0f;
            bool overlapping = false;

            for (int i = _ignored.Count - 1; i >= 0; i--)
            {
                var col = _ignored[i];
                if (col == null) { _ignored.RemoveAt(i); continue; } // destroyed: nothing to restore

                if (!Physics.ComputePenetration(
                        _cc, pos, rot,
                        col, col.transform.position, col.transform.rotation,
                        out var dir, out float depth))
                    continue;
                if (depth <= 0f) continue;

                overlapping = true;
                dir.y = 0f; // never eject a chicken upward out of a rock
                if (dir.sqrMagnitude < 0.0001f) continue;

                dir.Normalize();
                exit += dir * depth;
                if (depth > deepestDepth) { deepestDepth = depth; deepest = dir; }
            }

            if (!overlapping) return false;

            // Opposite walls can cancel out; fall back to the deepest single exit,
            // then to facing, so the push is never a zero vector.
            if (exit.sqrMagnitude < 0.0001f) exit = deepest;
            if (exit.sqrMagnitude < 0.0001f) exit = _transform.forward;
            exit.y = 0f;
            exit = exit.sqrMagnitude > 0.0001f ? exit.normalized : Vector3.forward;
            return true;
        }

        /// <summary>Re-enables collision against everything in the set and closes the window.</summary>
        private void Restore()
        {
            for (int i = 0; i < _ignored.Count; i++)
            {
                var col = _ignored[i];
                // A destroyed collider has already dropped its pair; a disabled one is
                // safe to call against (verified — no throw, no error log).
                if (col == null || _cc == null) continue;
                Physics.IgnoreCollision(_cc, col, false);
            }

            if (_ignored.Count > 0)
                _log?.Debug(Source, $"Traversal closed ({_tier}); restored {_ignored.Count} collider(s) " +
                    $"after {_unstickTicks} unstick tick(s).");

            _ignored.Clear();
            _tier = TerrainTraversal.None;
            _windowOpen = false;
            _unstickTicks = 0;
        }

        private float EffectiveRadius()
        {
            var s = _transform.lossyScale;
            return _cc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }

        private Vector3 CapsuleCentre() => _transform.TransformPoint(_cc.center);

        // ---- Classification (pure enough to unit-test) --------------------------

        /// <summary>
        /// May <paramref name="tier"/> pass through <paramref name="col"/>?
        ///
        /// This is the single gate into the ignore set, and the only reason arena
        /// containment holds: it clears a collider ONLY when a
        /// <see cref="TerrainObstacle"/> or a <see cref="FoodPile"/> sits on it or
        /// above it. A boundary wall is a bare primitive collider with neither, so
        /// it always answers false — including for <see cref="TerrainTraversal.Blink"/>,
        /// which clears every obstacle class. Do not "simplify" this into a layer or
        /// height test; height is the visual expression of a class, never its
        /// definition (ADR 0003 Decision 5).
        /// </summary>
        public static bool IsClearable(TerrainTraversal tier, Collider col)
        {
            if (tier == TerrainTraversal.None || col == null) return false;

            var obstacle = col.GetComponentInParent<TerrainObstacle>();
            if (obstacle != null) return TraversalRules.Clears(tier, obstacle.Class);

            // Pile blockers are children of the FoodPile root.
            if (col.GetComponentInParent<FoodPile>() != null) return TraversalRules.ClearsPiles(tier);

            return false;
        }
    }
}
