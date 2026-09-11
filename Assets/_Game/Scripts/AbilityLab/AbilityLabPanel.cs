using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.AbilityLab
{
    /// <summary>
    /// Every control in the Ability Lab: class swap, loadout picker, dummy options, time
    /// scale, cooldown override, and the way back to the main menu. Layout lives in
    /// <c>Assets/UI/AbilityLab.uxml</c>, styling in <c>Assets/UI/Styles/AbilityLab.uss</c>.
    /// </summary>
    /// <remarks>
    /// <b>Why this class exists at all.</b> These controls used to be <c>GUILayout</c> widgets
    /// inside <see cref="AbilityLabHud"/>. On this project's settings that is dead code in a
    /// player build: <c>activeInputHandler</c> is 1 ("Input System Package"), and the Input
    /// System package states plainly that it "cannot generate input for IMGUI" and that with
    /// that setting "the <c>OnGUI</c> methods in your player code won't receive any input
    /// events". Editor GUI is exempt, which is why the lab appeared to work — it had only ever
    /// been run from the Editor menu. The lab now ships and is meant to be driven by touch on
    /// a Pixel 9, so the controls had to move to the one UI stack that receives pointer input
    /// in a player: UI Toolkit, which is what the rest of the game's UI already uses.
    /// <para>
    /// <b>The split with <see cref="AbilityLabHud"/> is input, not aesthetics.</b> The HUD
    /// keeps the read-only telemetry, because player IMGUI still DRAWS fine — only input
    /// generation is missing — and a per-frame text dump of live networked state is genuinely
    /// what immediate mode is good at. Anything you can touch belongs here.
    /// </para>
    /// <para>
    /// <b>Still Solo-only, and now that matters more.</b> The lab is reachable at runtime
    /// rather than only from an Editor menu, so the Solo pin in
    /// <c>AbilityLabBootstrapper.Start</c> is what keeps the dummy patrol and the held match
    /// timer sound. Nothing here starts or joins a session; <see cref="ReturnToMenu"/> shuts
    /// the runner down rather than leaving it live across the scene load.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AbilityLabPanel : MonoBehaviour
    {
        /// <summary>Which control the ability picker is currently assigning to.</summary>
        private enum PickerTarget { Passive = -1, Slot0 = 0, Slot1 = 1, Slot2 = 2, Slot3 = 3 }

        private const string Source = "AbilityLab";

        [Tooltip("Scene to return to from the MENU button. Must be in Build Settings.")]
        [SerializeField] private string _bootstrapSceneName = "Bootstrap";

        private AbilityLabBootstrapper _lab;
        private AbilityLabHud _hud;
        private AbilityRegistrySO _registry;
        private INetworkService _network;
        private ILogService _log;

        private VisualElement _root, _sheet, _abilityList;
        private Button _sheetBtn, _telemetryBtn;
        private Toggle _ignoreLegalityToggle, _frozenToggle, _patrolToggle, _zeroCooldownsToggle;
        private Slider _resetIntervalSlider, _timeScaleSlider;
        private Label _resetIntervalLabel, _timeScaleLabel;
        private readonly List<Button> _classChips = new();
        private readonly List<Button> _targetChips = new();

        private PickerTarget _target = PickerTarget.Slot0;
        private bool _zeroCooldowns;
        private bool _sheetOpen;
        private bool _leaving;

        // Rebuild triggers. The chicken and the dummies are spawned asynchronously by the
        // bootstrapper, so the panel cannot bind its initial values in OnEnable — it watches
        // for them to appear instead.
        private ChickenController _lastSeenPlayer;
        private int _lastSeenDummyCount = -1;

        private static readonly ChickenClass[] ClassOrder =
            { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        private static readonly string[] ClassChipNames =
            { "ClassWarrior", "ClassSpeedy", "ClassFatty", "ClassAssassin" };

        private static readonly string[] TargetChipNames =
            { "TargetPassive", "TargetSlot0", "TargetSlot1", "TargetSlot2", "TargetSlot3" };

        [Inject]
        public void Construct(ILogService log, AbilityRegistrySO registry, INetworkService network)
        {
            _log = log;
            _registry = registry;
            _network = network;
        }

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _lab = FindFirstObjectByType<AbilityLabBootstrapper>();
            _hud = FindFirstObjectByType<AbilityLabHud>();

            if (_lab == null)
            {
                _log?.Error(Source, "No AbilityLabBootstrapper in the scene, so this panel controls " +
                    "nothing. It belongs on AbilityLab.unity alongside the lab GameObject.");
                enabled = false;
                return;
            }

            if (_registry == null)
            {
                _log?.Error(Source, "AbilityRegistrySO did not resolve — the ability picker will be " +
                    "empty. Assign it on ProjectInstaller.");
            }

            EnsureEventSystem();
        }

        /// <summary>
        /// UI Toolkit runtime panels need an EventSystem (+ Input System UI module) to receive
        /// pointer clicks. Same helper as <c>MenuUiController</c>, <c>MatchHud</c> and
        /// <c>MatchOverlaysController</c>, for the same reason.
        /// </summary>
        /// <remarks>
        /// Not optional here, and not inherited from anywhere. No scene in the project
        /// contains an EventSystem object — every scene that has UI Toolkit creates one at
        /// runtime through a copy of this helper, and the one <c>MenuUiController</c> creates
        /// in Bootstrap is a plain <c>new GameObject</c> that dies with the scene load. Without
        /// this call the lab's controls AND the touch joystick under it would both be inert:
        /// the exact failure this whole change exists to avoid, and one that only shows up in
        /// a build.
        /// </remarks>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (_root == null)
            {
                _log?.Error(Source, "UIDocument has no rootVisualElement — its Source Asset slot " +
                    "is probably empty. Assign Assets/UI/AbilityLab.uxml.");
                return;
            }

            // Bind next frame so it does not matter whether UIDocument.OnEnable ran first.
            // Same guard MenuUiController uses.
            _root.schedule.Execute(Bind).ExecuteLater(0);
        }

        private void OnDisable()
        {
            // Leave the time scale where we found it. A lab left at 0.25x that silently slows
            // the next scene — or the next Editor play session — would be a nasty little gift.
            if (!Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = 1f;
        }

        // ---- Binding ------------------------------------------------------------

        private void Bind()
        {
            if (_root == null) return;

            _sheet       = _root.Q<VisualElement>("LabSheet");
            _abilityList = _root.Q<VisualElement>("AbilityList");

            // These two are kept as fields because their appearance tracks state
            // (--on when the sheet is open / the telemetry is showing); the rest are
            // fire-and-forget and only need their handler attached.
            _sheetBtn     = BindButton("SheetBtn", () => SetSheetOpen(!_sheetOpen));
            _telemetryBtn = BindButton("TelemetryBtn", ToggleTelemetry);
            BindButton("CloseBtn", () => SetSheetOpen(false));
            BindButton("ResetBtn", () => ResetAllDummies("button"));
            BindButton("MenuBtn", ReturnToMenu);

            // Checked after binding, not before: BindButton already warns per missing
            // control, so this only has to cover the case where losing them means the
            // sheet cannot be opened at all and every warning above was about the same
            // root cause — the wrong asset, or none, on the UIDocument.
            if (_sheet == null || _abilityList == null || _sheetBtn == null)
            {
                _log?.Error(Source, "AbilityLab.uxml is missing #LabSheet, #AbilityList or " +
                    "#SheetBtn — the lab panel cannot be opened. Was the document edited, or " +
                    "the wrong asset assigned to the UIDocument?");
                return;
            }

            BindClassChips();
            BindTargetChips();
            BindToggles();
            BindSliders();

            SetSheetOpen(false);
            RefreshTargetChips();
            RefreshClassChips();
            RefreshTelemetryChip();
            RebuildAbilityList();
        }

        /// <summary>
        /// Attaches <paramref name="onClick"/> to the named Button and returns it, so a caller
        /// that also needs to restyle the control later can keep the reference.
        /// </summary>
        private Button BindButton(string name, System.Action onClick)
        {
            var b = _root.Q<Button>(name);
            if (b == null)
            {
                _log?.Warn(Source, $"AbilityLab.uxml has no #{name}; that control will do nothing.");
                return null;
            }
            b.clicked += onClick;
            return b;
        }

        private void BindClassChips()
        {
            _classChips.Clear();
            for (int i = 0; i < ClassChipNames.Length; i++)
            {
                var chip = _root.Q<Button>(ClassChipNames[i]);
                _classChips.Add(chip);
                if (chip == null) continue;

                var cls = ClassOrder[i];
                chip.clicked += () => RespawnAs(cls);
            }
        }

        private void BindTargetChips()
        {
            _targetChips.Clear();
            for (int i = 0; i < TargetChipNames.Length; i++)
            {
                var chip = _root.Q<Button>(TargetChipNames[i]);
                _targetChips.Add(chip);
                if (chip == null) continue;

                // Index 0 is Passive (-1); 1..4 are slots 0..3.
                var target = (PickerTarget)(i - 1);
                chip.clicked += () =>
                {
                    _target = target;
                    RefreshTargetChips();
                    RebuildAbilityList();
                };
            }
        }

        private void BindToggles()
        {
            _ignoreLegalityToggle = _root.Q<Toggle>("IgnoreLegalityToggle");
            if (_ignoreLegalityToggle != null)
            {
                _ignoreLegalityToggle.SetValueWithoutNotify(_lab.IgnoreLegality);
                _ignoreLegalityToggle.RegisterValueChangedCallback(evt =>
                {
                    _lab.IgnoreLegality = evt.newValue;
                    // The pool this filters IS the list on screen, so it has to be rebuilt or
                    // the toggle appears to do nothing until something else refreshes it.
                    RebuildAbilityList();
                });
            }

            _zeroCooldownsToggle = _root.Q<Toggle>("ZeroCooldownsToggle");
            _zeroCooldownsToggle?.RegisterValueChangedCallback(evt => _zeroCooldowns = evt.newValue);

            _frozenToggle = _root.Q<Toggle>("FrozenToggle");
            _frozenToggle?.RegisterValueChangedCallback(evt => ApplyToDummies(d => d.Frozen = evt.newValue));

            _patrolToggle = _root.Q<Toggle>("PatrolToggle");
            _patrolToggle?.RegisterValueChangedCallback(evt => ApplyToDummies(d => d.Patrol = evt.newValue));
        }

        private void BindSliders()
        {
            _resetIntervalLabel  = _root.Q<Label>("ResetIntervalLabel");
            _resetIntervalSlider = _root.Q<Slider>("ResetIntervalSlider");
            if (_resetIntervalSlider != null)
            {
                _resetIntervalSlider.SetValueWithoutNotify(_lab.DummyResetSeconds);
                _resetIntervalSlider.RegisterValueChangedCallback(evt =>
                {
                    // Writes through to every live dummy, and pulls in a pending deadline that
                    // is now further out than the new interval — see AbilityLabDummy.
                    _lab.DummyResetSeconds = evt.newValue;
                    RefreshResetIntervalLabel();
                });
            }
            RefreshResetIntervalLabel();

            _timeScaleLabel  = _root.Q<Label>("TimeScaleLabel");
            _timeScaleSlider = _root.Q<Slider>("TimeScaleSlider");
            if (_timeScaleSlider != null)
            {
                _timeScaleSlider.SetValueWithoutNotify(Time.timeScale);
                _timeScaleSlider.RegisterValueChangedCallback(evt =>
                {
                    Time.timeScale = evt.newValue;
                    RefreshTimeScaleLabel();
                });
            }
            RefreshTimeScaleLabel();
        }

        // ---- Per-frame ----------------------------------------------------------

        private void Update()
        {
            // Desktop convenience, unchanged from the keyboard-only era. The on-screen RESET
            // is the path that works on the device.
            var kb = Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame) ResetAllDummies("manual");

            WatchForSpawns();
            ApplyZeroCooldowns();
        }

        /// <summary>
        /// Re-seeds the controls once the bootstrapper's asynchronous spawns land.
        /// </summary>
        /// <remarks>
        /// The player chicken and the dummies are spawned from an <c>async</c> <c>Start</c>
        /// after the Fusion runner comes up, so nothing exists to bind to when
        /// <see cref="Bind"/> runs. Rather than racing it with a delay, the panel watches for
        /// the objects to appear. The player check doubles as the class-swap hook:
        /// <c>RespawnPlayerAs</c> despawns and respawns, so the reference changes and the
        /// equipped markers in the list are rebuilt against the new chicken.
        /// </remarks>
        private void WatchForSpawns()
        {
            var player = _lab.Player;
            if (!ReferenceEquals(player, _lastSeenPlayer))
            {
                _lastSeenPlayer = player;
                RefreshClassChips();
                RebuildAbilityList();
            }

            int dummyCount = _lab.Dummies.Count;
            if (dummyCount != _lastSeenDummyCount)
            {
                _lastSeenDummyCount = dummyCount;
                SeedDummyToggles();
            }
        }

        /// <summary>
        /// Holds every slot off cooldown while the toggle is on, by re-arming each timer to
        /// zero each frame. Writes networked state outside <c>FixedUpdateNetwork</c>, which is
        /// sound only because the lab pins <c>GameMode.Single</c> — see the bootstrapper.
        /// </summary>
        private void ApplyZeroCooldowns()
        {
            if (!_zeroCooldowns) return;

            var abilities = PlayerAbilities;
            if (abilities == null || !abilities.HasStateAuthority) return;
            if (abilities.Object == null || !abilities.Object.IsValid) return;

            for (int i = 0; i < AbilityController.SlotCount; i++) abilities.TriggerCooldown(i, 0f);
        }

        // ---- Actions ------------------------------------------------------------

        private void SetSheetOpen(bool open)
        {
            _sheetOpen = open;
            _sheet?.EnableInClassList("cw-lab-sheet--open", open);
            _sheetBtn?.EnableInClassList("cw-lab-tbtn--on", open);
        }

        private void ToggleTelemetry()
        {
            if (_hud == null)
            {
                _log?.Warn(Source, "No AbilityLabHud in the scene, so there is no telemetry " +
                    "readout to toggle.");
                return;
            }
            _hud.Visible = !_hud.Visible;
            RefreshTelemetryChip();
        }

        private void RefreshTelemetryChip() =>
            _telemetryBtn?.EnableInClassList("cw-lab-tbtn--on", _hud != null && _hud.Visible);

        private void RespawnAs(ChickenClass cls)
        {
            _lab.RespawnPlayerAs(cls);
            // Chips and list refresh on their own via WatchForSpawns once the new body lands,
            // but the class chip is repainted now so the tap has immediate feedback.
            RefreshClassChips();
        }

        private void ResetAllDummies(string reason)
        {
            var dummies = _lab.Dummies;
            for (int i = 0; i < dummies.Count; i++) dummies[i]?.ResetNow(reason);
        }

        private void ApplyToDummies(System.Action<AbilityLabDummy> apply)
        {
            var dummies = _lab.Dummies;
            for (int i = 0; i < dummies.Count; i++)
            {
                if (dummies[i] != null) apply(dummies[i]);
            }
        }

        /// <summary>
        /// Shuts the Fusion runner down and returns to the main menu.
        /// </summary>
        /// <remarks>
        /// <b>The shutdown is not optional.</b> On desktop you leave the lab by stopping play
        /// mode, which tears everything down; on a phone there is no such escape, so this
        /// button is the only way out and it has to leave the process in a state the menu can
        /// start a new session from. <c>FusionNetworkService.StartAsync</c> refuses outright
        /// ("a runner is already active") if one is still live, so skipping the shutdown would
        /// mean the next PLAY SOLO silently did nothing — a dead main menu with no error, which
        /// is exactly the kind of failure that reads as "the build is broken".
        /// <para>
        /// <c>async void</c> because a UI Toolkit <c>clicked</c> handler has no Task to return.
        /// The body is wrapped so an exception cannot vanish into the synchronization context
        /// with nothing CluckWars-tagged to show for it — the same reasoning as
        /// <c>AbilityLabBootstrapper.Start</c>. The scene load happens whether or not the
        /// shutdown threw: being stuck in the lab with no way out is strictly worse than
        /// returning to a menu whose network state needs a relaunch, and the log says which
        /// happened.
        /// </para>
        /// </remarks>
        private async void ReturnToMenu()
        {
            if (_leaving) return;   // A double-tap would otherwise start two scene loads.
            _leaving = true;

            try
            {
                if (_network != null) await _network.ShutdownAsync();
            }
            catch (System.Exception e)
            {
                _log?.Error(Source, $"Shutting the runner down on the way out of the lab failed: {e}. " +
                    "Loading the menu anyway — but the next session may refuse to start, in which " +
                    "case relaunch the app.");
            }

            // Restore before leaving: the scale is global and would otherwise follow us into
            // the menu. OnDisable also does this, but only if this component is still enabled
            // when the scene unloads, which is not something to rely on.
            Time.timeScale = 1f;

            _log?.Info(Source, $"Leaving the Ability Lab for '{_bootstrapSceneName}'.");
            SceneManager.LoadScene(_bootstrapSceneName);
        }

        // ---- Refresh ------------------------------------------------------------

        private void RefreshResetIntervalLabel()
        {
            if (_resetIntervalLabel != null)
                _resetIntervalLabel.text = $"Reset every {_lab.DummyResetSeconds:0.0}s";
        }

        private void RefreshTimeScaleLabel()
        {
            if (_timeScaleLabel != null)
                _timeScaleLabel.text = $"Time scale {Time.timeScale:0.00}x";
        }

        private void RefreshClassChips()
        {
            for (int i = 0; i < _classChips.Count; i++)
                _classChips[i]?.EnableInClassList("cw-lab-chip--sel", ClassOrder[i] == _lab.SpawnedClass);
        }

        private void RefreshTargetChips()
        {
            for (int i = 0; i < _targetChips.Count; i++)
                _targetChips[i]?.EnableInClassList("cw-lab-chip--sel", (PickerTarget)(i - 1) == _target);
        }

        private void SeedDummyToggles()
        {
            var dummies = _lab.Dummies;
            if (dummies.Count == 0 || dummies[0] == null) return;

            // SetValueWithoutNotify: this is reading the dummies' current state into the
            // controls, not a user choice. Letting it raise a ChangeEvent would write the
            // value straight back out to every dummy — harmless today, but it would also
            // stamp dummy 0's state onto dummies 1 and 2 the moment they spawned.
            _frozenToggle?.SetValueWithoutNotify(dummies[0].Frozen);
            _patrolToggle?.SetValueWithoutNotify(dummies[0].Patrol);
        }

        /// <summary>
        /// Rebuilds the picker list for the current <see cref="_target"/>, class and legality
        /// setting. Tapping an entry equips it and rebuilds so the equipped marker moves.
        /// </summary>
        private void RebuildAbilityList()
        {
            if (_abilityList == null) return;
            _abilityList.Clear();

            var abilities = PlayerAbilities;

            if (_target == PickerTarget.Passive) BuildPassiveOptions(abilities);
            else BuildActiveOptions(abilities);

            if (_abilityList.childCount == 0)
            {
                _abilityList.Add(new Label(
                    "Nothing legal for this class. Turn on 'Ignore class legality' to see the " +
                    "whole pool.")
                { name = "AbilityListEmpty" });
                _abilityList[0].AddToClassList("cw-lab-empty");
            }
        }

        private void BuildPassiveOptions(AbilityController abilities)
        {
            var options = AbilityLabLoadout.FilterPassives(_registry, _lab.SpawnedClass, _lab.IgnoreLegality);
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                bool equipped = abilities != null && abilities.Passive == option;
                _abilityList.Add(MakeItem(option.DisplayName, equipped, () =>
                {
                    _lab.ApplyLoadout(option, CurrentSlots());
                    RebuildAbilityList();
                }));
            }
        }

        private void BuildActiveOptions(AbilityController abilities)
        {
            var options = AbilityLabLoadout.FilterActives(_registry, _lab.SpawnedClass, _lab.IgnoreLegality);
            int slot = (int)_target;

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                bool equipped = abilities != null && abilities.GetSlot(slot) == option;
                _abilityList.Add(MakeItem(option.DisplayName, equipped, () =>
                {
                    var slots = CurrentSlots();
                    slots[slot] = option;
                    _lab.ApplyLoadout(PlayerAbilities != null ? PlayerAbilities.Passive : null, slots);
                    RebuildAbilityList();
                }));
            }
        }

        private static Button MakeItem(string label, bool equipped, System.Action onClick)
        {
            var b = new Button(onClick) { text = (equipped ? "✓  " : "    ") + label };
            b.AddToClassList("cw-lab-item");
            if (equipped) b.AddToClassList("cw-lab-item--equipped");
            return b;
        }

        /// <summary>The four abilities currently equipped, as a mutable copy for the picker to edit.</summary>
        private AbilityBaseSO[] CurrentSlots()
        {
            var slots = new AbilityBaseSO[AbilityController.SlotCount];
            var abilities = PlayerAbilities;
            if (abilities == null) return slots;
            for (int i = 0; i < slots.Length; i++) slots[i] = abilities.GetSlot(i);
            return slots;
        }

        private AbilityController PlayerAbilities =>
            _lab != null && _lab.Player != null ? _lab.Player.GetComponent<AbilityController>() : null;
    }
}
