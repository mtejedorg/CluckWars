using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.UI;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>What the mark banner says, pure.</summary>
    public enum MarkPhase : byte
    {
        None = 0,
        /// <summary>Assassin: the target is out of kill range and not stunned.</summary>
        Hunt = 1,
        /// <summary>Assassin: in reach, stun it.</summary>
        Stun = 2,
        /// <summary>Assassin: it is stunned, get into kill range.</summary>
        CloseIn = 3,
        /// <summary>Assassin: it stands behind a base wall; nothing reaches it there.</summary>
        Walled = 4,
        /// <summary>Assassin: a press kills now.</summary>
        Kill = 5,
        /// <summary>This player is somebody's mark.</summary>
        Victim = 6,
    }

    /// <summary>The Assassin's banner, pure, in the order the Kill press judges it.</summary>
    public static class MarkFeedbackRules
    {
        public static MarkPhase AssassinPhase(bool canKill, bool stunned, bool inRange, bool walled)
        {
            if (canKill) return MarkPhase.Kill;
            if (walled) return MarkPhase.Walled;
            if (stunned) return MarkPhase.CloseIn;
            return inRange ? MarkPhase.Stun : MarkPhase.Hunt;
        }

        public static string Text(MarkPhase phase, string name, int seconds, int meters)
        {
            switch (phase)
            {
                case MarkPhase.Hunt:    return UiText.Format(UiKeys.MarkHunt, ("name", name), ("m", meters), ("secs", seconds));
                case MarkPhase.Stun:    return UiText.Format(UiKeys.MarkStun, ("name", name), ("secs", seconds));
                case MarkPhase.CloseIn: return UiText.Format(UiKeys.MarkCloseIn, ("m", meters), ("secs", seconds));
                case MarkPhase.Walled:  return UiText.Format(UiKeys.MarkWalled, ("name", name), ("secs", seconds));
                case MarkPhase.Kill:    return UiText.Get(UiKeys.MarkKill);
                case MarkPhase.Victim:  return UiText.Format(UiKeys.MarkVictim, ("secs", seconds));
                default:                return string.Empty;
            }
        }

        /// <summary>Whole seconds shown: rounds up, so "0" never shows while the mark is alive.</summary>
        public static int ShownSeconds(float remaining) => Mathf.CeilToInt(Mathf.Max(0f, remaining));

        /// <summary>The kill-range ring only appears once it can inform a decision: within this factor of the range.</summary>
        public static bool ShowsKillRing(float distance, float killRange) =>
            distance <= killRange * FeedbackTuning.MarkKillRingShowFactor;
    }

    /// <summary>
    /// Everything the Assassin and the marked bird see of a Death Mark. Local presentation read from replicated state
    /// (<c>AssassinExecute.MarkedTarget</c>, its timers, positions) on every frame: no RPC, no networked field.
    /// </summary>
    /// <remarks>
    /// One scene-root object, like <see cref="FloorRivalArrows"/>: never under a chicken, so <c>ChickenVisuals</c>' renderer scan and
    /// ghost swap cannot touch it, and the mark stays visible on an invisible target. On the target: a stack of two big chevrons
    /// over its head (shape, not only hue), a ring at its feet, and the kill-range ring round it when that can inform a decision.
    /// <see cref="RivalIndicator"/> asks <see cref="IsMarkedByLocal"/> and gives this rival its distinct edge arrow.
    /// <see cref="UI.MatchHud"/> reads <see cref="Phase"/> for the banner. Only the two people involved see any of it.
    /// </remarks>
    [DefaultExecutionOrder(FloorRivalArrows.ExecutionOrder + 1)]
    public sealed class MarkFeedback : MonoBehaviour
    {
        private const string Source = "MarkFeedback";
        private const int RingSegments = 64;
        private const int ChevronCount = 2;

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

        /// <summary>Is <paramref name="chicken"/> the local player's mark?</summary>
        public static bool IsMarkedByLocal(ChickenController chicken) =>
            _instance != null && chicken != null && _instance._target == chicken;

        /// <summary>This frame's banner state.</summary>
        public static MarkPhase Phase => _instance != null ? _instance._phase : MarkPhase.None;

        /// <summary>This frame's banner text; empty with no mark involved.</summary>
        public static string BannerText => _instance != null ? _instance._text : string.Empty;

        private ILogService _log;
        private ChickenController _target;
        private MarkPhase _phase;
        private string _text = string.Empty;
        private float _killRange;

        private LineRenderer _footRing, _rangeRing;
        private readonly SpriteRenderer[] _fills = new SpriteRenderer[ChevronCount];
        private readonly SpriteRenderer[] _inks = new SpriteRenderer[ChevronCount];
        private Transform _stack;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            var line = TelegraphShapes.SharedSolidLineMaterial(_log);
            if (line == null)
                _log?.Error(Source, "No line material: the marked chicken's rings are not drawn; the Assassin still has the edge arrow and the banner.");
            _footRing = TelegraphShapes.BuildLine(transform, "MarkFootRing", line, FeedbackTuning.MarkFootRingWidth, true);
            _rangeRing = TelegraphShapes.BuildLine(transform, "MarkKillRing", line, FeedbackTuning.MarkKillRingWidth, true);
            _footRing.positionCount = _rangeRing.positionCount = RingSegments;

            _stack = new GameObject("MarkChevrons").transform;
            _stack.SetParent(transform, false);
            for (int i = 0; i < ChevronCount; i++)
            {
                var ink = new GameObject("MarkChevronInk").AddComponent<SpriteRenderer>();
                ink.transform.SetParent(_stack, false);
                ink.sprite = RivalIndicator.ChevronInkSpriteShared;
                ink.color = FeedbackTuning.MarkInk;
                ink.sortingOrder = FeedbackTuning.MarkSortingOrder;
                _inks[i] = ink;

                var fill = new GameObject("MarkChevronFill").AddComponent<SpriteRenderer>();
                fill.transform.SetParent(ink.transform, false);
                fill.sprite = RivalIndicator.ChevronSpriteShared;
                fill.sortingOrder = FeedbackTuning.MarkSortingOrder + 1;
                _fills[i] = fill;

                ink.transform.localPosition = new Vector3(0f, -i * FeedbackTuning.MarkChevronSpacing, 0f);
            }
            ShowTargetVisuals(false);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Before every <c>LateUpdate</c>: <see cref="RivalIndicator"/> reads the answer there.</summary>
        private void Update()
        {
            _target = null;
            _phase = MarkPhase.None;
            _text = string.Empty;

            var local = FindLocal();
            if (local == null || local.Runner == null) return;

            var exec = local.Execute;
            if (exec != null && exec.MarkedTarget != NetworkBehaviourId.None &&
                local.Runner.TryFindBehaviour(exec.MarkedTarget, out ChickenController target) && target != null)
            {
                _target = target;
                _killRange = exec.KillRange;
                Vector3 me = local.transform.position, it = target.transform.position;
                float d = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(it.x, it.z));
                _phase = MarkFeedbackRules.AssassinPhase(exec.CanKillNow, target.CurrentControlState == ControlState.Stunned,
                    d <= exec.KillRange, PlayerBase.WallBetween(me, it));
                _text = MarkFeedbackRules.Text(_phase, MatchStandings.DisplayName(false, target.IsBot, target.Class, target.HomeCornerIndex),
                    MarkFeedbackRules.ShownSeconds(exec.WindowTimer.RemainingTime(local.Runner) ?? 0f), Mathf.RoundToInt(d));
                return;
            }

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == local || c.Object == null || !c.Object.IsValid || c.Execute == null) continue;
                if (c.Execute.MarkedTarget != local.Id) continue;
                _phase = MarkPhase.Victim;
                _text = MarkFeedbackRules.Text(_phase, null, MarkFeedbackRules.ShownSeconds(c.Execute.WindowTimer.RemainingTime(local.Runner) ?? 0f), 0);
                return;
            }
        }

        private static ChickenController FindLocal()
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.Object != null && c.Object.IsValid && c.HasInputAuthority && !c.IsDecoy) return c;
            }
            return null;
        }

        private void LateUpdate()
        {
            var cam = MatchCamera.Instance != null ? MatchCamera.Instance.Camera : Camera.main;
            if (_target == null || cam == null)
            {
                ShowTargetVisuals(false);
                return;
            }

            ShowTargetVisuals(true);
            bool kill = _phase == MarkPhase.Kill;
            Color color = kill ? FeedbackTuning.MarkKillColor : FeedbackTuning.MarkColor;
            float pulse = 1f + (kill ? FeedbackTuning.MarkKillPulse : FeedbackTuning.MarkPulse)
                * Mathf.Sin(Time.unscaledTime * FeedbackTuning.MarkPulseSpeed);

            Vector3 at = _target.transform.position;
            SetRing(_footRing, at, FeedbackTuning.MarkFootRingRadius * pulse, color);

            Vector3 me = cam.transform.position;
            bool showRange = MarkFeedbackRules.ShowsKillRing(Planar(at, FindLocal()), _killRange);
            _rangeRing.gameObject.SetActive(showRange);
            if (showRange) SetRing(_rangeRing, at, _killRange, new Color(color.r, color.g, color.b, FeedbackTuning.MarkKillRingAlpha));

            _stack.position = at + Vector3.up * (FeedbackTuning.MarkChevronHeight +
                FeedbackTuning.MarkChevronBob * Mathf.Sin(Time.unscaledTime * FeedbackTuning.MarkPulseSpeed));
            _stack.rotation = Quaternion.LookRotation(cam.transform.forward, -cam.transform.up);   // the art points up: this points it down
            _stack.localScale = Vector3.one * FeedbackTuning.MarkChevronSize * pulse;
            for (int i = 0; i < ChevronCount; i++) _fills[i].color = color;
        }

        private static float Planar(Vector3 a, ChickenController b)
        {
            if (b == null) return float.PositiveInfinity;
            Vector3 p = b.transform.position;
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(p.x, p.z));
        }

        private void ShowTargetVisuals(bool show)
        {
            if (_footRing.gameObject.activeSelf != show) _footRing.gameObject.SetActive(show);
            if (!show && _rangeRing.gameObject.activeSelf) _rangeRing.gameObject.SetActive(false);
            if (_stack.gameObject.activeSelf != show) _stack.gameObject.SetActive(show);
        }

        private static void SetRing(LineRenderer ring, Vector3 center, float radius, Color color)
        {
            ring.startColor = ring.endColor = color;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, TelegraphShapes.GroundY, center.z + Mathf.Sin(a) * radius));
            }
        }
    }
}
