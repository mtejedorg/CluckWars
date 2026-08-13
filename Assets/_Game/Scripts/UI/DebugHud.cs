using System.Text;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Networking;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// Developer overlay — toggle with F1. Reports FPS, networking state,
    /// GameManager phase, local chicken stats, base ownership, loose pickup
    /// count, and ability cooldowns at a glance. Read-only — no debug
    /// commands, no networked side effects.
    /// </summary>
    /// <remarks>
    /// IMGUI on purpose: zero scene wiring, draws over whatever else is on
    /// screen, easy to disable in release. Drop the component into Game.unity
    /// and forget about it.
    /// </remarks>
    public sealed class DebugHud : MonoBehaviour
    {
        [Tooltip("Visible at start? Toggle with F1 either way.")]
        [SerializeField] private bool _visible = false;

        [Tooltip("Seconds between FPS samples.")]
        [Min(0.05f)]
        [SerializeField] private float _fpsSampleInterval = 0.25f;

        private INetworkService _network;

        // Injected optionally — may be null when registry asset is not bound.
        [InjectOptional] private ChickenClassRegistrySO _classRegistry;

        private float _accumulatedFrames;
        private float _accumulatedTime;
        private float _fps;
        private float _nextRefreshUnscaledTime;
        private bool  _balanceVisible;

        private readonly StringBuilder _sb        = new StringBuilder(1024);
        private readonly StringBuilder _balanceSb = new StringBuilder(1024);

        [Inject]
        public void Construct(INetworkService network) => _network = network;

        private void Awake()
        {
            if (_network == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void Update()
        {
            // FPS sampling regardless of visibility — cheap and lets the readout
            // settle by the time you toggle on.
            _accumulatedFrames++;
            _accumulatedTime += Time.unscaledDeltaTime;
            if (_accumulatedTime >= _fpsSampleInterval)
            {
                _fps = _accumulatedFrames / _accumulatedTime;
                _accumulatedFrames = 0f;
                _accumulatedTime = 0f;
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) _visible        = !_visible;
                if (kb.f2Key.wasPressedThisFrame) _balanceVisible = !_balanceVisible;
            }
        }

        /// <summary>
        /// IMGUI draws in raw screen pixels with a ~12px default font and has no
        /// equivalent of the PanelSettings/CanvasScaler scaling the rest of the UI
        /// uses. On a 422 ppi phone that renders this whole overlay at about 4.5 dp
        /// — illegible, which matters because docs/TESTING.md makes F1 the on-device
        /// diagnostic path. Scale the GUI matrix by pixel density instead, so the
        /// rects below stay in the desktop coordinate space they were authored in.
        /// Capped at 3x: uncapped, a Pixel 9 would take 4.4x and the panel would
        /// swallow two thirds of the screen.
        /// </summary>
        private static float HudScale =>
            Screen.dpi > 1f ? Mathf.Clamp(Screen.dpi / 96f, 1f, 3f) : 1f;

        private void OnGUI()
        {
            const int pad = 12;

            float scale = HudScale;
            var prevMatrix = GUI.matrix;
            if (!Mathf.Approximately(scale, 1f))
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // Logical (pre-scale) screen size — right-anchored rects must be placed
            // in the scaled space, not in raw pixels.
            float logicalWidth = Screen.width / scale;

            if (_visible)
            {
                const int width  = 360;
                const int height = 22 * 18;
                int x = pad;
                int y = 110;

                _sb.Clear();
                BuildReport(_sb);

                GUI.Box(new Rect(x, y, width, height), "Debug (F1)");
                GUI.Label(new Rect(x + 10, y + 24, width - 20, height - 32), _sb.ToString());
            }

            if (_balanceVisible)
            {
                const int width  = 480;
                const int height = 22 * 24;
                float x = logicalWidth - width - pad;
                int y = 110;

                _balanceSb.Clear();
                BuildBalanceReport(_balanceSb);

                GUI.Box(new Rect(x, y, width, height), "Balance (F2)");
                GUI.Label(new Rect(x + 10, y + 24, width - 20, height - 32), _balanceSb.ToString());
            }

            GUI.matrix = prevMatrix;
        }

        private void BuildReport(StringBuilder sb)
        {
            sb.Append("FPS ").AppendLine(_fps.ToString("0"));

            // --- Network ---
            sb.AppendLine();
            sb.AppendLine("[Network]");
            var runner = _network?.Runner;
            if (runner == null)
            {
                sb.AppendLine("Runner: (none)");
            }
            else
            {
                sb.Append("Mode: ").AppendLine(runner.GameMode.ToString());
                sb.Append("LocalPlayer: ").AppendLine(runner.LocalPlayer.ToString());
                sb.Append("IsMaster: ").AppendLine(runner.IsSharedModeMasterClient.ToString());
                int activeCount = 0;
                foreach (var _ in runner.ActivePlayers) activeCount++;
                sb.Append("ActivePlayers: ").AppendLine(activeCount.ToString());
            }

            // --- GameManager ---
            sb.AppendLine();
            sb.AppendLine("[Match]");
            var gm = FindFirstObjectByType<GameManager>();
            if (gm == null || gm.Object == null || !gm.Object.IsValid)
            {
                sb.AppendLine("GameManager: (none)");
            }
            else
            {
                sb.Append("State: ").AppendLine(gm.State.ToString());
                if (gm.IsIntroActive) sb.Append("Intro: ").Append(gm.IntroRemaining.ToString("0.0")).AppendLine("s");
                sb.Append("Time: ").Append(gm.TimeRemaining.ToString("0.0")).AppendLine("s");
                if (gm.State == MatchState.Ended)
                {
                    sb.Append("Winner: ").AppendLine(gm.WinnerPlayer.ToString());
                    sb.Append("Final: ").Append(gm.WinnerFoodTotal.ToString("0")).AppendLine();
                    sb.Append("Restart in: ").Append(gm.RestartRemaining.ToString("0.0")).AppendLine("s");
                }
            }

            // --- Local chicken ---
            sb.AppendLine();
            sb.AppendLine("[Local Chicken]");
            ChickenController localCtrl = null;
            var ctrls = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < ctrls.Length; i++)
            {
                var c = ctrls[i];
                if (c.Object != null && c.Object.IsValid && c.HasInputAuthority)
                {
                    localCtrl = c;
                    break;
                }
            }
            if (localCtrl == null)
            {
                sb.AppendLine("(not spawned)");
            }
            else
            {
                sb.Append("Class: ").AppendLine(localCtrl.Class.ToString());
                sb.Append("State: ").Append(localCtrl.CurrentControlState.ToString())
                  .Append(localCtrl.Combat != null && localCtrl.Combat.IsRemoved ? "  REMOVED" : "").AppendLine();
                if (localCtrl.Cargo != null)
                {
                    sb.Append("Cargo: ").Append(Mathf.FloorToInt(localCtrl.Cargo.Cargo))
                      .Append(" / ").AppendLine(Mathf.FloorToInt(localCtrl.Cargo.Capacity).ToString());
                }
                if (localCtrl.Abilities != null)
                {
                    var a = localCtrl.Abilities;
                    sb.Append("Slot0: ").Append(a.Slot0 != null ? a.Slot0.name : "—")
                      .Append(" CD ").Append(a.CooldownRemaining(0).ToString("0.0")).AppendLine();
                    sb.Append("Slot1: ").Append(a.Slot1 != null ? a.Slot1.name : "—")
                      .Append(" CD ").Append(a.CooldownRemaining(1).ToString("0.0")).AppendLine();
                    if (a.ActiveSlot != AbilityController.InvalidSlot)
                        sb.Append("Active: slot ").AppendLine(a.ActiveSlot.ToString());
                }
            }

            // --- World ---
            sb.AppendLine();
            sb.AppendLine("[World]");
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null) continue;
                sb.Append("Base C").Append(b.CornerIndex)
                  .Append(": owner=").Append(b.Owner.IsRealPlayer ? "P" + (b.Owner.PlayerId + 1) : "—")
                  .Append("  food=").AppendLine(Mathf.FloorToInt(b.FoodTotal).ToString());
            }
            var pickups = FindObjectsByType<FoodPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            sb.Append("Pickups: ").AppendLine(pickups.Length.ToString());
        }

        // ---- Balance report (F2) -------------------------------------------------

        private void BuildBalanceReport(StringBuilder sb)
        {
            // ── Classes ─────────────────────────────────────────────────────────
            sb.AppendLine("[Classes]");
            sb.AppendLine("Name          Spd  Turn  Passive       Cap  Rate");

            if (_classRegistry != null)
            {
                foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                {
                    if (!_classRegistry.TryGet(cls, out var entry)) continue;
                    var s = entry.Stats;
                    if (s == null) continue;
                    sb.Append(s.DisplayName.PadRight(13))
                      .Append(s.MoveSpeed.ToString("0.0").PadLeft(6))
                      .Append(s.TurnSpeed.ToString("0").PadLeft(6))
                      .Append("  -".PadRight(14)) // specialization is on the equipped PassiveAbilitySO now, not on stats
                      .Append(s.CargoCapacity.ToString().PadLeft(5))
                      .Append(s.CollectionRate.ToString("0.0").PadLeft(6))
                      .AppendLine();
                }
            }
            else
            {
                sb.AppendLine("  (ClassRegistry not bound)");
            }

            // ── Local chicken ability timings ────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("[Local Abilities]");

            ChickenController localCtrl = null;
            var ctrls = FindObjectsByType<ChickenController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < ctrls.Length; i++)
            {
                var c = ctrls[i];
                if (c.Object != null && c.Object.IsValid && c.HasInputAuthority)
                {
                    localCtrl = c;
                    break;
                }
            }

            if (localCtrl == null || localCtrl.Abilities == null)
            {
                sb.AppendLine("  (no local chicken spawned)");
                return;
            }

            var a = localCtrl.Abilities;
            sb.AppendLine("Slot  Ability              Duration  Cooldown");
            AppendAbilityLine(sb, 0, a.Slot0);
            AppendAbilityLine(sb, 1, a.Slot1);

            if (a.ActiveSlot != AbilityController.InvalidSlot)
                sb.Append("  Active slot ").AppendLine(a.ActiveSlot.ToString());
        }

        private static void AppendAbilityLine(StringBuilder sb, int slot, AbilityBaseSO ability)
        {
            if (ability == null)
            {
                sb.Append("  ").Append(slot).AppendLine("     —");
                return;
            }
            sb.Append("  ").Append(slot).Append("    ")
              .Append(ability.DisplayName.PadRight(20))
              .Append(ability.Duration.ToString("0.00").PadLeft(6))
              .Append("s")
              .Append(ability.Cooldown.ToString("0.0").PadLeft(9))
              .AppendLine("s");
        }
    }
}
