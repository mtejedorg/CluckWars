using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// A networked deposit zone. Chickens within <see cref="DepositRadius"/> dump their
    /// cargo into <see cref="FoodTotal"/> via <see cref="RPC_AddFood"/>. State authority
    /// owns the running total — Phase 7 will read this for win-condition checks.
    /// </summary>
    /// <remarks>
    /// Phase 4 ships a single shared base for solo testing. Per-player ownership and
    /// proper allegiance checks land alongside the win condition in Phase 7.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerBase : NetworkBehaviour
    {
        private const string Source = "PlayerBase";

        [Tooltip("How close a chicken must be to deposit. Stays generous for placeholder geometry.")]
        [Min(0f)]
        [SerializeField] private float _depositRadius = 2.5f;

        [Networked] public float FoodTotal { get; set; }
        [Networked] public PlayerRef Owner { get; set; }

        /// <summary>
        /// Which corner this base represents (0..3). Set by <c>MapGenerator</c> at
        /// spawn time via <c>onBeforeSpawned</c>; pairs with
        /// <c>MapGenerator.SpawnPoints[CornerIndex]</c> so a player joining at
        /// corner N is also assigned the base at corner N.
        ///
        /// <c>GameManager.AssignBasesToPlayers</c> looks for a CornerIndex match
        /// first and falls back to first-unowned, so scene-baked legacy bases
        /// (which all default to 0) still receive an owner.
        /// </summary>
        [Networked] public int CornerIndex { get; set; }

        public float DepositRadius => _depositRadius;

        // Per-player identity colors — corner-indexed, matching MatchHud / ART.md §6.
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // corner 0 — Orange
            new Color(0.10f, 0.50f, 0.77f, 1f), // corner 1 — Blue
            new Color(0.77f, 0.16f, 0.44f, 1f), // corner 2 — Pink
            new Color(0.05f, 0.62f, 0.48f, 1f), // corner 3 — Teal
        };
        private static readonly Color UnownedColor = new Color(0.42f, 0.42f, 0.42f, 1f);

        // Shader property IDs — cached once so SetPropertyBlock is alloc-free.
        private static readonly int SColorId     = Shader.PropertyToID("_Color");
        private static readonly int SBaseColorId = Shader.PropertyToID("_BaseColor");

        private ILogService _log;

        // Tracks the last owner we tinted for — drives the LateUpdate poll.
        // _tintInitialized stays false until the NetworkObject is live so the
        // very first valid frame always triggers an apply (even if Owner is still
        // PlayerRef.None, which gives the grey unowned tint).
        private PlayerRef _lastOwnerForTint;
        private bool      _tintInitialized;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            _log?.Debug(Source, $"{name}: Spawned. HasStateAuthority={HasStateAuthority}, " +
                $"Owner={Owner}, CornerIndex={CornerIndex}.");
        }

        // LateUpdate polls Owner every frame — O(1) PlayerRef comparison.
        // More reliable than ChangeDetector+Render in GameMode.Single where the
        // simulation and render buffers are the same peer and DetectChanges can
        // silently skip locally-written networked properties.
        private void LateUpdate()
        {
            if (Object == null || !Object.IsValid) return;
            if (_tintInitialized && Owner == _lastOwnerForTint) return;

            _tintInitialized  = true;
            _lastOwnerForTint = Owner;
            ApplyOwnerTint();
        }

        /// <summary>
        /// Tints every <see cref="MeshRenderer"/> on this base with the player's
        /// identity color (corner-indexed). Sets both <c>_Color</c> (Standard /
        /// Built-in pipeline) and <c>_BaseColor</c> (URP) via a
        /// <see cref="MaterialPropertyBlock"/> to avoid creating material instances,
        /// then also writes directly to <c>material.color</c> as a fallback for
        /// shaders that don't honour PropertyBlock overrides.
        /// </summary>
        private void ApplyOwnerTint()
        {
            var color = Owner.IsRealPlayer
                ? PlayerColors[CornerIndex % PlayerColors.Length]
                : UnownedColor;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(SColorId,     color);
            mpb.SetColor(SBaseColorId, color);

            var renderers = GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            _log?.Debug(Source, $"{name}: ApplyOwnerTint — {renderers.Length} renderer(s), " +
                $"color={color}, corner={CornerIndex}, owner={Owner}.");

            foreach (var r in renderers)
            {
                r.SetPropertyBlock(mpb);
                // Direct material fallback — handles shaders that ignore PropertyBlock
                // for the base color (some URP variants, custom shaders, etc.).
                // Creates a per-instance material copy but there are only 4 bases.
                r.material.color = color;
                r.material.SetColor(SBaseColorId, color);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddFood(float amount)
        {
            if (amount <= 0f) return;
            FoodTotal += amount;
            _log?.Debug(Source, $"{name}: +{amount:0.00} → FoodTotal={FoodTotal:0.0}.");
        }
    }
}
