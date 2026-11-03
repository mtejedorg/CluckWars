using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Gameplay;
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
            { MarkerPop = 1f, DrawScale = 1f, ToastScale = 1f, SkullCorner = -1, TargetCorner = -1, HunterCorner = -1 };

        /// <summary>The marked chicken while the local Assassin's mark is shown (kept through the end fade while it exists).</summary>
        public static ChickenController Target => _instance != null ? _instance._target : null;

        /// <summary>The hunter while the local player is marked.</summary>
        public static ChickenController Hunter => _instance != null ? _instance._hunter : null;

        /// <summary>Is <paramref name="chicken"/> the local Assassin's mark, with the hunt marker showing? Its own floor arrow
        /// and edge chevron stand down then: the marker is its arrow.</summary>
        public static bool IsMarkedByLocal(ChickenController chicken) =>
            _instance != null && chicken != null && chicken == _instance._target &&
            _instance._model.Current.Role == MarkRole.Assassin && _instance._model.Current.MarkerAlpha > 0f;

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
        private IAudioService _audio;
        private AudioRegistrySO _sfx;
        private bool _missingAudioReported;

        private readonly MarkPresentationModel _model = new MarkPresentationModel();
        private readonly List<MarkChicken> _snapshot = new List<MarkChicken>(8);
        private readonly List<ChickenController> _snapshotRefs = new List<ChickenController>(8);
        private ChickenController _target, _hunter;
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
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
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
            _model.Reset();
            _target = _hunter = null;
            _shownHunterId = 0;
            MarkScreenEffect.Clear();
        }

        private void Update()
        {
            var o = _model.Step(Gather(), Time.unscaledDeltaTime);
            if (o.Role == MarkRole.None) _target = _hunter = null;
            MarkScreenEffect.Set(o.Dim, o.Cold, o.Red);
            if (o.Events != MarkEvents.None) PlayStings(o);
        }

        // ---- Gathering (replicated state -> MarkInputs) ------------------------------------------------------------------

        private MarkInputs Gather()
        {
            var gm = GameManager.Instance;
            bool gmValid = gm != null && gm.Object != null && gm.Object.IsValid;
            if (gmValid && gm.IsIntroActive) _introLastSeenAt = Time.unscaledTime;

            var input = new MarkInputs
            {
                MatchRunning = gmValid && gm.IsMatchRunning,
                ReducedMotion = PlayerPreferences.ReducedMotionEnabled,
                BannerMayShow = gmValid && EventBannerTiming.CanShow(gm.IsIntroActive, Time.unscaledTime - _introLastSeenAt),
            };

            var r = MarkRoleRules.Resolve(Snapshot(), _shownHunterId);
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
            _shownHunterId = _snapshot[r.Hunter].Id;
            _target = r.Role == MarkRole.Assassin ? target : null;
            _hunter = r.Role == MarkRole.Victim ? hunter : null;

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

            // Ambush lit: the target inside the equipped Ambush's OWN shape (the hot test's WouldAffect: reach, shape, wall,
            // eligibility, all read from the asset) and the slot ready.
            var abilities = local.Abilities;
            if (abilities == null) return;
            for (int s = 0; s < AbilityController.SlotCount; s++)
            {
                if (!(abilities.GetSlot(s) is AmbushAbilitySO ambush)) continue;
                // "Ready" = a press would fire now (off cooldown, the Assassin free to cast): the refusal table the button uses.
                input.AmbushReadyOnTarget = abilities.EvaluateRefusal(s) == AbilityRefusal.None &&
                                            ambush.WouldAffect(local, target, local.transform.forward);
                break;
            }
        }

        private static void GatherVictim(ref MarkInputs input, ChickenController local, ChickenController hunter)
        {
            input.HunterDistance = FloorArrowRules.PlanarDistance(local.transform.position, hunter.transform.position);
            var cam = MatchCamera.Instance != null ? MatchCamera.Instance.Camera : null;
            input.InnerRadius = FloorRivalArrows.RingRadiusFor(cam);
            input.HunterInvisible = Mathf.Clamp01(hunter.VisualOpacity) < RivalIndicator.VisibleOpacityThreshold;
            var indicator = hunter.GetComponent<RivalIndicator>();
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
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy || c.Execute == null) continue;
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
        }

        /// <summary>The draw tick rises a step per row, capped.</summary>
        public static float DrawTickPitch(int step) =>
            Mathf.Min(FeedbackTuning.MarkDrawTickPitchMax, FeedbackTuning.MarkDrawTickPitch + FeedbackTuning.MarkDrawTickPitchStep * Mathf.Max(0, step));
    }
}
