using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.UI;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>What the local Warrior sees on one rival this frame (<see cref="WarriorFeedback.TagAt"/>). Plain values; the HUD draws them.</summary>
    public struct WarriorTag
    {
        public ChickenController Rival;
        /// <summary>The Enforcer's fresh marker is on: the rival is fresh for this Enforcer (mid Peck cast, or just after a Peck that landed food).</summary>
        public bool FreshShown;
        /// <summary>The fresh window's fill, 1 down to 0 (<see cref="WarriorFeedbackRules.FreshFill01"/>).</summary>
        public float FreshFill;
        /// <summary>Debts the local Enforcer holds on the rival.</summary>
        public int Debts;
        /// <summary>The debts' decay timer's fill, 1 down to 0.</summary>
        public float DecayFill;
        /// <summary>The local Bully just took from the rival and is locked against it: a second hit takes nothing yet.</summary>
        public bool LockShown;
        /// <summary>The lock's fill, 1 down to 0.</summary>
        public float LockFill;

        public bool Any => FreshShown || Debts > 0 || LockShown;
    }

    /// <summary>What the local player sees outside the rival tags this frame.</summary>
    public struct WarriorOutputs
    {
        /// <summary>Debts an Enforcer holds on the local player (the largest count when two do); 0 shows nothing.</summary>
        public int OweCount;
        /// <summary>The owed debts' decay fill, 1 down to 0.</summary>
        public float OweFill;
        /// <summary>The colour of the Enforcer the cue names (its player colour).</summary>
        public Color OweColor;
        /// <summary>Debt pips the cue draws: the Enforcer's cap, inside <see cref="FeedbackTuning.WarriorPipSlotsMax"/>.</summary>
        public int OwePipSlots;
        /// <summary>Debt pips every tag draws (the local Enforcer's cap, inside <see cref="FeedbackTuning.WarriorPipSlotsMax"/>); 0 for a Bully or anyone else.</summary>
        public int TagPipSlots;
        /// <summary>The food the local Enforcer's next Steal would take from the nearest carrier in its reach; 0 with no Enforcer or no carrier in reach.</summary>
        public int StealAmount;
    }

    /// <summary>
    /// The Warrior kits' presentation adapter (BL-227, design 2026-11-25 section 16, slice G9): every frame it reads the replicated state the two kits already
    /// publish (<see cref="EnforcerDebts.CountOn"/>, <see cref="EnforcerDebts.DecayLeftOn"/>, <see cref="AbilityController.IsFreshFor"/>,
    /// <see cref="ChickenHitSteal.LockLeftOn"/>) into <see cref="WarriorTag"/>s for the HUD layer (<c>MarkHudController</c>) and the Steal hex, and it plays the
    /// kits' pops from the local chicken's <see cref="ChickenLocalEvents"/> (<c>HitStealTaken</c>, <c>FreshHit</c>, <c>DebtGained</c>, <c>DebtCashed</c>).
    /// </summary>
    /// <remarks>
    /// <b>Own view only.</b> The fresh marker, the debt pips and the lock show for the local Enforcer or Bully and nobody else; the "you owe" cue shows for
    /// the local victim alone; bystanders see nothing (CODE-DESIGN-19 by analogy). The events are the local chicken's own, so no other Warrior's pop is
    /// ever drawn here. Local presentation only: no RPC, no networked field, nothing written. One scene-level object like <see cref="LootFeedback"/>, created
    /// by the first <see cref="RivalIndicator"/> to wake, kept across scene loads and cleared on any scene unload.
    /// <b>No per-frame allocation:</b> the tag array is fixed; text is formatted only on an event.
    /// <b>The food pops split the cargo gain</b> as the Thief's <c>LOOT +n</c> does: a hit-steal's take and an Enforcer's cash bonus are named by the kit's own
    /// pop, and <see cref="TakePendingFood"/> tells the plain <c>+n</c> pop (<c>HitFeedback.OnCargoChanged</c>) what is already said, so one food gain is never drawn twice.
    /// </remarks>
    [DefaultExecutionOrder(MarkFeedback.ExecutionOrder + 2)]
    public sealed class WarriorFeedback : MonoBehaviour
    {
        private static WarriorFeedback _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Called by every <see cref="RivalIndicator"/> as it wakes. Play mode only.</summary>
        public static void EnsureForMatch()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("WarriorFeedback");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WarriorFeedback>();
        }

        private static readonly WarriorOutputs Clean = default;

        /// <summary>This frame's presentation outside the tags; all clear with no instance.</summary>
        public static WarriorOutputs Current => _instance != null ? _instance._out : Clean;

        /// <summary>Rivals that carry a tag this frame.</summary>
        public static int TagCount => _instance != null ? _instance._tagCount : 0;

        /// <summary>The <paramref name="i"/>-th tag of this frame (0 to <see cref="TagCount"/> - 1). The rival may be a Unity null once it despawned: check it.</summary>
        public static WarriorTag TagAt(int i) => _instance != null && i >= 0 && i < _instance._tagCount ? _instance._tags[i] : default;

        /// <summary>
        /// The food a kit pop just named, once, for <paramref name="chicken"/>'s own cargo pop (<see cref="WarriorFeedbackRules.GainAfterPopped"/>); 0 for anyone
        /// else and for a stale amount. Consumed by the first cargo change that reads it.
        /// </summary>
        public static float TakePendingFood(ChickenController chicken)
        {
            if (_instance == null || chicken == null || chicken != _instance._bound) return 0f;
            float food = _instance._pendingFood;
            bool fresh = WarriorFeedbackRules.PendingFresh(Time.frameCount, _instance._pendingFrame, FeedbackTuning.WarriorPendingFoodFrames);
            _instance._pendingFood = 0f;
            return fresh ? food : 0f;
        }

        private ILogService _log;
        private IHapticsService _haptics;
        private WarriorOutputs _out;
        private readonly WarriorTag[] _tags = new WarriorTag[EnforcerDebts.Slots + 4];
        private int _tagCount;
        private ChickenController _bound;
        private float _pendingFood;
        private int _pendingFrame;
        // The last FreshHit pop: its victim and frame, so the debt that same hit stacked does not pop a second word over the same rival.
        private long _freshPopVictim;
        private int _freshPopFrame = -10;

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
            _tagCount = 0;
            _pendingFood = 0f;
        }

        private static bool Usable(ChickenController c) =>
            c != null && c.Object != null && c.Object.IsValid && !c.IsDecoy && (c.Combat == null || !c.Combat.IsRemoved);

        private void Update()
        {
            var gm = GameManager.Instance;
            bool running = gm != null && gm.Object != null && gm.Object.IsValid && gm.IsMatchRunning;
            ChickenController local = null;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.HasInputAuthority && c.Object != null && c.Object.IsValid && !c.IsDecoy) { local = c; break; }
            }
            Bind(local);

            _tagCount = 0;
            _out = Clean;
            if (!running || !Usable(local)) return;

            var debts = local.Debts;
            var hits = local.Hits;
            bool enforcer = debts != null && debts.Object != null && debts.Object.IsValid && debts.Facts.IsEnforcer;
            bool bully = hits != null && hits.Object != null && hits.Object.IsValid && hits.Facts.Active;
            var passive = local.Passive;
            BiteFacts bite = passive != null ? passive.Bite : BiteFacts.None;
            DebtFacts debtFacts = enforcer ? debts.Facts : DebtFacts.None;
            int pipSlots = WarriorFeedbackRules.PipSlots(debtFacts.Cap, FeedbackTuning.WarriorPipSlotsMax);
            float lockSeconds = bully ? hits.Facts.LockSeconds : 0f;

            if (enforcer || bully)
            {
                _out.TagPipSlots = enforcer ? pipSlots : 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (c == local || !Usable(c) || HiddenVisibilityRules.IsHiddenFor(c)) continue;   // a hidden rival shows nothing, as it shows no bird
                    var tag = new WarriorTag { Rival = c };
                    if (enforcer)
                    {
                        var peck = c.Abilities;
                        if (peck != null && bite.Active && peck.IsFreshFor(bite.FreshSeconds))
                        {
                            tag.FreshShown = true;
                            tag.FreshFill = WarriorFeedbackRules.FreshFill01(peck.IsPeckCasting, peck.SecondsSinceFed, bite.FreshSeconds);
                        }
                        tag.Debts = debts.CountOn(c);
                        if (tag.Debts > 0) tag.DecayFill = WarriorFeedbackRules.Fill01(debts.DecayLeftOn(c), debtFacts.Seconds);
                    }
                    if (bully)
                    {
                        float left = hits.LockLeftOn(c);
                        if (left > 0f)
                        {
                            tag.LockShown = true;
                            tag.LockFill = WarriorFeedbackRules.Fill01(left, lockSeconds);
                        }
                    }
                    if (tag.Any && _tagCount < _tags.Length) _tags[_tagCount++] = tag;
                }
            }

            if (enforcer) _out.StealAmount = StealAmountFor(local, debts, all);
            ReadOwe(local, all);
        }

        /// <summary>The food the Steal would take from the nearest carrier in its reach (the debt-inflated ask, clamped as the cargo clamps it); 0 with none.</summary>
        private static int StealAmountFor(ChickenController local, EnforcerDebts debts, IReadOnlyList<ChickenController> all)
        {
            var abilities = local.Abilities;
            var cargo = local.Cargo;
            var passive = local.Passive;
            if (abilities == null || cargo == null || passive == null) return 0;
            AbilityBaseSO steal = null;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                var a = abilities.GetSlot(i);
                if (a.Is(AbilityJob.Steal)) { steal = a; break; }
            }
            if (steal == null) return 0;

            float reach = steal.IndicatorRange;
            ChickenController best = null;
            float bestDistance = float.MaxValue;
            Vector3 me = local.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == local || !Usable(c) || c.Cargo == null || !(c.Cargo.Stealable > 0f) || !StealRules.CanStealFrom(c) || HiddenVisibilityRules.IsHiddenFor(c)) continue;
                float d = FloorArrowRules.PlanarDistance(me, c.transform.position);
                if (d > reach || d >= bestDistance || PlayerBase.WallBetween(me, c.transform.position)) continue;
                best = c;
                bestDistance = d;
            }
            if (best == null) return 0;
            float asked = debts.StealAmountOn(best, passive.ModifyStealAmount(steal.NominalStealAmount, local));
            return WarriorFeedbackRules.HexAmount(WarriorFeedbackRules.ClampedSteal(asked, best.Cargo.Stealable, cargo.FreeSpace));
        }

        /// <summary>The local victim's cue: the Enforcer holding the most debts on it (the first on a tie), its colour, the count and the decay.</summary>
        private void ReadOwe(ChickenController local, IReadOnlyList<ChickenController> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == local || !Usable(c)) continue;
                var d = c.Debts;
                if (d == null || d.Object == null || !d.Object.IsValid || !d.Facts.IsEnforcer) continue;
                int count = d.CountOn(local);
                if (count <= _out.OweCount) continue;
                _out.OweCount = count;
                _out.OweFill = WarriorFeedbackRules.Fill01(d.DecayLeftOn(local), d.Facts.Seconds);
                _out.OweColor = PlayerPalette.ForCorner(c.HomeCornerIndex);
                _out.OwePipSlots = WarriorFeedbackRules.PipSlots(d.Facts.Cap, FeedbackTuning.WarriorPipSlotsMax);
            }
        }

        // ---- Pops and buzzes ------------------------------------------------------------------------------------------------------

        private void Bind(ChickenController local)
        {
            if (local == _bound) return;
            Unbind();
            _bound = local;
            if (_bound == null) return;
            var e = _bound.LocalEvents;
            e.HitStealTaken += OnHitStealTaken;
            e.FreshHit += OnFreshHit;
            e.DebtGained += OnDebtGained;
            e.DebtCashed += OnDebtCashed;
        }

        private void Unbind()
        {
            if (_bound != null)
            {
                var e = _bound.LocalEvents;
                e.HitStealTaken -= OnHitStealTaken;
                e.FreshHit -= OnFreshHit;
                e.DebtGained -= OnDebtGained;
                e.DebtCashed -= OnDebtCashed;
            }
            _bound = null;
        }

        private bool TryFindRival(NetworkBehaviourId id, out ChickenController rival)
        {
            rival = null;
            if (_bound == null || _bound.Runner == null) return false;
            return _bound.Runner.TryFindBehaviour(id, out rival) && Usable(rival);
        }

        private void NamePendingFood(float food)
        {
            _pendingFood += food;
            _pendingFrame = Time.frameCount;
        }

        /// <summary>The local Bully's hit took food: <c>TAKE +n</c> over its own bird, the amount the hit credited (from the event), and a short tick.</summary>
        private void OnHitStealTaken(HitStealTakenEvent e)
        {
            if (_bound == null || !WarriorFeedbackRules.PopShown(e.Take)) return;
            FloatingCombatText.SpawnSelfPop(_bound, UiKeys.HudPopHitTake, FeedbackTuning.WarriorDebtColor, ("n", Mathf.RoundToInt(e.Take)));
            _haptics?.Buzz(HapticKind.HuntLive);
            NamePendingFood(e.Take);
        }

        /// <summary>The local Enforcer's hit spent a rival's fresh state: <c>FRESH!</c> over that rival. No count: a Steal that cashes in the same moment can already have spent it.</summary>
        private void OnFreshHit(FreshHitEvent e)
        {
            if (_bound == null || !TryFindRival(e.Victim, out var victim)) return;
            _freshPopVictim = LootFeedback.IdOf(e.Victim);
            _freshPopFrame = Time.frameCount;
            FloatingCombatText.Spawn(victim, victim.PopAnchor(), UiText.Get(UiKeys.HudPopFresh), FeedbackTuning.WarriorFreshColor);
        }

        /// <summary>The local Enforcer put a debt on a rival: <c>OWES xn</c> over it, unless this same hit already popped <c>FRESH!</c> there, or a cash has already spent the count.</summary>
        private void OnDebtGained(DebtGainedEvent e)
        {
            if (_bound == null || _bound.Debts == null || !TryFindRival(e.Victim, out var victim)) return;
            if (_freshPopFrame == Time.frameCount && _freshPopVictim == LootFeedback.IdOf(e.Victim)) return;
            int held = _bound.Debts.CountOn(victim);
            if (held <= 0) return;
            FloatingCombatText.Spawn(victim, victim.PopAnchor(), UiText.Format(UiKeys.HudPopDebt, ("n", held)), FeedbackTuning.WarriorDebtColor);
        }

        /// <summary>The local Enforcer's Steal cashed debts: <c>PAY UP +n</c> over its own bird (the food the debts added) and a strong buzz.</summary>
        private void OnDebtCashed(DebtCashedEvent e)
        {
            if (_bound == null || !WarriorFeedbackRules.PopShown(e.Bonus)) return;
            FloatingCombatText.SpawnSelfPop(_bound, UiKeys.HudPopDebtCash, FeedbackTuning.WarriorDebtColor, ("n", Mathf.RoundToInt(e.Bonus)));
            _haptics?.Buzz(HapticKind.HuntPounce);
            NamePendingFood(e.Bonus);
        }
    }
}
