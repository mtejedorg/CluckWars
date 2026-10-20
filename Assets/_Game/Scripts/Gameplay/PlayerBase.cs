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

        [Header("Base kit (Phase 8 C S2) - visual only")]
        [Tooltip("Parent of the pad dressing (straw floor, ring, pennant), authored for corner 0. Spawned() yaws it by " +
            "-90 degrees per corner so every seat gets the same layout relative to its own corner (KitYawFor).")]
        [SerializeField] private Transform _kit;

        [Tooltip("Renderers drawn in the full player colour (the pad ring). With the pennant flag, nothing else on the base is tinted at full strength.")]
        [SerializeField] private Renderer[] _identityRenderers = System.Array.Empty<Renderer>();

        [Tooltip("The pennant flag: an UNLIT cloth, so the sun and the side the camera sees never decide its colour (Phase 8 D7). Takes the player colour times Flag Lift, see FlagColorFor.")]
        [SerializeField] private Renderer[] _flagRenderers = System.Array.Empty<Renderer>();

        [Tooltip("Brightness the unlit flag is lifted by so it reads like the sunlit ring (an unlit surface skips the sun's contribution the ring gets). Channels clamp at 1, so the flag can never bloom.")]
        [Range(1f, MaxFlagLift)]
        [SerializeField] private float _flagLift = DefaultFlagLift;

        [Tooltip("Renderers that take only a hint of the player colour (the straw floor): white lerped toward the colour by Floor Tint.")]
        [SerializeField] private Renderer[] _floorRenderers = System.Array.Empty<Renderer>();

        [Tooltip("How far the floor is pulled from white toward the player colour (0 = plain straw). Capped at 15 % so the straw stays straw.")]
        [Range(0f, MaxFloorTint)]
        [SerializeField] private float _floorTint = 0.12f;

        /// <summary>Upper bound for <see cref="_floorTint"/>: the straw floor carries at most a 15 % hint of the player colour.</summary>
        public const float MaxFloorTint = 0.15f;

        /// <summary>Upper bound for <see cref="_flagLift"/>.</summary>
        public const float MaxFlagLift = 1.6f;

        /// <summary>Shipped flag lift (Phase 8 D7): chosen by a sweep (1.0 / 1.15 / 1.3 / 1.45) so the rendered flag lands within 10 dE00 of the sunlit half of the rendered pad ring, all four seats, both tiers (1.3: 3.5-6.3 dE00) (Phase8ChunkD7PennantTests pins the setup, tools/ui_capture/capture_d7_pennant.py measures it).</summary>
        public const float DefaultFlagLift = 1.3f;

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

        /// <summary>
        /// Where the spawner put this base, stamped in <c>onBeforeSpawned</c> (<c>MapGenerator.SpawnBases</c>) because Fusion
        /// does not replicate the spawn position of an object without a NetworkTransform (<see cref="SpawnPlacement"/>).
        /// </summary>
        [Networked] public Vector3 SpawnPosition { get; set; }

        /// <summary><see cref="SpawnPosition"/> was stamped (a base may legitimately stand at the origin).</summary>
        [Networked] public NetworkBool HasSpawnPosition { get; set; }

        // True once this peer has applied a stamped spawn position (or holds the authority, whose transform is the truth).
        private bool _placed;

        public float DepositRadius => _depositRadius;

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

            // A base not spawned through MapGenerator (scene-baked, Ability Lab) stamps its own spot for the other peers.
            if (HasStateAuthority && !HasSpawnPosition)
            {
                SpawnPosition = transform.position;
                HasSpawnPosition = true;
            }
            ApplySpawnPosition();

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
            _placed = false;   // pooled: the next life re-applies its own stamp
        }

        /// <summary>
        /// Moves a non-authority copy to the spawner's position (<see cref="SpawnPlacement"/>) and lays the kit out for that
        /// corner. Local, read-only toward networked state. Retried from <see cref="LateUpdate"/> until a stamp has been seen.
        /// </summary>
        private void ApplySpawnPosition()
        {
            if (SpawnPlacement.ProxyMustMove(HasStateAuthority, HasSpawnPosition, SpawnPosition, transform.position))
            {
                transform.position = SpawnPosition;
                _log?.Debug(Source, $"{name}: moved to its spawn position {SpawnPosition} (corner {CornerIndex}).");
            }
            _placed = HasStateAuthority || HasSpawnPosition;
            // Local, visual-only: every peer lays the kit out for the corner the base really stands in.
            if (_kit != null) _kit.rotation = Quaternion.Euler(0f, KitYawFor(transform.position), 0f);
        }

        // LateUpdate polls Owner every frame — O(1) PlayerRef comparison.
        // More reliable than ChangeDetector+Render in GameMode.Single where the
        // simulation and render buffers are the same peer and DetectChanges can
        // silently skip locally-written networked properties.
        private void LateUpdate()
        {
            if (Object == null || !Object.IsValid) return;
            if (!_placed) ApplySpawnPosition();
            if (_tintInitialized && Owner == _lastOwnerForTint && (bool)BotClaimed == _lastBotClaimedForTint) return;

            _tintInitialized       = true;
            _lastOwnerForTint      = Owner;
            _lastBotClaimedForTint = BotClaimed;
            ApplyOwnerTint();
        }

        // Reused across tints — ApplyOwnerTint only runs when the owner changes, but there is
        // no reason to allocate a block each time.
        private MaterialPropertyBlock _tintBlock;

        /// <summary>
        /// Tints the base kit with the player's identity colour (corner-indexed), grey while the base is unclaimed:
        /// <see cref="_identityRenderers"/> (ring) at full strength, <see cref="_flagRenderers"/> (pennant flag) by
        /// <see cref="FlagColorFor"/>, <see cref="_floorRenderers"/> (straw) by <see cref="FloorTintFor"/>. Property blocks only, so the renderers keep their <c>sharedMaterial</c>.
        /// The pennant pole and the scene props (coop, nest, barrel) are deliberately left untinted.
        /// </summary>
        /// <remarks>
        /// Never touch <c>renderer.material</c> here: it clones a <c>Lit (Instance)</c> copy per
        /// renderer (Phase 8 A2). URP Lit honours per-renderer property blocks for
        /// <c>_BaseColor</c>; the block costs the SRP batcher for these few renderers, which
        /// is accepted. <c>_Color</c> is written as well for any shader that still reads it.
        /// </remarks>
        private void ApplyOwnerTint()
        {
            var color = IsClaimed
                ? CluckWars.UI.PlayerPalette.ForCorner(CornerIndex)
                : UnownedColor;

            _log?.Debug(Source, $"{name}: ApplyOwnerTint - {_identityRenderers.Length} identity + {_flagRenderers.Length} flag + " +
                $"{_floorRenderers.Length} floor renderer(s), color={color}, corner={CornerIndex}, owner={Owner}.");

            _tintBlock ??= new MaterialPropertyBlock();
            ApplyKitTint(_identityRenderers, _flagRenderers, _flagLift, _floorRenderers, _floorTint, color, _tintBlock);
        }

        /// <summary>
        /// The kit tint itself, separated from the networked claim state so it can be exercised in EditMode:
        /// <paramref name="identity"/> get <paramref name="color"/>, <paramref name="flags"/> get
        /// <see cref="FlagColorFor"/>(<paramref name="color"/>, <paramref name="flagLift"/>), <paramref name="floor"/> get
        /// <see cref="FloorTintFor"/>(<paramref name="color"/>, <paramref name="floorTint"/>). Null entries are skipped.
        /// </summary>
        public static void ApplyKitTint(Renderer[] identity, Renderer[] flags, float flagLift, Renderer[] floor, float floorTint, Color color, MaterialPropertyBlock block)
        {
            foreach (var r in identity)
                if (r != null) ApplyTint(r, block, color);

            var flagColor = FlagColorFor(color, flagLift);
            foreach (var r in flags)
                if (r != null) ApplyTint(r, block, flagColor);

            var floorColor = FloorTintFor(color, floorTint);
            foreach (var r in floor)
                if (r != null) ApplyTint(r, block, floorColor);
        }

        /// <summary>
        /// The unlit pennant flag's colour: <paramref name="playerColor"/> scaled by <paramref name="lift"/> (clamped to <see cref="MaxFlagLift"/>),
        /// each channel capped at 1 so the flag never exceeds white, and so never reaches the PC bloom threshold (1).
        /// </summary>
        public static Color FlagColorFor(Color playerColor, float lift)
        {
            float k = Mathf.Clamp(lift, 1f, MaxFlagLift);
            return new Color(Mathf.Min(1f, playerColor.r * k), Mathf.Min(1f, playerColor.g * k), Mathf.Min(1f, playerColor.b * k), 1f);
        }

        /// <summary>The straw floor's colour: white pulled toward <paramref name="playerColor"/> by <paramref name="strength"/>, clamped to <see cref="MaxFloorTint"/>.</summary>
        public static Color FloorTintFor(Color playerColor, float strength)
        {
            var c = Color.Lerp(Color.white, playerColor, Mathf.Clamp(strength, 0f, MaxFloorTint));
            c.a = 1f;
            return c;
        }

        /// <summary>
        /// Yaw (degrees) that turns the corner-0-authored kit to the corner this base sits in: 0 for the (+x,+z) corner,
        /// -90 per corner counter-clockwise (the same 4-fold rotation as the Map.unity base dressing). 0 at the origin.
        /// </summary>
        public static float KitYawFor(Vector3 basePosition)
        {
            var flat = new Vector2(basePosition.x, basePosition.z);
            if (flat.sqrMagnitude < 1e-6f) return 0f;
            return Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg - 45f;
        }

        /// <summary>
        /// Writes <paramref name="color"/> into <paramref name="renderer"/>'s property block
        /// (<c>_BaseColor</c>, plus <c>_Color</c> when its shared material has one), preserving
        /// any other overrides already in the block. Leaves <c>sharedMaterial</c> untouched.
        /// </summary>
        public static void ApplyTint(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(SBaseColorId, color);
            var shared = renderer.sharedMaterial;
            if (shared == null || shared.HasProperty(SColorId))
                block.SetColor(SColorId, color);
            renderer.SetPropertyBlock(block);
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
