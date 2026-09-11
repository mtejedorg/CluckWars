using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure-C# movement helper. No MonoBehaviour, no networking — just translates a
    /// 2D input vector into a <see cref="CharacterController.Move"/> displacement
    /// using stats + ability modifiers from the owning <see cref="ChickenController"/>.
    /// Owned by <see cref="ChickenController"/> and ticked from <c>FixedUpdateNetwork</c>.
    /// </summary>
    /// <remarks>
    /// Phase 6: reads <c>MoveSpeedMultiplier</c> and <c>MovementLocked</c> from the
    /// owner so abilities (Speed Burst, Egg Shell, …) can modulate movement without
    /// touching this class. Gravity always runs even when locked so a frozen
    /// chicken still stays grounded.
    ///
    /// v0.3: also reads <c>SlowMultiplier</c> (collision + pile + ability slow
    /// sources), <c>Rooted</c> (blocks planar motion but allows ability casts, unlike
    /// <c>MovementLocked</c>), and <c>ExternalDisplacement</c> (knockback impulse,
    /// decayed to zero exponentially each tick).
    ///
    /// This class holds no per-tick simulation state of its own. Everything it integrates
    /// across ticks — <c>ExternalDisplacement</c>, <c>VerticalVelocity</c> — lives on the
    /// owning <see cref="ChickenController"/> as <c>[Networked]</c> or owner-held state and is
    /// read and written back through <c>_owner</c>, because a plain field here would sit
    /// outside Fusion's predicted state and drift under resimulation. Keep it that way: if a
    /// new accumulator is needed, it belongs on the controller, not in a field here.
    /// </remarks>
    public sealed class ChickenMovement
    {
        private readonly CharacterController _controller;
        private readonly ChickenController _owner;
        private readonly Transform _transform;

        // ExternalDisplacement (knockback) decays toward zero at this rate (1/s).
        // Tune during the Part B balance pass. A value of 8 means the impulse
        // falls to ~1% of its initial value in about 0.58 seconds.
        private const float KnockbackDecayRate = 8f;

        public ChickenMovement(CharacterController controller, ChickenController owner)
        {
            _controller = controller;
            _owner = owner;
            _transform = controller.transform;
        }

        /// <param name="input">Camera-relative world-space XZ direction, already yaw-rotated
        /// by <c>FusionNetworkService.OnInput</c> — see <paramref name="aimRotateOnly"/>.</param>
        /// <param name="deltaTime">Simulation delta time.</param>
        /// <param name="aimRotateOnly">
        /// v0.6 hold-to-aim (FEEDBACK.md §2.3): true while the caster is charging a
        /// directional ability (Cone/ForwardCircle/Jump aim shape). The stick still
        /// steers <em>facing</em> — the chicken turns to aim — but planar translation is
        /// suppressed; gravity and knockback keep running exactly as normal so a rooted-
        /// in-place chicken doesn't float or clip through the floor while aiming.
        /// </param>
        public void Tick(Vector2 input, float deltaTime, bool aimRotateOnly = false)
        {
            var stats = _owner.Stats;
            if (stats == null) return;

            // Planar input is ignored when movement is locked (Egg Shell, Turtle Mode, …),
            // when rooted (Rooted blocks horizontal motion but allows ability casts), or
            // while aim-rotating a directional hold (translation suppressed, facing only).
            var planar = Vector3.zero;
            if (!aimRotateOnly && !_owner.MovementLocked && !_owner.Rooted)
            {
                planar = new Vector3(input.x, 0f, input.y);
                if (planar.sqrMagnitude > 1f) planar.Normalize();
            }

            // Gravity always runs so we stay grounded — even while locked or rooted.
            // The accumulator lives on the owner as [Networked] state so resimulation
            // restores it; see ChickenController.VerticalVelocity for why that matters.
            if (_controller.isGrounded && _owner.VerticalVelocity < 0f)
                _owner.VerticalVelocity = -2f;
            else
                _owner.VerticalVelocity += Physics.gravity.y * deltaTime;

            float speedMult = _owner.MoveSpeedMultiplier;
            if (_owner.UnderdogSurgeActive)
            {
                speedMult *= 1.4f;
            }

            // Speed = base × ability multiplier × slow multiplier (all are ≥ 0).
            var speed = stats.MoveSpeed
                * Mathf.Max(0f, speedMult)
                * Mathf.Max(0f, _owner.SlowMultiplier);

            var displacement = planar * speed;
            displacement.y = _owner.VerticalVelocity;

            // External displacement (knockback / push) is a velocity in world-units/sec.
            // Applied additively, decays exponentially toward zero.
            if (_owner.ExternalDisplacement.sqrMagnitude > 0.001f)
            {
                displacement += _owner.ExternalDisplacement;
                _owner.ExternalDisplacement = Vector3.Lerp(
                    _owner.ExternalDisplacement, Vector3.zero, KnockbackDecayRate * deltaTime);
            }

            _controller.Move(displacement * deltaTime);

            ClampInsideArena();

            // Face movement direction — or, while aim-rotating, the raw stick direction
            // (planar is deliberately zeroed above in that case, so read the input directly).
            // Skip while locked, rooted (aim-rotating is never both true — see the
            // ControlRules.CanMove gate at the ChickenController call site), or with no input.
            Vector3 faceDir = aimRotateOnly ? new Vector3(input.x, 0f, input.y) : planar;
            if (!_owner.MovementLocked && (aimRotateOnly || !_owner.Rooted) && faceDir.sqrMagnitude > 0.01f)
            {
                var targetRot = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
                _transform.rotation = Quaternion.RotateTowards(
                    _transform.rotation,
                    targetRot,
                    stats.TurnSpeed * deltaTime);
            }
        }

        /// <summary>
        /// Hard geometric guarantee that a chicken is inside the arena, applied every tick
        /// after the <see cref="CharacterController"/> has moved.
        /// </summary>
        /// <remarks>
        /// <b>This deliberately does not trust the boundary colliders.</b> Maestro, 2026-08-19:
        /// a chicken was found wedged in the edge wall at match start. A <c>CharacterController</c>
        /// against a thin <c>BoxCollider</c> can tunnel or wedge for reasons that are not bugs in
        /// this code — a <c>deltaTime</c> spike, a knockback landing on the same tick as a wall
        /// contact, or depenetration resolving along the wrong axis in a corner where two walls
        /// meet. Clamping is arithmetic, not a physics query, so it cannot fail to apply.
        ///
        /// <b>Two correction paths, and the split is load-bearing.</b> The obvious implementation
        /// — disable the controller, write <c>transform.position</c>, re-enable — is what
        /// <see cref="Gameplay.ChickenController.RPC_TeleportTo"/> and <c>ChickenTraversal</c> use,
        /// but those fire ONCE on an event. This runs every tick, and a player holding into a
        /// corner makes it fire on consecutive ticks indefinitely. Toggling <c>enabled</c>
        /// invalidates the controller's cached ground state, so <c>isGrounded</c> at the top of
        /// <see cref="Tick"/> can read false while the chicken is plainly standing on the floor —
        /// which sends gravity down the accumulating branch instead of the pegged -2, building a
        /// downward velocity every tick until the body is fast enough to tunnel the ground. That
        /// would have re-created the very failure class this method exists to prevent, on the Y
        /// axis, under exactly the corner-pocket condition that was reported.
        ///
        /// So the ordinary case routes through <see cref="CharacterController.Move"/>, which
        /// preserves grounded and collision state. Only a correction too large to be an ordinary
        /// overshoot — a body genuinely outside the wall, where <c>Move</c> would be refused by
        /// the surface it is trying to come back through — falls back to the teleport idiom.
        ///
        /// The limit is the wall's INNER FACE pulled in by <see cref="PinwheelLayout.ChickenRadius"/>,
        /// which INCLUDES skin width, so the body rests against the surface rather than sinking
        /// into it. <c>JumpResolver.BodyClearance</c> is the same radius WITHOUT skin width, so a
        /// legal jump may land ~0.08 m closer than this allows and get nudged out on the next
        /// tick. That is this method doing its job, not the two disagreeing: both now derive from
        /// <see cref="PinwheelLayout.ChickenControllerRadius"/>.
        ///
        /// Y is untouched — vertical movement is gravity and jump arcs, which the arena bounds
        /// have nothing to say about.
        /// </remarks>
        /// <summary>
        /// Largest clamp correction still treated as an ordinary overshoot and applied through
        /// <see cref="CharacterController.Move"/>. Above this the body is assumed to be genuinely
        /// outside the arena rather than merely pressed against its edge. One chicken diameter:
        /// anything smaller cannot have carried the body past a wall it was already touching.
        /// </summary>
        private const float MaxMoveCorrection = PinwheelLayout.ChickenDiameter;

        private void ClampInsideArena()
        {
            var p = _transform.position;
            var clamped = PinwheelLayout.ClampIntoArena(p, MapGenerator.ArenaHalfSize);
            if (clamped == p) return;

            float cx = clamped.x;
            float cz = clamped.z;
            var correction = new Vector3(cx - p.x, 0f, cz - p.z);

            if (correction.sqrMagnitude <= MaxMoveCorrection * MaxMoveCorrection)
            {
                // Ordinary overshoot. Move() keeps the controller's ground state intact, which
                // the gravity gate depends on — see the remarks above.
                _controller.Move(correction);
                return;
            }

            // Genuinely outside. Move() would have to push back through the wall it escaped
            // past and would be refused, so bypass the controller for this one write.
            _controller.enabled = false;
            _transform.position = new Vector3(cx, p.y, cz);
            _controller.enabled = true;
        }

    }
}
