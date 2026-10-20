using System.Text;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Networking;
using CluckWars.Settings;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// Developer overlay — toggle with F1, or on a touch device with five taps in the top-left corner
    /// within 2 s (dev-tools build + Developer Mode; <see cref="CornerTapDetector"/>). Reports FPS, networking
    /// state, GameManager phase, local chicken stats, base ownership, ability cooldowns, and the test-session
    /// probes (FPS p95 + hitch count from PerfProbe, Visual offenders + last scan, RTT / loss). Read-only — the
    /// one button only asks the Visual probe for a scan; no networked side effects.
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

        private readonly CornerTapDetector _cornerTaps = new CornerTapDetector();

        private readonly StringBuilder _sb        = new StringBuilder(1024);
        private readonly StringBuilder _balanceSb = new StringBuilder(1024);

        [Inject]
        public void Construct(INetworkService network) => _network = network;

        private void Awake()
        {
            if (_network == null)
            {
                // INetworkService is scene-scoped (GameInstaller): SceneContext first (CONVENTIONS "Self-injection").
                var sceneCtx = FindFirstObjectByType<SceneContext>();
                if (sceneCtx != null) sceneCtx.Container.Inject(this);
                else ProjectContext.Instance.Container.Inject(this);
            }
            // Only GUI.Box / Label / Button are used: skipping IMGUI's layout pass keeps the hidden overlay's per-frame
            // cost down now that a dev-tools build also puts it into every Game scene (perf numbers stay representative).
            useGUILayout = false;
            // Auto-test cw_hud 1: show the overlay as soon as it exists.
            if (DevTools.Available && DiagnosticsReadout.HudRequestedAtBoot) _visible = true;
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

            // A phone has no F1: five taps in the top-left corner, in a dev-tools build with Developer Mode on.
            if (DevTools.Available && PlayerPreferences.DeveloperModeEnabled && TryGetTap(out var tap)
                && _cornerTaps.Register(tap, Screen.width, Screen.height, Time.unscaledTime))
                _visible = !_visible;
        }

        /// <summary>A touch (or mouse click, for testing on PC) that began this frame, in screen pixels.</summary>
        private static bool TryGetTap(out Vector2 position)
        {
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame)
            {
                position = ts.primaryTouch.position.ReadValue();
                return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }
            position = default;
            return false;
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

        /// <summary>Top of the panels in logical (pre-scale) pixels, under the match HUD's top bar.</summary>
        public const float PanelTop = 110f;
        /// <summary>IMGUI's default label line height at the default font, in logical pixels.</summary>
        public const float LineHeight = 16f;
        /// <summary>The panel's title strip + padding above and below the text.</summary>
        public const float PanelChrome = 32f;
        /// <summary>Smallest scale the fit may shrink to (below this the text is unreadable anyway).</summary>
        public const float MinScale = 0.6f;

        /// <summary>
        /// The density scale, capped so a panel of <paramref name="contentHeight"/> logical pixels starting at
        /// <see cref="PanelTop"/> ends above the bottom of a <paramref name="screenHeight"/>-pixel screen. On a ~420 dpi
        /// phone the density alone asks for 3x, which pushed the panel's lower half (and the Scan button) off a 1080 px
        /// tall landscape screen. Pure, tested.
        /// </summary>
        public static float FitScale(float densityScale, float screenHeight, float contentHeight)
        {
            float needed = PanelTop + contentHeight + 12f;
            float fit = needed > 0f && screenHeight > 0f ? screenHeight / needed : densityScale;
            return Mathf.Max(MinScale, Mathf.Min(densityScale, fit));
        }

        private static int LineCount(StringBuilder sb)
        {
            int n = 1;
            for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') n++;
            return n;
        }

        private void OnGUI()
        {
            const int pad = 12;

            // Build first: the panel's height (and so the scale that fits it on screen) depends on how many lines it has.
            float debugHeight = 0f;
            if (_visible)
            {
                _sb.Clear();
                BuildReport(_sb);
                debugHeight = PanelChrome + LineCount(_sb) * LineHeight;
            }
            const float balanceHeight = 22 * 24;
            float scale = FitScale(HudScale, Screen.height, Mathf.Max(debugHeight, _balanceVisible ? balanceHeight : 0f));
            var prevMatrix = GUI.matrix;
            if (!Mathf.Approximately(scale, 1f))
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // Logical (pre-scale) screen size — right-anchored rects must be placed
            // in the scaled space, not in raw pixels.
            float logicalWidth = Screen.width / scale;

            if (_visible)
            {
                const int width  = 360;
                float height = debugHeight;
                int x = pad;
                float y = PanelTop;

                GUI.Box(new Rect(x, y, width, height), "Debug (F1)");
                GUI.Label(new Rect(x + 10, y + 24, width - 20, height - PanelChrome), _sb.ToString());
                // In the title strip (top-right), so it is on screen whenever the panel's top is: asks VisualProbe for a
                // scan, same as F9 or a three-finger tap.
                if (DevTools.Available && GUI.Button(new Rect(x + width - 128, y + 2, 124, 20), "Scan visuals (F9)"))
                    DiagnosticsReadout.VisualScanRequested = true;
            }

            if (_balanceVisible)
            {
                const int width  = 480;
                const float height = balanceHeight;
                float x = logicalWidth - width - pad;
                float y = PanelTop;

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
            AppendDiagnostics(sb);

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
                    sb.AppendLine("Awaiting: Play Again / Back to Lobby");
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
                    AppendActiveSlots(sb, a, "Active: slot ");
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
        }

        /// <summary>Test-session probe readouts (dev tools only; "n/a" until a probe has reported).</summary>
        private static void AppendDiagnostics(StringBuilder sb)
        {
            if (!DevTools.Available) return;
            float p95 = DiagnosticsReadout.PerfP95Ms;
            sb.Append("p95 ").Append(p95 >= 0f ? p95.ToString("0.0") + " ms (" + (1000f / Mathf.Max(p95, 0.01f)).ToString("0") + " fps)" : "n/a")
              .Append("  hitches ").AppendLine(DiagnosticsReadout.PerfHitchCount.ToString());
            int offenders = DiagnosticsReadout.VisualOffenders;
            sb.Append("Visual ").Append(offenders >= 0 ? offenders.ToString() : "n/a")
              .Append(" @ ").AppendLine(DiagnosticsReadout.LastVisualScanUtc);
            float rtt = DiagnosticsReadout.NetRttMs;
            sb.Append("RTT ").Append(rtt >= 0f ? rtt.ToString("0") + " ms" : "n/a")
              .Append("  loss ").AppendLine(DiagnosticsReadout.NetLoss);
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
                      .Append(s.PeckAmount.ToString("0.#").PadLeft(6))
                      .Append(s.PeckCooldown.ToString("0.00").PadLeft(6))
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

            AppendActiveSlots(sb, a, "  Active slot ");
        }

        /// <summary>One line per running slot (abilities overlap as of Phase 6 chunk 3).</summary>
        private static void AppendActiveSlots(StringBuilder sb, AbilityController a, string prefix)
        {
            for (int i = 0; i < AbilityController.SlotCount; i++)
                if (a.IsSlotActive(i)) sb.Append(prefix).AppendLine(i.ToString());
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
