using System.Text;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace CluckWars.AbilityLab
{
    /// <summary>
    /// The Ability Lab's instrumentation readout: how long the last press took to become a
    /// cast, how long that cast stayed active, what it hit, and the live cooldown / refusal
    /// state of all four slots. F3 or the on-screen TELEMETRY toggle shows and hides it.
    /// </summary>
    /// <remarks>
    /// <b>Read-only, and that is now a hard constraint rather than a stylistic one.</b>
    /// This panel used to carry the lab's controls too — loadout picker, sliders, toggles.
    /// They have moved to <see cref="AbilityLabPanel"/> (UI Toolkit) because
    /// <b>IMGUI receives no input at all in a player build</b> on this project's settings.
    /// <c>ProjectSettings.activeInputHandler</c> is 1 ("Input System Package"), and the Input
    /// System's own documentation is explicit: "The Input System cannot generate input for
    /// IMGUI" (<c>KnownLimitations.md</c>), and "the <c>OnGUI</c> methods in your player code
    /// won't receive any input events" (<c>UISupport.md</c>). The Editor is the exception —
    /// Editor GUI keeps its events regardless — which is exactly why this was invisible while
    /// the lab was Editor-only. Every button and slider that was here worked in Play Mode and
    /// would have been dead on the first build, Windows included.
    /// <para>
    /// <b>What survives is drawing.</b> Only input generation is missing; repaint still
    /// happens, so an <c>OnGUI</c> panel that never reads <c>Event.current</c> renders
    /// correctly on device. That is the whole reason the telemetry stayed here instead of
    /// being ported: it is a per-frame text dump of live networked state, which immediate
    /// mode gives for free and retained mode would need binding plumbing for. Do not add a
    /// <c>GUILayout.Button</c>, <c>Toggle</c>, <c>Slider</c> or <c>TextField</c> to this file —
    /// it will look right in the Editor and do nothing on the phone.
    /// </para>
    /// <para>
    /// GUI-matrix scaling is lifted from <c>DebugHud</c> for the same reason it exists there:
    /// IMGUI draws in raw pixels and would be illegible on a high-DPI display. On a Pixel 9
    /// (~420 dpi) this lands on the 3x clamp.
    /// </para>
    /// </remarks>
    public sealed class AbilityLabHud : MonoBehaviour
    {
        private const string Source = "AbilityLab";

        [Tooltip("Visible at start? Toggle with F3, or the TELEMETRY control on the lab panel.")]
        [SerializeField] private bool _visible = true;

        private AbilityLabBootstrapper _lab;
        private IInputProvider _input;
        private ILogService _log;

        /// <summary>
        /// Whether the readout is drawn. Settable so <see cref="AbilityLabPanel"/> can offer
        /// an on-screen equivalent of F3 — there is no keyboard on a phone.
        /// </summary>
        public bool Visible { get => _visible; set => _visible = value; }

        // ---- Instrumentation state ----
        // Press timestamps are unscaled on purpose: the time-scale slider is a lab control,
        // and a latency figure that shrank whenever you slowed time down would be a lie.
        private readonly float[] _pressUnscaledTime = new float[AbilityController.SlotCount];
        private readonly bool[] _heldLastFrame = new bool[AbilityController.SlotCount];
        private AbilityController _trackedAbilities;
        private byte _lastSeenCastEventId;
        private bool _haveCastBaseline;
        private float _lastCastLatencyMs = -1f;
        private int _lastCastSlot = AbilityController.InvalidSlot;
        private int _lastCastHits = -1;
        private float _activeWindowStart = -1f;
        private float _lastActiveWindowSeconds = -1f;
        private int _previousActiveSlot = AbilityController.InvalidSlot;

        private readonly StringBuilder _sb = new StringBuilder(1024);

        [Inject]
        public void Construct(ILogService log, IInputProvider input)
        {
            _log = log;
            _input = input;
        }

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _lab = FindFirstObjectByType<AbilityLabBootstrapper>();
            if (_lab == null)
            {
                _log?.Error(Source, "No AbilityLabBootstrapper in the scene, so this panel has " +
                    "nothing to report on. Both components belong on the same GameObject in " +
                    "AbilityLab.unity.");
                enabled = false;
                return;
            }

            if (_input == null)
            {
                _log?.Error(Source, "IInputProvider did not resolve, so press-to-cast latency will " +
                    "read '—' for every cast. It is bound in ProjectInstaller; check that " +
                    "ProjectContext resolved at all.");
            }
        }

        private void Update()
        {
            // Desktop convenience only. The on-screen equivalent lives on AbilityLabPanel,
            // because Keyboard.current is null on the device this tool is aimed at.
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) _visible = !_visible;

            SampleAbilityPresses();
            TrackCastInstrumentation();
        }

        // ---- Instrumentation ---------------------------------------------------

        /// <summary>
        /// Stamps the moment each ability slot went down, so the cast that follows can be
        /// dated against it.
        /// </summary>
        /// <remarks>
        /// <b>Reads <see cref="IInputProvider"/>, not <c>Keyboard.current</c>.</b> It used to
        /// sample the keyboard directly through <c>KeyboardInputProvider.AbilityKeys</c>, which
        /// measured nothing on a phone: with no keyboard attached, no press is ever stamped, so
        /// every cast reports "—" and the one number Maestro wants ON the device was the one
        /// number that could not be read there. Going through the provider measures whichever
        /// path actually fired — arrow key or on-screen hex — which is also the project rule:
        /// gameplay never reads Input directly.
        /// <para>
        /// <b><see cref="IInputProvider.GetAbilityHeld"/>, not <c>GetAbilityNPressed</c>, and
        /// the choice is load-bearing.</b> The press getters are edge-triggered with one-shot
        /// semantics — <c>CompositeInputProvider</c> warns not to read them more than once per
        /// tick, because reading consumes the latch. This panel reading them would race
        /// <c>FusionNetworkService</c> for the same press and swallow casts outright: the lab
        /// would measure the latency of inputs it had just eaten. <c>GetAbilityHeld</c> is
        /// level-triggered with no latch to consume and is documented as safe to read every
        /// frame, so the rising edge is detected here instead. It goes high on the same frame
        /// as the press edge on both providers (a key's <c>isPressed</c> and
        /// <c>wasPressedThisFrame</c> turn on together; the touch controller sets held and
        /// pressed in the same PointerDown), so the measurement is unchanged — only the
        /// observation is now passive.
        /// </para>
        /// </remarks>
        private void SampleAbilityPresses()
        {
            if (_input == null) return;

            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
            {
                bool held = _input.GetAbilityHeld(slot);
                if (held && !_heldLastFrame[slot]) _pressUnscaledTime[slot] = Time.realtimeSinceStartup;
                _heldLastFrame[slot] = held;
            }
        }

        /// <summary>
        /// Closes the loop on the two numbers that answer "does this feel responsive":
        /// how long the press took to become a cast, and how long the cast's active window
        /// actually lasted.
        /// </summary>
        private void TrackCastInstrumentation()
        {
            var abilities = PlayerAbilities;
            if (abilities == null || abilities.Object == null || !abilities.Object.IsValid) return;

            // A class swap respawns the chicken, and the replacement's event id starts from
            // zero. Carrying the old chicken's baseline across would read that as a cast that
            // never happened and stamp a nonsense latency on it, so re-baseline on identity.
            if (!ReferenceEquals(abilities, _trackedAbilities))
            {
                _trackedAbilities = abilities;
                _haveCastBaseline = false;
                _lastCastLatencyMs = -1f;
                _lastActiveWindowSeconds = -1f;
                _lastCastSlot = AbilityController.InvalidSlot;
                _lastCastHits = -1;
                _previousActiveSlot = AbilityController.InvalidSlot;
                _activeWindowStart = -1f;
            }

            // LastCastEventId is a wrapping one-shot signal, so the first read after spawn
            // establishes a baseline instead of counting as a cast.
            byte eventId = abilities.LastCastEventId;
            if (!_haveCastBaseline)
            {
                _lastSeenCastEventId = eventId;
                _haveCastBaseline = true;
            }
            else if (eventId != _lastSeenCastEventId)
            {
                _lastSeenCastEventId = eventId;
                _lastCastSlot = abilities.ActiveSlot;
                _lastCastHits = abilities.LastCastHitCount;

                if (_lastCastSlot >= 0 && _lastCastSlot < AbilityController.SlotCount &&
                    _pressUnscaledTime[_lastCastSlot] > 0f)
                {
                    _lastCastLatencyMs = (Time.realtimeSinceStartup - _pressUnscaledTime[_lastCastSlot]) * 1000f;
                }
                else
                {
                    // A cast with no press behind it: an instant self-buff resolving on the same
                    // frame the slot cleared, or a cast the lab did not originate. Not a whiff,
                    // just unmeasurable — say so rather than report a stale number.
                    _lastCastLatencyMs = -1f;
                }
            }

            int activeSlot = abilities.ActiveSlot;
            if (activeSlot != AbilityController.InvalidSlot && _previousActiveSlot == AbilityController.InvalidSlot)
            {
                _activeWindowStart = Time.realtimeSinceStartup;
            }
            else if (activeSlot == AbilityController.InvalidSlot && _previousActiveSlot != AbilityController.InvalidSlot
                     && _activeWindowStart > 0f)
            {
                _lastActiveWindowSeconds = Time.realtimeSinceStartup - _activeWindowStart;
                _activeWindowStart = -1f;
            }
            _previousActiveSlot = activeSlot;
        }

        private AbilityController PlayerAbilities =>
            _lab != null && _lab.Player != null ? _lab.Player.GetComponent<AbilityController>() : null;

        // ---- Drawing -----------------------------------------------------------

        /// <summary>
        /// IMGUI has no equivalent of PanelSettings scaling, so scale the matrix by pixel
        /// density and keep the rects below in the desktop coordinate space they were
        /// authored in. Same approach and same 3x cap as <c>DebugHud</c>.
        /// </summary>
        private static float HudScale =>
            Screen.dpi > 1f ? Mathf.Clamp(Screen.dpi / 96f, 1f, 3f) : 1f;

        private void OnGUI()
        {
            if (!_visible || _lab == null) return;

            float scale = HudScale;
            var previousMatrix = GUI.matrix;
            if (!Mathf.Approximately(scale, 1f))
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // Heights are sized to their content: the cast readout is a fixed nine lines, the
            // dummy readout four. Undersizing silently clips the last line off the bottom,
            // which is the kind of bug an immediate-mode panel gives you for free.
            DrawInstrumentation(new Rect(12f, 12f, 330f, 200f));
            DrawDummyState(new Rect(12f, 220f, 330f, 120f));

            GUI.matrix = previousMatrix;
        }

        private void DrawInstrumentation(Rect rect)
        {
            GUILayout.BeginArea(rect, "Ability Lab — cast (F3)", GUI.skin.window);
            GUILayout.Space(4f);

            var abilities = PlayerAbilities;
            if (abilities == null)
            {
                GUILayout.Label("Waiting for the lab chicken to spawn…");
                GUILayout.EndArea();
                return;
            }

            _sb.Clear();
            _sb.Append("Class: ").Append(_lab.SpawnedClass)
               .Append("   Passive: ").AppendLine(Name(abilities.Passive));

            _sb.Append("Press to cast: ")
               .AppendLine(_lastCastLatencyMs >= 0f ? $"{_lastCastLatencyMs:0} ms" : "—");

            _sb.Append("Active window: ")
               .AppendLine(_lastActiveWindowSeconds >= 0f ? $"{_lastActiveWindowSeconds:0.000} s" : "—");

            _sb.Append("Last cast: ");
            if (_lastCastSlot >= 0)
            {
                var cast = abilities.GetSlot(_lastCastSlot);
                _sb.Append("slot ").Append(_lastCastSlot).Append(" ").Append(Name(cast));
                // A zero here only means a whiff for abilities that actually report hits;
                // self-buffs and placed zones legitimately report none. AbilityBaseSO owns
                // that distinction, so ask it rather than guessing from the number.
                bool reports = cast != null && cast.ReportsCastHits;
                _sb.Append("  hits=").Append(_lastCastHits < 0 ? "—" : _lastCastHits.ToString());
                if (!reports && cast != null) _sb.Append(" (n/a)");
                else if (_lastCastHits == 0) _sb.Append(" WHIFF");
            }
            else _sb.Append('—');
            _sb.AppendLine();
            _sb.AppendLine();

            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                var ability = abilities.GetSlot(i);
                float cd = abilities.CooldownRemaining(i);
                _sb.Append('[').Append(i).Append("] ").Append(Name(ability))
                   .Append(cd > 0f ? $"  cd {cd:0.0}s" : "  ready")
                   .Append("  ").Append(abilities.EvaluateRefusal(i))
                   .AppendLine();
            }

            GUILayout.Label(_sb.ToString());
            GUILayout.EndArea();
        }

        /// <summary>
        /// The dummies' live control state. Read-only; the controls that change any of it are
        /// on <see cref="AbilityLabPanel"/>.
        /// </summary>
        private void DrawDummyState(Rect rect)
        {
            GUILayout.BeginArea(rect, "Dummy state", GUI.skin.window);
            GUILayout.Space(4f);

            var dummies = _lab.Dummies;
            if (dummies.Count == 0)
            {
                GUILayout.Label("No dummies spawned.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Next reset in {dummies[0].TimeToNextReset:0.0}s");

            var target = FirstDummyController();
            if (target != null)
            {
                GUILayout.Label($"{target.CurrentControlState}" +
                    (target.IsStunned ? $"  (stun {target.StunRemaining:0.0}s)" : string.Empty) +
                    (target.Rooted ? $"  (root {target.RootRemaining:0.0}s)" : string.Empty) +
                    $"  slow x{target.SlowMultiplier:0.00}");

                // Cargo is what the steal abilities read to decide whether the dummy is a
                // legal target at all, so it belongs next to the control state, not hidden.
                var cargo = target.GetComponent<ChickenCargo>();
                if (cargo != null) GUILayout.Label($"Cargo: {cargo.Cargo:0.0} / {cargo.Capacity:0.0}");
            }

            GUILayout.EndArea();
        }

        private ChickenController FirstDummyController()
        {
            var dummies = _lab.Dummies;
            for (int i = 0; i < dummies.Count; i++)
            {
                if (dummies[i] != null) return dummies[i].GetComponent<ChickenController>();
            }
            return null;
        }

        private static string Name(AbilityBaseSO ability) =>
            ability != null ? ability.DisplayName : "(none)";
    }
}
