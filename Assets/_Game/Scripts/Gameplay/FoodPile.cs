using CluckWars.Logging;
using Fusion;
using UnityEngine;
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

        public static readonly System.Collections.Generic.List<FoodPile> ActivePiles = new System.Collections.Generic.List<FoodPile>();

        [Networked] public float Amount { get; set; }
        [Networked] public float MaxAmount { get; set; }

        public float CollectRadius => _collectRadius;
        public bool IsEmpty => Amount <= 0f;

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            ActivePiles.Add(this);
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

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
