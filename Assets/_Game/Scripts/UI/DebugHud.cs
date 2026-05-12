using System.Text;
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

        private float _accumulatedFrames;
        private float _accumulatedTime;
        private float _fps;
        private float _nextRefreshUnscaledTime;

        private readonly StringBuilder _sb = new StringBuilder(1024);

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
            if (kb != null && kb.f1Key.wasPressedThisFrame)
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            const int pad = 12;
            const int width = 360;

            _sb.Clear();
            BuildReport(_sb);

            // Center-left so it doesn't fight the top bar or right-side panels.
            int x = pad;
            int y = 110;

            // Approximate height — works because IMGUI clips and overflowing the
            // box is purely cosmetic.
            int height = 22 * 18;

            GUI.Box(new Rect(x, y, width, height), "Debug (F1)");
            GUI.Label(new Rect(x + 10, y + 24, width - 20, height - 32), _sb.ToString());
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
                if (localCtrl.Combat != null)
                {
                    sb.Append("HP: ").Append(localCtrl.Combat.HP.ToString("0"))
                      .Append(localCtrl.Combat.IsStunned ? "  STUNNED" : "").AppendLine();
                }
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
    }
}
