using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// One player's networked deposit zone, pinned to a corner of the arena. Chickens whose
    /// <c>HomeCornerIndex</c> matches this base's <see cref="CornerIndex"/> and who are inside
    /// <see cref="DepositRadius"/> bank their cargo into <see cref="FoodTotal"/>; the state
    /// authority owns the running total and <c>GameManager</c> reads it for the win condition.
    /// </summary>
    /// <remarks>
    /// Four bases exist, one per corner, spawned by the master client in
    /// <c>MapGenerator.SpawnBases</c> with <see cref="CornerIndex"/> stamped via
    /// <c>onBeforeSpawned</c>. A base is in play once a human owns it (<see cref="Owner"/>,
    /// assigned by <c>GameManager.AssignBasesToPlayers</c>) or a bot claims it
    /// (<see cref="BotClaimed"/>) — see <see cref="IsClaimed"/>.
    /// <para>
    /// <b>Two ways in, and the difference is trust.</b> <see cref="RPC_AddFood"/> is the
    /// untrusted client-facing path — any peer can call it, so it validates the sender against
    /// <see cref="BaseDepositRules"/> before crediting anything. <see cref="AddFoodAuthoritative"/>
    /// is the direct write for code already running on this base's state authority, which has no
    /// proximity semantics to validate. Don't route trusted awards through the RPC: they would
    /// have to fake a depositor standing at the base to get past its checks.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerBase : NetworkBehaviour
    {
        private const string Source = "PlayerBase";

        [Tooltip("How close a chicken must be to deposit. Stays generous for placeholder geometry.")]
        [Min(0f)]
        [SerializeField] private float _depositRadius = 2.5f;

        public static readonly System.Collections.Generic.List<PlayerBase> ActiveBases = new System.Collections.Generic.List<PlayerBase>();

        [Networked] public float FoodTotal { get; set; }
        [Networked] public PlayerRef Owner { get; set; }

        /// <summary>
        /// True when a solo-mode bot claims this base. Bots share
        /// <c>PlayerRef.None</c> input authority so <see cref="Owner"/> can't carry
        /// their identity — this flag makes the base count for win checks and tinting.
        /// Set by <c>GameManager.AssignBasesToPlayers</c>.
        /// </summary>
        [Networked] public NetworkBool BotClaimed { get; set; }

        /// <summary>A base is in play when a human owns it or a bot claims it.</summary>
        public bool IsClaimed => Owner.IsRealPlayer || BotClaimed;

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
        private MatchConfigSO _matchConfig;

        // Tracks the last owner we tinted for — drives the LateUpdate poll.
        // _tintInitialized stays false until the NetworkObject is live so the
        // very first valid frame always triggers an apply (even if Owner is still
        // PlayerRef.None, which gives the grey unowned tint).
        private PlayerRef _lastOwnerForTint;
        private bool      _lastBotClaimedForTint;
        private bool      _tintInitialized;

        // MatchConfigSO is bound in ProjectInstaller (project scope, see GameInstaller's note),
        // so the plain ProjectContext self-inject below resolves both parameters.
        [Inject]
        public void Construct(ILogService log, MatchConfigSO matchConfig)
        {
            _log = log;
            _matchConfig = matchConfig;
        }

        public override void Spawned()
        {
            ActiveBases.Add(this);
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            if (_matchConfig == null)
            {
                _log?.Error(Source, $"{name}: no MatchConfigSO resolved, so RPC_AddFood cannot bound " +
                    "the deposit amount and will accept any size a client sends. Check that " +
                    "ProjectInstaller._matchConfig is assigned.");
            }

            _log?.Debug(Source, $"{name}: Spawned. HasStateAuthority={HasStateAuthority}, " +
                $"Owner={Owner}, CornerIndex={CornerIndex}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveBases.Remove(this);
        }

        // LateUpdate polls Owner every frame — O(1) PlayerRef comparison.
        // More reliable than ChangeDetector+Render in GameMode.Single where the
        // simulation and render buffers are the same peer and DetectChanges can
        // silently skip locally-written networked properties.
        private void LateUpdate()
        {
            if (Object == null || !Object.IsValid) return;
            if (_tintInitialized && Owner == _lastOwnerForTint && (bool)BotClaimed == _lastBotClaimedForTint) return;

            _tintInitialized       = true;
            _lastOwnerForTint      = Owner;
            _lastBotClaimedForTint = BotClaimed;
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
            var color = IsClaimed
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

        /// <summary>
        /// Credits <paramref name="amount"/> directly, for callers already running on this
        /// base's state authority — today only <c>GameManager.AwardMatchEndBonuses</c>, which
        /// banks a passive's match-end bonus into a possibly-distant home base from inside its
        /// own authority-gated tick.
        /// </summary>
        /// <remarks>
        /// Bases are spawned by the master client (<c>MapGenerator.SpawnBases</c>) and
        /// <c>GameManager</c> runs its authority tick on that same peer, so the direct write is
        /// legal. It exists because such an award has no proximity or allegiance semantics to
        /// check — routing it through <see cref="RPC_AddFood"/> would mean inventing a depositor
        /// standing at the base just to satisfy validation the award was never subject to.
        /// </remarks>
        /// <returns>False when this peer is not the authority and the award was dropped.</returns>
        public bool AddFoodAuthoritative(float amount)
        {
            if (amount <= 0f) return false;

            if (!HasStateAuthority)
            {
                _log?.Warn(Source, $"{name}: AddFoodAuthoritative({amount:0.00}) ignored — this peer " +
                    "does not hold the base's state authority, so the award is lost. It should only " +
                    "be called from authority-gated code on the peer that spawned the bases.");
                return false;
            }

            FoodTotal += amount;
            _log?.Debug(Source, $"{name}: +{amount:0.00} (authoritative) → FoodTotal={FoodTotal:0.0}.");
            return true;
        }

        /// <summary>
        /// The untrusted deposit path: a chicken's <c>ChickenCargo.FlushBaseDeposit</c> asking this
        /// base's authority to bank a batched cargo flush.
        /// </summary>
        /// <remarks>
        /// <c>RpcSources.All</c> means *any* peer can call this on *any* base, so everything it is
        /// handed is a claim to be checked, not a fact. <paramref name="info"/> is filled in by
        /// Fusion (existing call sites pass nothing) and carries the sender —
        /// <see cref="TryResolveDepositor"/> turns that into the chicken the caller is entitled to
        /// speak for, and refuses to credit anything without one.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddFood(float amount, RpcInfo info = default)
        {
            if (!IsPlausibleAmount(amount, out string amountRejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_AddFood({amount:0.00}) from {info.Source} — {amountRejection}");
                return;
            }

            if (!TryResolveDepositor(info, out var depositor))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_AddFood({amount:0.00}) from {info.Source} — " +
                    $"no chicken that caller is entitled to speak for is standing at corner " +
                    $"{CornerIndex} with a matching HomeCornerIndex.");
                return;
            }

            FoodTotal += amount;
            _log?.Debug(Source, $"{name}: +{amount:0.00} from {depositor.name} → FoodTotal={FoodTotal:0.0}.");
        }

        /// <summary>
        /// Magnitude check, with the reason a rejection happened so the caller can log it.
        /// </summary>
        private bool IsPlausibleAmount(float amount, out string rejection)
        {
            if (amount <= 0f)
            {
                rejection = "the amount is not positive.";
                return false;
            }

            // Without a MatchConfigSO there is no honest bound to derive, so the magnitude check
            // is skipped rather than guessed at. Spawned() has already logged that as an Error,
            // and the depositor checks below still stand on their own.
            if (_matchConfig == null)
            {
                rejection = null;
                return true;
            }

            float max = BaseDepositRules.MaxSingleDeposit(_matchConfig.DepositRatePerSecond);
            if (amount > max)
            {
                rejection = $"it exceeds the largest possible single deposit flush ({max:0.00}).";
                return false;
            }

            rejection = null;
            return true;
        }

        /// <summary>
        /// Finds the chicken the RPC's sender is entitled to deposit with: one parked at this
        /// base, stamped to this base's corner, and attributable to the caller.
        /// </summary>
        /// <remarks>
        /// <b>Bots are why attribution is not just an InputAuthority comparison.</b> Bot chickens
        /// spawn with <c>PlayerRef.None</c> input authority and are simulated by the master
        /// (<c>MatchBootstrapper.TrySpawnBots</c>), so a bot's deposit does not arrive stamped with
        /// the bot's own identity. It arrives as a <em>local</em> invocation on the master, because
        /// the master is both the bot's state authority and this base's. That is the signal used:
        /// on a local invoke, accept any chicken this peer already simulates (which covers bots and
        /// the master's own chicken alike); on a message from the wire, require the chicken's input
        /// authority to be the sender. A remote peer can never forge <c>IsInvokeLocal</c>, so a
        /// compromised master still cannot bank food into a base it is not standing at.
        /// </remarks>
        private bool TryResolveDepositor(in RpcInfo info, out ChickenController depositor)
        {
            depositor = null;

            // A local invocation has no wire time; asking for a remote player's RTT would be
            // meaningless (and Source may be None).
            float latency = info.IsInvokeLocal || Runner == null
                ? 0f
                : (float)Runner.GetPlayerRtt(info.Source);

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var chicken = all[i];
                if (chicken == null || chicken.Object == null || !chicken.Object.IsValid) continue;
                if (chicken.IsDecoy) continue; // a phantom carries no cargo and banks nothing
                if (!BaseDepositRules.IsAllegianceMatch(chicken.HomeCornerIndex, CornerIndex)) continue;

                bool attributable = info.IsInvokeLocal
                    ? chicken.Object.HasStateAuthority
                    : chicken.Object.InputAuthority == info.Source;
                if (!attributable) continue;

                float moveSpeed = chicken.Stats != null ? chicken.Stats.MoveSpeed : 0f;
                if (!BaseDepositRules.IsWithinDepositRange(
                        transform.position, chicken.transform.position, DepositRadius,
                        BaseDepositRules.RangeMargin(moveSpeed, latency)))
                {
                    continue;
                }

                depositor = chicken;
                return true;
            }

            return false;
        }
    }
}
