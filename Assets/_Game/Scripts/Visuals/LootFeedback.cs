using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>What the loot mark shows on this peer this frame. All clear unless the local player is the Thief or the marked rival (<see cref="LootPresentationRules.Resolve"/>).</summary>
    public struct LootOutputs
    {
        /// <summary>The local player is a Thief with a live mark on a living rival.</summary>
        public bool AsThief;
        /// <summary>The bag is drawn on the marked rival (the Thief, and the rival is not hidden).</summary>
        public bool MarkerShown;
        public bool TargetHidden;
        public LootBlocker Blocker;
        /// <summary>Planar metres from the Thief to the marked rival.</summary>
        public float Distance;
        /// <summary>Steal's reach, read from the Steal ability (0 when the kit has none).</summary>
        public float Reach;
        public bool InReach;
        /// <summary>Gold ground brackets (grey when <see cref="Blocker"/> is set).</summary>
        public bool BracketsShown;
        /// <summary>The bag's pop factor (1 at rest; below 1 for a hop after a mark moved).</summary>
        public float MarkerPop;
        /// <summary>A Thief's live mark is on the local player.</summary>
        public bool AsMarked;
        /// <summary>The marked rival's own badge and the bag on the Thief's arrow: 1 while marked, fading to 0 after (the old victim fades with no word).</summary>
        public float VictimAlpha;
        /// <summary>The local Thief's Steal-hex dot (independent of whether a mark is on a rival: it also shows "coming").</summary>
        public LootHexView Hex;
    }

    /// <summary>
    /// The Thief's loot mark presentation adapter (BL-219, design 2026-11-23 sections 11 and 22): every frame it copies the replicated mark state
    /// (<c>ThiefLoot.State</c>, <c>MarkedTarget</c>) of every chicken into <see cref="LootSeat"/>s, asks <see cref="LootPresentationRules.Resolve"/> who the local
    /// player is, and publishes <see cref="Current"/> for the HUD layers (<c>MarkHudController</c> for the bag marker, the arrow bag and the victim's badge,
    /// <c>TouchControlsController</c> for the Steal-hex dot, <see cref="RivalIndicator"/> for the marked rival's arrow). It also draws the ground brackets, plays the
    /// pops (<c>MARKED!</c>, <c>LOOT +n</c>, <c>SAFE</c>) and the buzzes.
    /// </summary>
    /// <remarks>
    /// <b>Only the Thief and the marked rival see anything</b> (owner, 2026-11-24): the one gate is <see cref="LootPresentationRules.Resolve"/>, a bystander's
    /// outputs are all clear, and the event listeners bind to the local chicken alone. Local presentation only: no RPC, no networked field, nothing written.
    /// One scene-level object like <see cref="MarkFeedback"/>, created by the first <see cref="RivalIndicator"/> to wake, kept across scene loads and hard-cleared
    /// on any scene unload. The clock prize has no feedback of its own (owner: "se queda, pero sin feedback propio por ahora").
    /// </remarks>
    [DefaultExecutionOrder(MarkFeedback.ExecutionOrder + 1)]
    public sealed class LootFeedback : MonoBehaviour
    {
        private const string Source = "LootFeedback";

        private static LootFeedback _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Called by every <see cref="RivalIndicator"/> as it wakes. Play mode only.</summary>
        public static void EnsureForMatch()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("LootFeedback");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LootFeedback>();
        }

        private static readonly LootOutputs Clean = new LootOutputs { MarkerPop = 1f };

        /// <summary>This frame's presentation; all clear with no instance.</summary>
        public static LootOutputs Current => _instance != null ? _instance._out : Clean;

        /// <summary>The marked rival while the local Thief's bag is drawn (Unity null once it despawned).</summary>
        public static ChickenController Target => _instance != null ? _instance._target : null;

        /// <summary>The Thief whose mark is on the local player.</summary>
        public static ChickenController Thief => _instance != null ? _instance._thief : null;

        /// <summary>Is <paramref name="chicken"/> the local Thief's marked rival with the bag showing? A hidden (Hide) rival is not: it hides the mark as it hides the bird.</summary>
        public static bool IsMarkedByLocalThief(ChickenController chicken) =>
            _instance != null && chicken != null && chicken == _instance._target && _instance._out.MarkerShown;

        /// <summary>The marked rival's arrow colour: the loot gold, or grey while Steal could not take it (wall, shell). False when no bag is showing.</summary>
        public static bool TryGetArrowTint(out Color tint)
        {
            tint = default;
            if (_instance == null || !_instance._out.MarkerShown) return false;
            tint = _instance._out.Blocker == LootBlocker.None ? FeedbackTuning.LootGoldColor : FeedbackTuning.NeutralNoEffectColor;
            return true;
        }

        /// <summary>The bounty a just-paid marked steal named, once, for <paramref name="chicken"/>'s own cargo pop (<see cref="LootPresentationRules.GrabAfterBounty"/>); 0 for anyone else.</summary>
        public static float TakePendingBounty(ChickenController chicken)
        {
            if (_instance == null || chicken == null || chicken != _instance._bound) return 0f;
            float bounty = _instance._pendingBounty;
            bool fresh = Time.frameCount - _instance._pendingFrame <= PendingBountyFrames;
            _instance._pendingBounty = 0f;
            return fresh ? bounty : 0f;
        }

        /// <summary>The cargo change of a paid steal lands in the same Render as its event; one frame of slack covers either order.</summary>
        private const int PendingBountyFrames = 2;

        /// <summary>A chicken's network id as one number, 0 for none (the key <see cref="LootSeat"/> carries).</summary>
        public static long IdOf(NetworkBehaviourId id) => id == NetworkBehaviourId.None ? 0L : ((long)id.Object.Raw << 16) | (uint)(id.Behaviour & 0xFFFF);

        private ILogService _log;
        private IHapticsService _haptics;
        private LootOutputs _out = Clean;
        private HuntBrackets _brackets;

        private readonly List<LootSeat> _seats = new List<LootSeat>(8);
        private readonly List<ChickenController> _refs = new List<ChickenController>(8);
        private ChickenController _target, _thief, _bound;
        private ThiefLoot _boundLoot;
        private long _lastTargetId, _lastThiefId;
        private float _hopAt = float.NegativeInfinity;
        private float _victimAlpha;
        private bool _reportedMissingLoot;
        private LootDotState _lastDot;
        private float _pendingBounty;
        private int _pendingFrame;

        [Inject]
        public void Construct(ILogService log, [InjectOptional] IHapticsService haptics)
        {
            _log = log;
            _haptics = haptics;
        }

        private void Awake()
        {
            SelfInject.Ensure(this, _log != null);
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _brackets = HuntBrackets.Create(transform, _log, "LootBrackets", reportsToStatics: false);
        }

        private void OnDisable() => HardClear();

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            HardClear();
            if (_instance == this) _instance = null;
        }

        private void OnSceneUnloaded(Scene _) => HardClear();

        private void HardClear()
        {
            Unbind();
            _out = Clean;
            _target = _thief = null;
            _lastTargetId = _lastThiefId = 0;
            _victimAlpha = 0f;
            _pendingBounty = 0f;
            if (_brackets != null) _brackets.Hide();
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            bool running = gm != null && gm.Object != null && gm.Object.IsValid && gm.IsMatchRunning;
            Gather();
            var roles = running ? LootPresentationRules.Resolve(_seats) : new LootRoles { Local = -1, Target = -1, Thief = -1 };
            var local = roles.Local >= 0 ? _refs[roles.Local] : null;
            Bind(local);

            bool reduced = PlayerPreferences.ReducedMotionEnabled;
            float now = Time.unscaledTime;
            var o = new LootOutputs { MarkerPop = 1f };
            _target = roles.AsThief ? _refs[roles.Target] : null;
            _thief = roles.AsMarked ? _refs[roles.Thief] : null;

            if (roles.AsThief && _target != null)
            {
                o.AsThief = true;
                o.TargetHidden = HiddenVisibilityRules.IsHiddenFor(_target);
                o.MarkerShown = LootPresentationRules.MarkerShown(true, o.TargetHidden);
                Vector3 me = local.transform.position, it = _target.transform.position;
                o.Distance = FloorArrowRules.PlanarDistance(me, it);
                o.Reach = StealReachOf(local);
                o.InReach = LootPresentationRules.InReach(o.Distance, o.Reach);
                o.Blocker = LootPresentationRules.BlockerFor(PlayerBase.WallBetween(me, it), !StealRules.CanStealFrom(_target));
                o.BracketsShown = LootPresentationRules.BracketsShown(o.MarkerShown, o.InReach);

                long id = IdOf(_target.Id);
                if (id != _lastTargetId)
                {
                    if (_lastTargetId != 0) _hopAt = now;   // the bag hopped from one bird to another
                    _lastTargetId = id;
                }
                o.MarkerPop = LootPresentationRules.HopPop(now - _hopAt, reduced);
            }
            else
            {
                _lastTargetId = 0;
            }

            if (roles.AsMarked && _thief != null)
            {
                o.AsMarked = true;
                long thiefId = IdOf(_thief.Id);
                if (thiefId != _lastThiefId)
                {
                    _lastThiefId = thiefId;
                    WarnLanded(local);
                }
            }
            else
            {
                _lastThiefId = 0;
            }
            _victimAlpha = LootPresentationRules.BadgeAlpha(_victimAlpha, o.AsMarked, Time.unscaledDeltaTime);
            o.VictimAlpha = _victimAlpha;

            o.Hex = HexFor(local, o);
            if (o.Hex.Dot != _lastDot)
            {
                _lastDot = o.Hex.Dot;
                _log?.Debug(Source, $"Steal hex loot dot: {o.Hex.Dot} (thief={o.AsThief}, marked={o.AsMarked}).");
            }
            _out = o;
            DriveBrackets(in o);
        }

        // ---- Gathering (replicated state -> seats) ---------------------------------------------------------------------------

        private void Gather()
        {
            _seats.Clear();
            _refs.Clear();
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                var loot = c.Loot;
                if (loot == null && !_reportedMissingLoot)
                {
                    _reportedMissingLoot = true;
                    _log?.Error(Source, $"{c.name} has no ThiefLoot: a Thief's marks on it are not shown (prefab wiring).");
                }
                bool lootValid = loot != null && loot.Object != null && loot.Object.IsValid;
                _seats.Add(new LootSeat
                {
                    Id = IdOf(c.Id),
                    IsLocal = c.HasInputAuthority,
                    Removed = c.Combat != null && c.Combat.IsRemoved,
                    ThiefActive = lootValid && loot.Facts.Active,
                    MarkLive = lootValid && loot.MarkLive,
                    MarkedId = lootValid ? IdOf(loot.MarkedTarget) : 0L,
                });
                _refs.Add(c);
            }
        }

        /// <summary>Steal's reach, read from the Steal ability in the local kit (<see cref="AbilityBaseSO.IndicatorRange"/>); 0 with no Steal slot.</summary>
        private static float StealReachOf(ChickenController local)
        {
            var abilities = local != null ? local.Abilities : null;
            if (abilities == null) return 0f;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                var a = abilities.GetSlot(i);
                if (a.Is(AbilityJob.Steal)) return a.IndicatorRange;
            }
            return 0f;
        }

        private static LootHexView HexFor(ChickenController local, in LootOutputs o)
        {
            var loot = local != null ? local.Loot : null;
            var abilities = local != null ? local.Abilities : null;
            if (loot == null || abilities == null || loot.Object == null || !loot.Object.IsValid) return default;
            var facts = loot.Facts;
            if (!facts.Active) return default;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                var a = abilities.GetSlot(i);
                if (!a.Is(AbilityJob.Steal)) continue;
                return LootHexRules.ViewFor(true, loot.State, abilities.CooldownRemaining(i), abilities.CooldownProgress01(i), facts.LeadSeconds, o.InReach);
            }
            return default;
        }

        // ---- Brackets ----------------------------------------------------------------------------------------------------------

        private void DriveBrackets(in LootOutputs o)
        {
            var target = _target;   // Unity null once the chicken despawned
            if (_brackets == null) return;
            if (!o.BracketsShown || target == null)
            {
                _brackets.Hide();
                return;
            }
            var indicator = target.Indicator();
            float ring = indicator != null ? indicator.RingWorldRadius : FeedbackTuning.RivalRingRadius * target.transform.lossyScale.x;
            Color colour = o.Blocker == LootBlocker.None ? FeedbackTuning.LootGoldColor : FeedbackTuning.NeutralNoEffectColor;
            colour.a = 1f;
            _brackets.ApplyColour(colour, target.transform.position, ring, 1f);
        }

        // ---- Pops and buzzes ----------------------------------------------------------------------------------------------------

        /// <summary>The marked rival's warning: MARKED! over its own bird (a self-pop, so no rival ever draws it) and a short buzz.</summary>
        private void WarnLanded(ChickenController local)
        {
            if (local == null) return;
            FloatingCombatText.SpawnSelfPop(local, UiKeys.HudPopMarked, FeedbackTuning.LootGoldColor);
            _haptics?.Buzz(HapticKind.HuntLive);
        }

        private void Bind(ChickenController local)
        {
            if (local == _bound) return;
            Unbind();
            _bound = local;
            if (_bound == null) return;
            _bound.LocalEvents.LootTaken += OnLootTaken;
            _boundLoot = _bound.Loot;
            if (_boundLoot != null) _boundLoot.MarkedStealRefused += OnMarkedStealRefused;
        }

        private void Unbind()
        {
            if (_bound != null) _bound.LocalEvents.LootTaken -= OnLootTaken;
            if (_boundLoot != null) _boundLoot.MarkedStealRefused -= OnMarkedStealRefused;
            _bound = null;
            _boundLoot = null;
        }

        /// <summary>The local Thief's marked steal paid: <c>LOOT +n</c> over its own bird, the bounty that fitted (from the event, not the asset), and a strong buzz.
        /// The event is the local chicken's own, so no other Thief's payment is ever drawn here.</summary>
        private void OnLootTaken(LootTakenEvent e)
        {
            if (_bound == null || !LootPresentationRules.RewardShown(e.Bounty)) return;
            int n = Mathf.RoundToInt(e.Bounty);
            FloatingCombatText.SpawnSelfPop(_bound, UiKeys.HudPopLootTake, FeedbackTuning.LootGoldColor, ("n", n));
            _haptics?.Buzz(HapticKind.HuntPounce);
            _pendingBounty = e.Bounty;
            _pendingFrame = Time.frameCount;
        }

        /// <summary>A wall or a shell refused the marked steal: SAFE over the victim, in the neutral no-effect grey (the word the Hunt and Steal already use).</summary>
        private void OnMarkedStealRefused(ChickenController victim)
        {
            if (victim == null) return;
            FloatingCombatText.Spawn(victim, victim.PopAnchor(), UiText.Get(UiKeys.HudPopSafe), FeedbackTuning.NeutralNoEffectColor);
        }
    }
}
