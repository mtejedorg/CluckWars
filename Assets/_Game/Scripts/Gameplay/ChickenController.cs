using CluckWars.Networking;
using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Top-level networked chicken. Owns movement only in Phase 1 — combat / cargo /
    /// abilities will hang off this same GameObject as separate components later.
    /// </summary>
    /// <remarks>
    /// State authority drives motion via the Fusion input buffer. Pure-MonoBehaviour
    /// helpers (animator, visuals) stay local and react to <c>[Networked]</c> state.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ChickenController : NetworkBehaviour
    {
        [SerializeField] private ChickenStatsSO _stats;

        private CharacterController _characterController;
        private ChickenMovement _movement;

        public ChickenStatsSO Stats => _stats;

        public override void Spawned()
        {
            _characterController = GetComponent<CharacterController>();
            if (_stats == null)
            {
                Debug.LogError($"[ChickenController] {name} has no ChickenStatsSO assigned. Assign one on the prefab.");
                return;
            }
            _movement = new ChickenMovement(_characterController, _stats);
        }

        public override void FixedUpdateNetwork()
        {
            if (_movement == null) return;
            if (!HasStateAuthority) return;

            if (GetInput<PlayerNetworkInput>(out var input))
            {
                _movement.Tick(input.Movement, Runner.DeltaTime);
            }
        }
    }
}
