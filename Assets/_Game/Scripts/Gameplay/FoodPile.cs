using CluckWars.Logging;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// A networked food pile. State authority owns <see cref="Amount"/>; chickens drain it
    /// via <see cref="RPC_Drain"/> from any client. Visual feedback is local
    /// (<c>FoodPileVisuals</c>) and reacts to networked-state changes.
    /// </summary>
    /// <remarks>
    /// Scene-placed (baked NetworkObject); the master client becomes StateAuthority on
    /// session start. Spawn is left to the runner's scene-load path — no MatchBootstrapper
    /// changes needed for Phase 4.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FoodPile : NetworkBehaviour
    {
        private const string Source = "FoodPile";

        [Tooltip("Amount of food the pile starts with and will be visually scaled against.")]
        [Min(0f)]
        [SerializeField] private float _initialAmount = 30f;

        [Tooltip("How close a chicken must be (chicken position to pile position) to drain.")]
        [Min(0f)]
        [SerializeField] private float _collectRadius = 1.6f;

        [Header("Blocking — stocked piles are solid, so chases route around them")]
        [Tooltip("Solid collider radius. Must stay < CollectRadius minus the chicken capsule radius (~0.5) so collection still triggers from the edge. The center pile's 1.5× root scale scales this too — keep the margin.")]
        [Min(0.2f)]
        [SerializeField] private float _blockerRadius = 0.65f;
        [Tooltip("Solid collider height. Above the NavMesh step height (0.75) so bots path around, not over.")]
        [Min(0.8f)]
        [SerializeField] private float _blockerHeight = 1.2f;

        public static readonly System.Collections.Generic.List<FoodPile> ActivePiles = new System.Collections.Generic.List<FoodPile>();

        [Networked] public float Amount { get; set; }
        [Networked] public float MaxAmount { get; set; }
        [Networked] public float VisualScale { get; set; }

        public float CollectRadius => _collectRadius;
        public bool IsEmpty => Amount <= 0f;

        private ILogService _log;
        private GameObject _blocker;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            ActivePiles.Add(this);
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            float scale = VisualScale <= 0f ? 1f : VisualScale;
            transform.localScale = Vector3.one * scale;

            CreateBlocker();

            if (HasStateAuthority)
            {
                // Only seed the prefab default if the spawner didn't preset Amount /
                // MaxAmount via onBeforeSpawned (e.g., MapGenerator wants a bigger
                // center pile than the small ones around it).
                if (MaxAmount <= 0f)
                {
                    Amount = _initialAmount;
                    MaxAmount = _initialAmount;
                }
                _log?.Debug(Source, $"{name}: Spawned. Amount={Amount}/{MaxAmount}.");
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActivePiles.Remove(this);
        }

        /// <summary>
        /// Solid capsule + NavMesh carve, built in code so the prefab needs no
        /// re-authoring. Local on every peer — Amount is networked, so all peers
        /// agree on whether the pile blocks.
        /// </summary>
        private void CreateBlocker()
        {
            if (_blocker != null) return;
            _blocker = new GameObject("Blocker");
            _blocker.transform.SetParent(transform, worldPositionStays: false);
            _blocker.transform.localPosition = Vector3.zero;

            var cap = _blocker.AddComponent<CapsuleCollider>();
            cap.radius = _blockerRadius;
            cap.height = _blockerHeight;
            cap.center = Vector3.up * (_blockerHeight * 0.5f);

            var carve = _blocker.AddComponent<NavMeshObstacle>();
            carve.shape = NavMeshObstacleShape.Capsule;
            carve.radius = _blockerRadius;
            carve.height = _blockerHeight;
            carve.center = cap.center;
            carve.carving = true;
        }

        public override void Render()
        {
            // Empty piles stop blocking (the leftover stub is walkable, and the
            // NavMesh un-carves); refilled piles (match restart) turn solid again.
            if (_blocker != null && _blocker.activeSelf == IsEmpty)
                _blocker.SetActive(!IsEmpty);
        }

        /// <summary>
        /// Asks the pile's state authority to remove up to <paramref name="amount"/> food.
        /// Caller is expected to have already credited their own cargo for the same amount;
        /// any over-request (pile empty / under-supplied) just clamps to whatever's left.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Drain(float amount)
        {
            if (amount <= 0f || Amount <= 0f) return;
            var actual = Mathf.Min(amount, Amount);
            Amount -= actual;
            if (Amount < 0f) Amount = 0f;
            _log?.Verbose(Source, $"{name}: drained {actual:0.00} → {Amount:0.0}/{MaxAmount}.");
        }
    }
}
