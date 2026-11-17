using CluckWars.Injection;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Settings;
using CluckWars.UI;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The Death Mark presentation adapter: every frame it copies the replicated execute state (<c>AssassinExecute.MarkedTarget</c>,
    /// its timers, <c>CanKillNow</c>), positions, control state and visibility into <see cref="MarkInputs"/>, steps the pure
    /// <see cref="MarkPresentationModel"/>, and hands the outputs on: the victim's screen mood (<see cref="MarkScreenEffect"/>),
    /// the stings (<see cref="IAudioService"/>), and <see cref="Current"/> for the HUD layers (<c>MarkHudController</c>,
    /// <c>MatchHudController</c>, <c>TouchControlsController</c>, <see cref="RivalIndicator"/>, <see cref="AbilitySlotOverlay"/>).
    /// </summary>
    /// <remarks>
    /// Local presentation only: no RPC, no networked field. One scene-root object created by the first
    /// <see cref="RivalIndicator"/> to wake (<see cref="EnsureForMatch"/>) and kept across scene loads; any scene unload, disable
    /// or destroy hard-clears the model and the screen mood, so nothing outlives the match.
    /// </remarks>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class MarkFeedback : MonoBehaviour
    {
        /// <summary>Before every HUD Update and every LateUpdate: they read this frame's outputs.</summary>
        public const int ExecutionOrder = -100;

        private const string Source = "MarkFeedback";

        private static MarkFeedback _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Called by every <see cref="RivalIndicator"/> as it wakes. Play mode only.</summary>
        public static void EnsureForMatch()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("MarkFeedback");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MarkFeedback>();
        }

        /// <summary>This frame's presentation; all clear with no instance.</summary>
        public static MarkOutputs Current => _instance != null ? _instance._model.Current : CleanOutputs;

        private static readonly MarkOutputs CleanOutputs = new MarkOutputs
            { MarkerPop = 1f, DrawScale = 1f, ToastScale = 1f, LiveScale = 1f, SkullCorner = -1, TargetCorner = -1, HunterCorner = -1 };

        /// <summary>The marked chicken while the local Assassin's mark is shown (kept through the end fade while it exists).</summary>
        public static ChickenController Target => _instance != null ? _instance._target : null;

        /// <summary>The hunter while the local player is marked.</summary>
        public static ChickenController Hunter => _instance != null ? _instance._hunter : null;

        /// <summary>Is <paramref name="chicken"/> the local Assassin's mark, with the hunt marker showing? Its floor arrow then
        /// carries the marker (K5r) while it is outside the circle round the hunter's bird, and is emphasised.</summary>
        public static bool IsMarkedByLocal(ChickenController chicken) =>
            _instance != null && chicken != null && chicken == _instance._target &&
            _instance._model.Current.Role == MarkRole.Assassin && _instance._model.Current.MarkerAlpha > 0f;

        /// <summary>The marked target's arrow colour (K5r): the marker's own state colour, seat colour while arming, red or grey or
        /// cream by mode, yellow when the pounce is legal (<see cref="HuntRules.MarkerColour"/>).</summary>
        public static Color MarkArrowTint()
        {
            if (_instance == null) return Color.white;
            var o = _instance._model.Current;
            return HuntRules.MarkerColour(o.MarkerMode, o.TargetCorner);
        }

        /// <summary>Is <paramref name="chicken"/> the hunter whose arrow must show even while it is invisible (direction only)?</summary>
        public static bool ForcesArrowFor(ChickenController chicken) =>
            _instance != null && chicken != null && chicken == _instance._hunter && _instance._model.Current.ForceShowHunter;

        /// <summary>Immediate off for everything (scene unload, disable, destroy). Static so a test can drive it.</summary>
        public static void ClearPresentation()
        {
            MarkScreenEffect.Clear();
            if (_instance != null) _instance.HardClear();
        }

        private ILogService _log;
        private bool _missingIndicatorReported, _missingExecuteReported;
        private GameManager _seenMatch;

        private void ReportOnce(ref bool reported, string message)
        {
            if (reported) return;
            reported = true;
            _log?.Error(Source, message);
        }
        private IAudioService _audio;
        private AudioRegistrySO _sfx;
        private bool _missingAudioReported;

        private readonly MarkPresentationModel _model = new MarkPresentationModel();
        private readonly List<MarkChicken> _snapshot = new List<MarkChicken>(8);
        private readonly List<ChickenController> _snapshotRefs = new List<ChickenController>(8);
        private ChickenController _target, _hunter, _localHunter;
        private HuntBrackets _brackets;
        private long _shownHunterId;
        private float _introLastSeenAt = float.NegativeInfinity;

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO sfx)
        {
            _log = log;
            _audio = audio;
            _sfx = sfx;
        }

        private void Awake()
        {
            SelfInject.Ensure(this, _log != null);
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _brackets = HuntBrackets.Create(transform, _log);   // K4: the hunter's ground brackets, a child of this scene-level object
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
            _model.Reset();
            _target = _hunter = _localHunter = null;
            _shownHunterId = 0;
            MarkScreenEffect.Clear();
            if (_brackets != null) _brackets.Hide();
        }

        private void Update()
        {
            var o = _model.Step(Gather(), Time.unscaledDeltaTime);
            if (o.Role == MarkRole.None) _target = _hunter = _localHunter = null;
            MarkScreenEffect.Set(o.Dim, o.Cold, o.Red);
            DriveBrackets(in o);
            if (o.Events != MarkEvents.None) PlayStings(o);
            if ((o.Events & MarkEvents.Ended) != 0 && _localHunter != null) SpawnEndWord(o.EndCause);
            if (ShowsHuntPop(o) && _localHunter != null)
                FloatingCombatText.Spawn(_localHunter, _localHunter.PopAnchor(),
                    UiText.Get(UiKeys.HudPopHunt), FeedbackPalette.Current.Accent(AccentKey.Hunt));
        }

        /// <summary>The hunter's ground brackets round the marked target (K4): only while this peer's local Assassin owns the mark
        /// (<see cref="HuntBrackets.ShouldShow"/>), the target following the same chicken the marker rides, hidden (Hide) or not.</summary>
        private void DriveBrackets(in MarkOutputs o)
        {
            if (_brackets == null) return;
            var target = _target;   // Unity null once the chicken despawned during the end fade
            bool has = target != null;
            Vector3 at = has ? target.transform.position : default;
            _brackets.Apply(in o, has, at, has ? RingWorldRadiusOf(target) : 0f, PlayerPreferences.ReducedMotionEnabled, Time.unscaledTime);
        }

        /// <summary>The target's rival ring radius in world metres (its indicator's), the authored radius at its scale if it has none.</summary>
        private static float RingWorldRadiusOf(ChickenController target)
        {
            var indicator = target.Indicator();
            return indicator != null ? indicator.RingWorldRadius : FeedbackTuning.RivalRingRadius * target.transform.lossyScale.x;
        }

        /// <summary>The word the hunter reads when a mark ends (K6): MISSED, or SAFE when the target was behind a wall or in an egg;
        /// null for a pounce, a stopped round or a gone hunter (no word).</summary>
        public static string EndWordKey(MarkEndCause cause) =>
            cause == MarkEndCause.Missed ? UiKeys.HudPopMissed
            : cause == MarkEndCause.Safe ? UiKeys.HudPopSafe
            : null;

        /// <summary>MISSED in the Hunt accent (as HUNT!), SAFE in the neutral no-effect grey <see cref="HitFeedback"/> gives it.</summary>
        public static Color EndWordColour(MarkEndCause cause) =>
            cause == MarkEndCause.Safe ? FeedbackTuning.NeutralNoEffectColor : FeedbackPalette.Current.Accent(AccentKey.Hunt);

        /// <summary>The end word over the local hunter's own bird, on its owner's screen only (<see cref="FloatingCombatText.SpawnSelfPop"/>).</summary>
        private void SpawnEndWord(MarkEndCause cause)
        {
            string key = EndWordKey(cause);
            if (key == null) return;
            if (!FloatingCombatText.SpawnSelfPop(_localHunter, key, EndWordColour(cause)))
                _log?.Warn(Source, $"End word {key} not drawn: the local hunter is not a self-pop target");
        }

        /// <summary>HUNT! over the hunter the frame its kill goes live, on the hunter's own screen only: the Assassin role is
        /// resolved from the local chicken (<c>HasInputAuthority</c>), so a victim or an observer never draws it.</summary>
        public static bool ShowsHuntPop(in MarkOutputs o) =>
            (o.Events & MarkEvents.KillLive) != 0 && o.Role == MarkRole.Assassin;

        // ---- Gathering (replicated state -> MarkInputs) ------------------------------------------------------------------

        private MarkInputs Gather()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm != _seenMatch)
            {
                // A new match: its mark keys can repeat the last match's.
                _seenMatch = gm;
                _model.ForgetAnnounced();
            }
            bool gmValid = gm != null && gm.Object != null && gm.Object.IsValid;
            if (gmValid && gm.IsIntroActive) _introLastSeenAt = Time.unscaledTime;

            var input = new MarkInputs
            {
                MatchRunning = gmValid && gm.IsMatchRunning,
                ReducedMotion = PlayerPreferences.ReducedMotionEnabled,
                BannerMayShow = gmValid && EventBannerTiming.CanShow(gm.IsIntroActive, Time.unscaledTime - _introLastSeenAt),
            };

            var r = MarkRoleRules.Resolve(Snapshot(), _shownHunterId);
            // The local chicken's own removal, read before the early return: it tells an end the hunter caused from a lost target.
            input.LocalRemoved = r.Local < 0 || _snapshot[r.Local].Removed;
            if (r.Role == MarkRole.None || !input.MatchRunning)
            {
                _shownHunterId = 0;
                // Keep the refs for the end fade only while the chickens still exist (Unity null for a despawned one).
                return input;
            }

            var hunter = _snapshotRefs[r.Hunter];
            var target = _snapshotRefs[r.Target];
            var local = _snapshotRefs[r.Local];
            var exec = hunter.Execute;
            var runner = hunter.Runner;

            input.Role = r.Role;
            input.MarkKey = r.Key;
            input.TargetCorner = target.HomeCornerIndex;
            input.HunterCorner = hunter.HomeCornerIndex;
            input.Remaining = exec.WindowTimer.RemainingTime(runner) ?? 0f;
            input.Window = exec.WindowSeconds;
            input.ArmSeconds = exec.ArmSeconds;
            input.ArmRemaining = exec.ArmRemaining;
            _shownHunterId = _snapshot[r.Hunter].Id;
            _target = r.Role == MarkRole.Assassin ? target : null;
            _hunter = r.Role == MarkRole.Victim ? hunter : null;
            _localHunter = r.Role == MarkRole.Assassin ? local : null;

            if (r.Role == MarkRole.Assassin) GatherAssassin(ref input, local, target, exec);
            else if (r.Role == MarkRole.Victim) GatherVictim(ref input, local, hunter);
            return input;
        }

        private static void GatherAssassin(ref MarkInputs input, ChickenController local, ChickenController target, AssassinExecute exec)
        {
            Vector3 me = local.transform.position, it = target.transform.position;
            input.Distance = FloorArrowRules.PlanarDistance(me, it);
            input.CanKillNow = exec.CanKillNow;
            input.Walled = PlayerBase.WallBetween(me, it);
            input.TargetShelled = !ExecuteRules.CanBeExecuted(target.IsStealProof);
            input.TargetStunned = target.CurrentControlState == ControlState.Stunned;
            input.KillRange = exec.KillRange;

            // Stun lit: the target inside the equipped stun's OWN shape (the hot test's WouldAffect: reach, shape, wall,
            // eligibility, all read from the asset) and the slot ready. Any stun counts (FixedKit.EmitsStun), the rule the kit
            // validation and the bot's hunt already use; the Ambush-only test went with Ambush (2026-11-05).
            var abilities = local.Abilities;
            if (abilities == null) return;
            for (int s = 0; s < AbilityController.SlotCount; s++)
            {
                var stun = abilities.GetSlot(s);
                if (!FixedKit.EmitsStun(stun)) continue;
                // "Ready" = a press would fire now (off cooldown, the Assassin free to cast): the refusal table the button uses.
                input.AmbushReadyOnTarget = abilities.EvaluateRefusal(s) == AbilityRefusal.None &&
                                            stun.WouldAffect(local, target, local.transform.forward);
                break;
            }
        }

        private void GatherVictim(ref MarkInputs input, ChickenController local, ChickenController hunter)
        {
            input.HunterDistance = FloorArrowRules.PlanarDistance(local.transform.position, hunter.transform.position);
            var cam = MatchCamera.Instance != null ? MatchCamera.Instance.Camera : null;
            input.InnerRadius = FloorRivalArrows.RingRadiusFor(cam);
            input.HunterInvisible = Mathf.Clamp01(hunter.VisualOpacity) < RivalIndicator.VisibleOpacityThreshold;
            var indicator = hunter.Indicator();
            if (indicator == null) ReportOnce(ref _missingIndicatorReported, $"{hunter.name} has no RivalIndicator: the victim's " +
                "hunter badge treats it as never off screen (prefab wiring).");
            input.HunterHiddenFromView = indicator != null && indicator.HiddenFromClearViewport;
        }

        /// <summary>Living, valid, non-decoy chickens, as plain values.</summary>
        private IReadOnlyList<MarkChicken> Snapshot()
        {
            _snapshot.Clear();
            _snapshotRefs.Clear();
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                if (c.Execute == null)
                {
                    ReportOnce(ref _missingExecuteReported, $"{c.name} has no AssassinExecute: its marks (and marks on it) are " +
                        "not shown (prefab wiring).");
                    continue;
                }
                var exec = c.Execute;
                bool hasMark = exec.Object != null && exec.Object.IsValid && exec.MarkedTarget != NetworkBehaviourId.None;
                Vector3 p = c.transform.position;
                _snapshot.Add(new MarkChicken
                {
                    Id = IdOf(c.Id),
                    Corner = c.HomeCornerIndex,
                    IsLocal = c.HasInputAuthority,
                    Removed = c.Combat != null && c.Combat.IsRemoved,
                    MarkedTargetId = hasMark ? IdOf(exec.MarkedTarget) : 0,
                    WindowEndTick = hasMark ? exec.WindowTimer.TargetTick ?? 0 : 0,
                    Position = new Vector2(p.x, p.z),
                });
                _snapshotRefs.Add(c);
            }
            return _snapshot;
        }

        private static long IdOf(NetworkBehaviourId id) => ((long)id.Object.Raw << 16) | (uint)(id.Behaviour & 0xFFFF);

        // ---- Stings -----------------------------------------------------------------------------------------------------

        private void PlayStings(in MarkOutputs o)
        {
            if (_audio == null || _sfx == null)
            {
                if (!_missingAudioReported)
                {
                    _missingAudioReported = true;
                    _log?.Error(Source, "No IAudioService / AudioRegistrySO injected: the Death Mark stings are silent.");
                }
                return;
            }
            if ((o.Events & MarkEvents.DrawTick) != 0)
                _audio.PlaySFX(_sfx.MarkDrawTick, FeedbackTuning.MarkDrawTickVolume, DrawTickPitch(o.DrawStep));
            if ((o.Events & MarkEvents.Lock) != 0)
                _audio.PlaySFX(_sfx.MarkLock, FeedbackTuning.MarkLockVolume, FeedbackTuning.MarkLockPitch);
            if ((o.Events & MarkEvents.Marked) != 0)
                _audio.PlaySFX(_sfx.MarkVictim, FeedbackTuning.MarkVictimVolume, FeedbackTuning.MarkVictimPitch);
            if ((o.Events & MarkEvents.KillReady) != 0)
                _audio.PlaySFX(_sfx.MarkKillReady, FeedbackTuning.MarkKillReadyVolume, FeedbackTuning.MarkKillReadyPitch);
            if ((o.Events & MarkEvents.KillLive) != 0)
            {
                if (o.Role == MarkRole.Victim)
                    _audio.PlaySFX(_sfx.MarkVictim, FeedbackTuning.MarkKillLiveVictimVolume, FeedbackTuning.MarkKillLiveVictimPitch);
                else if (o.Role == MarkRole.Assassin)
                {
                    // E1: the HUNT! pop's own sting (the hunter's peer only, same gate as ShowsHuntPop). Until the clip is
                    // filled, the pitched-up lock sting keeps the moment audible.
                    if (_audio == null || !_audio.TryPlaySFX(SfxId.HuntLive, FeedbackTuning.MarkKillLiveAssassinVolume))
                        _audio.PlaySFX(_sfx.MarkLock, FeedbackTuning.MarkKillLiveAssassinVolume, FeedbackTuning.MarkKillLiveAssassinPitch);
                }
                else
                    _audio.PlaySFX(_sfx.MarkLock, FeedbackTuning.MarkKillLiveObserverVolume, FeedbackTuning.MarkKillLiveObserverPitch);
            }
        }

        /// <summary>The draw tick rises a step per row, capped.</summary>
        public static float DrawTickPitch(int step) =>
            Mathf.Min(FeedbackTuning.MarkDrawTickPitchMax, FeedbackTuning.MarkDrawTickPitch + FeedbackTuning.MarkDrawTickPitchStep * Mathf.Max(0, step));
    }
}
