using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// UI Toolkit menu front-end: Main Menu → Class Select → Loadout → Lobby → Game.
    /// Replaces the procedural-UGUI CharacterSelectController. Visuals live in
    /// Assets/UI/*.uxml + Assets/UI/Styles/CluckWarsTheme.uss; this controller
    /// only binds data and drives navigation. All selections are written back to
    /// <see cref="ISessionSelectionService"/> exactly as the old controller did,
    /// so the spawner (Game scene) keeps working unchanged.
    /// </summary>
    /// <remarks>
    /// Character select used to be one screen (class pick + passive fork + stat
    /// preview + two ability pick rows + detail strip + toggles + ready button).
    /// Split 2026-09 into two steps: Class Select (pick class + specialization)
    /// and Loadout (pick abilities into four explicit slots). The old
    /// "tap a card, it auto-fills the first open slot, and drops the oldest pick
    /// when full" behaviour made the in-match button an ability landed on
    /// unpredictable; it is replaced by an explicit slot-arming state machine
    /// (see <see cref="_armedSlot"/>) that never compacts.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuUiController : MonoBehaviour
    {
        private const string Source = "MenuUI";

        [Header("Page templates (assigned in scene)")]
        [SerializeField] private VisualTreeAsset _mainMenuUxml;
        [SerializeField] private VisualTreeAsset _classSelectUxml;
        // Field name kept as "_characterSelectUxml" (was the whole old character-select
        // screen) to avoid re-pointing the scene reference; it now instantiates Step 2
        // "Loadout" (Assets/UI/CharacterSelect.uxml, reworked in place).
        [SerializeField] private VisualTreeAsset _characterSelectUxml;
        [SerializeField] private VisualTreeAsset _lobbyUxml;

        // ---- Injected services ------------------------------------------------
        private ISessionSelectionService _selection;
        private ILogService              _log;
        private IUGSService              _ugs;
        private SceneLoader              _sceneLoader;
        private ChickenClassRegistrySO   _classRegistry;
        private AbilityRegistrySO        _abilityRegistry;
        private MatchConfigSO            _matchConfig;

        // ---- Runtime state ----------------------------------------------------
        private VisualElement _root;
        private VisualElement _mainMenu, _classSelect, _loadout, _lobby;
        private bool _isBusy;

        private readonly Dictionary<ChickenClass, VisualElement> _classChips = new();
        // (pill element, the passive it selects) for every SpecOptA/SpecOptB across
        // all four chips — populated once in BuildClassSelect, used by
        // RefreshClassSelect to toggle the selected-highlight class.
        private readonly List<(VisualElement Pill, PassiveAbilitySO Passive)> _specPills = new();
        // #PreFilledTag per class chip — populated once in BuildClassSelect, repainted by
        // RefreshPreFilledTags. A dictionary rather than a per-chip Q<Label> lookup on every
        // refresh, matching the _classChips caching pattern.
        private readonly Dictionary<ChickenClass, Label> _preFilledTags = new();
        private VisualElement _previewChicken, _previewGlow, _previewDisc;
        private Label _previewName, _previewQuote;
        private Label _roleCalloutStrong, _roleCalloutWeak;
        // .cw-chicken--<class> currently on the big preview figure (for swap).
        private string _previewChickenClass;

        // The two flat pick rows that replaced the slot-hex row + scrolling grid.
        private VisualElement _commonCards, _classCards;
        private Label _commonHint, _commonCount, _classHint, _classCount;
        // Shared "what does this do" strip — the description's only home. Cards
        // carry icon + name + category/CD; the last-tapped ability's text lands
        // here at full reading size. Null until a card is tapped.
        private VisualElement _abilityDetail;
        private Label _abilityDetailName, _abilityDetailText;
        private AbilityBaseSO _focusedAbility;
        private Button _readyBtn;

        // The four physical slot boxes on the Loadout screen (SlotBox0..3). The
        // ARMED slot is the one the next ability-card tap will act on — see the
        // state machine on OnAbilityCardTapped/AutoArmFirstEmpty. Always a valid
        // index into 0..3 once BuildLoadout has run.
        private VisualElement[] _slotBoxes = new VisualElement[4];
        private int _armedSlot;

        // Dim neutral tint for an empty ability hex (no equipped accent).
        private static readonly Color HexEmptyTint = new Color(0.45f, 0.38f, 0.28f, 0.7f);

        private static readonly ChickenClass[] Order =
            { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        private sealed class ClassMeta
        {
            // PassiveName/PassiveDesc were removed on 2026-08-23. They still carried the
            // damage-era passives deleted in 62219fc ("MIGHTY: +25% outgoing ability damage",
            // "COMBO: equips 3 abilities instead of 2") and were rendering on the lobby card.
            // Beyond being stale, a per-CLASS passive name is now structurally wrong: the
            // passive comes from the chosen specialization, so any single hardcoded name is
            // right for at most one of a class's two builds. Role is stable across both.
            public string Name, Role;
            public Color Tint;
            public int[] Stats; // cargo, rate, speed (1..5) — HP/Resist removed in v0.4 (no health)
        }

        private static readonly Dictionary<ChickenClass, ClassMeta> Meta = new()
        {
            [ChickenClass.Warrior]  = new ClassMeta { Name = "WARRIOR CHICKEN",  Role = "All-Rounder",  Tint = UiGfx.Hex32("C04030"),  Stats = new[]{3,3,3} },
            [ChickenClass.Speedy]   = new ClassMeta { Name = "SPEEDY CHICKEN",   Role = "Hit & Run",    Tint = UiGfx.Hex32("E85A2A"), Stats = new[]{2,3,5} },
            [ChickenClass.Fatty]    = new ClassMeta { Name = "FATTY CHICKEN",    Role = "Bulk Carrier", Tint = UiGfx.Hex32("F5D75A"),       Stats = new[]{5,5,2} },
            [ChickenClass.Assassin] = new ClassMeta { Name = "ASSASSIN CHICKEN", Role = "Disruptor",    Tint = UiGfx.Hex32("7B68EE"), Stats = new[]{2,2,4} },
        };

        private static readonly string[] ChipNames  = { "ClassWarrior", "ClassSpeedy", "ClassFatty", "ClassAssassin" };

        /// <summary>Authored copy for RoleCalloutStrong/RoleCalloutWeak, narrative-designer pass
        /// (2026-09-17), cross-checked against <see cref="Meta"/>'s real cargo/rate/speed spread
        /// so nothing here reads a strength the numbers don't actually back. Replaces the old
        /// numeric stat-pip row and the placeholder "STRONG: X/Y" computed text — Maestro
        /// rejected numeric HP/damage-style pips since no such stat exists post GDD §2 combat
        /// rewrite, and the placeholder wasn't real copy.</summary>
        private sealed class RoleCallout { public string Strong; public string Weak; }
        private static readonly Dictionary<ChickenClass, RoleCallout> RoleCallouts = new()
        {
            // Warrior — {3,3,3}: flat spread, nothing to call out as strong OR weak, so this
            // is the one class with a single-line callout instead of a strong/weak pair.
            [ChickenClass.Warrior]  = new RoleCallout { Strong = "No weak spot. No big edge, either." },
            // Speedy — {cargo 2, rate 3, speed 5}: fastest, lightest carrier.
            [ChickenClass.Speedy]   = new RoleCallout { Strong = "Fastest bird in the yard.",       Weak = "Can't carry much at a time." },
            // Fatty — {cargo 5, rate 5, speed 2}: biggest hauler, slowest mover.
            [ChickenClass.Fatty]    = new RoleCallout { Strong = "Hauls the most, fastest hands.",  Weak = "Slowest waddle in the coop." },
            // Assassin — {cargo 2, rate 2, speed 4}: fast, but thin on cargo and hands.
            [ChickenClass.Assassin] = new RoleCallout { Strong = "Blink and it's gone.",            Weak = "Light load, slow hands." },
        };

        // Ability categories. These used to be section headers in a scrolling
        // grid; with 3 cards per row that produced headers holding one card each,
        // so the category now rides on the card itself as a colored tag.
        private sealed class CatMeta { public string Label; public Color Color; }
        private static readonly Dictionary<AbilityCategory, CatMeta> Cats = new()
        {
            [AbilityCategory.Steal]   = new CatMeta { Label = "STEAL",   Color = UiGfx.Hex32("FF5722") },
            [AbilityCategory.Control] = new CatMeta { Label = "CONTROL", Color = UiGfx.Hex32("9C27B0") },
            [AbilityCategory.Defense] = new CatMeta { Label = "DEFENSE", Color = UiGfx.Hex32("4CAF50") },
            [AbilityCategory.Utility] = new CatMeta { Label = "UTILITY", Color = UiGfx.Hex32("00BCD4") },
        };

        // ======================================================================
        [Inject]
        public void Construct(
            ISessionSelectionService selection,
            ILogService log,
            IUGSService ugs,
            [InjectOptional] SceneLoader sceneLoader,
            [InjectOptional] ChickenClassRegistrySO classRegistry,
            [InjectOptional] AbilityRegistrySO abilityRegistry,
            [InjectOptional] MatchConfigSO matchConfig)
        {
            _selection       = selection;
            _log             = log;
            _ugs             = ugs;
            _sceneLoader     = sceneLoader;
            _classRegistry   = classRegistry;
            _abilityRegistry = abilityRegistry;
            _matchConfig     = matchConfig;
        }

        private void Awake()
        {
            if (_selection == null)
                ProjectContext.Instance.Container.Inject(this);

            if (_sceneLoader == null) _sceneLoader = GetComponent<SceneLoader>();
            if (_sceneLoader == null) _sceneLoader = FindFirstObjectByType<SceneLoader>();
            _sceneLoader?.SetAutoLoad(false);

            EnsureEventSystem();
        }

        /// <summary>
        /// UI Toolkit runtime panels need an EventSystem (+ Input System UI module)
        /// to receive pointer clicks. The old UGUI flow created one; replicate that
        /// here so the menu is interactive even if the scene has none.
        /// </summary>
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
            if (_root == null) return;
            // Build next frame so it doesn't matter whether UIDocument.OnEnable ran first.
            _root.schedule.Execute(BuildAll).ExecuteLater(0);
        }

        private void BuildAll()
        {
            if (_root == null) return;
            _root.Clear();
            _root.style.flexGrow = 1;

            // Guaranteed brand font even if a USS url() font ref fails to resolve.
            var f = UiGfx.ChunkyFont();
            if (f != null) _root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(f));

            _mainMenu    = ClonePage(_mainMenuUxml);
            _classSelect = ClonePage(_classSelectUxml);
            _loadout     = ClonePage(_characterSelectUxml);
            _lobby       = ClonePage(_lobbyUxml);

            BuildMainMenu();
            BuildClassSelect();
            BuildLoadout();
            BuildLobby();

            _root.RegisterCallback<GeometryChangedEvent>(OnRootGeometry);
            UpdateLayout(_root.resolvedStyle.width, _root.resolvedStyle.height);

            if (_selection != null && _selection.SelectedClass == default)
                _selection.SelectedClass = ChickenClass.Warrior;

            ShowMainMenu();
        }

        private VisualElement ClonePage(VisualTreeAsset vta)
        {
            if (vta == null) return new VisualElement();
            var ve = vta.Instantiate();
            ve.style.flexGrow = 1;
            ve.style.display = DisplayStyle.None;
            _root.Add(ve);
            return ve;
        }

        // ---- Layout -----------------------------------------------------------
        private void OnRootGeometry(GeometryChangedEvent evt) =>
            UpdateLayout(evt.newRect.width, evt.newRect.height);

        private void UpdateLayout(float w, float h)
        {
            bool landscape = w >= h;
            _root.EnableInClassList("layout--landscape", landscape);
            _root.EnableInClassList("layout--portrait", !landscape);
        }

        /// <summary>Applies the emoji font to every element tagged .cw-emoji-text under <paramref name="root"/>.</summary>
        private static void ApplyEmojiFont(VisualElement root)
        {
            var ef = UiGfx.EmojiFont();
            if (ef == null || root == null) return;
            var emojis = root.Query<Label>(className: "cw-emoji-text").ToList();
            foreach (var e in emojis) e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
        }

        // ---- Navigation -------------------------------------------------------
        private void ShowMainMenu()    { SetPage(_mainMenu); RefreshDevRow(); }
        private void ShowClassSelect() { SetPage(_classSelect); RefreshClassSelect(); }
        private void ShowLoadout()     { SetPage(_loadout); RefreshLoadout(); }
        private void ShowLobby()       { SetPage(_lobby); RefreshLobby(); }

        private void SetPage(VisualElement page)
        {
            if (_mainMenu    != null) _mainMenu.style.display    = page == _mainMenu    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_classSelect != null) _classSelect.style.display = page == _classSelect ? DisplayStyle.Flex : DisplayStyle.None;
            if (_loadout     != null) _loadout.style.display     = page == _loadout     ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lobby       != null) _lobby.style.display       = page == _lobby       ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ======================================================================
        //  MAIN MENU
        // ======================================================================
        private void BuildMainMenu()
        {
            Bind<Button>(_mainMenu, "SoloBtn", b => b.clicked += () => ChooseMode(SessionMode.Solo));
            Bind<Button>(_mainMenu, "HostBtn", b => b.clicked += () => ChooseMode(SessionMode.Host));
            Bind<Button>(_mainMenu, "JoinBtn", b => b.clicked += () => ChooseMode(SessionMode.Join));
            Bind<Button>(_mainMenu, "AbilityLabBtn", b => b.clicked += OpenAbilityLab);

            // Build stamp — a tester reporting a bug from a device otherwise has no
            // way to say which build produced it.
            Bind<Label>(_mainMenu, "BuildStamp", l => l.text = $"v{Application.version}");
        }

        /// <summary>
        /// Shows or hides <c>#DevRow</c> to match
        /// <see cref="PlayerPreferences.DeveloperModeEnabled"/>.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="ShowMainMenu"/> rather than once from
        /// <see cref="BuildMainMenu"/>, because the toggle that sets the preference lives on
        /// the Loadout screen — i.e. the player is always somewhere else when they change it.
        /// Binding once would mean the button only appeared on the next launch, which on a
        /// phone is a reinstall-and-relaunch cycle to discover a feature that is already there.
        /// </remarks>
        private void RefreshDevRow()
        {
            Bind<VisualElement>(_mainMenu, "DevRow", r =>
                r.style.display = PlayerPreferences.DeveloperModeEnabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None);
        }

        /// <summary>
        /// Build-Settings name of the lab scene. Enabled in Build Settings since 2026-09-04 so
        /// it exists on a device; reaching it is gated on Developer Mode, not on the build.
        /// </summary>
        private const string AbilityLabSceneName = "AbilityLab";

        /// <summary>
        /// Loads <c>AbilityLab.unity</c>, the developer ability-feel scene.
        /// </summary>
        /// <remarks>
        /// <c>SceneManager</c> directly rather than <c>SceneLoader</c>: that component exists
        /// to run the Bootstrap → Game handoff and carries a serialized "next scene", so
        /// pointing it at the lab would mean mutating the menu's own flow to take a detour.
        /// The lab is a leaf — it loads, and its MENU button loads Bootstrap back.
        ///
        /// No <c>ISessionSelectionService</c> write here. <c>AbilityLabBootstrapper.Start</c>
        /// pins the mode to Solo itself, deliberately, rather than inheriting whatever the
        /// menu last left behind — its dummy patrol and held match timer are only sound on a
        /// single peer.
        /// </remarks>
        private void OpenAbilityLab()
        {
            if (!PlayerPreferences.DeveloperModeEnabled)
            {
                // Not reachable through the UI — the row is hidden. Refuse anyway rather than
                // trust that, so the gate holds even if a later change shows the row wrongly.
                _log?.Warn(Source, "Ability Lab requested with Developer Mode off. Ignoring.");
                return;
            }

            _log?.Info(Source, "Opening the Ability Lab.");
            UnityEngine.SceneManagement.SceneManager.LoadScene(AbilityLabSceneName);
        }

        private void ChooseMode(SessionMode mode)
        {
            if (_selection != null) _selection.Mode = mode;
            ShowClassSelect();
        }

        // ======================================================================
        //  STEP 1 — CLASS SELECT ("Choose Your Chicken")
        // ======================================================================
        private void BuildClassSelect()
        {
            _classChips.Clear();
            _specPills.Clear();
            _preFilledTags.Clear();

            for (int i = 0; i < Order.Length; i++)
            {
                var cls = Order[i];
                var chip = _classSelect.Q<VisualElement>(ChipNames[i]);
                if (chip == null) continue;
                _classChips[cls] = chip;

                var preFilled = chip.Q<Label>("PreFilledTag");
                if (preFilled != null) _preFilledTags[cls] = preFilled;

                // Class chip art — exported design chicken sprite (Stage-3). Chips
                // are fixed per class, so the modifier is applied once here.
                var art = chip.Q<VisualElement>(ChipNames[i] + "Art");
                if (art != null) art.AddToClassList("cw-chicken--" + KeyOf(cls));

                // Signature first, alternative second — registry order would put Bracer
                // ahead of Mighty and Second Wind ahead of Slippery, reading as if the
                // alternative were the class's identity.
                var passives = _abilityRegistry?.GetPassivesForClass(cls)
                                                .OrderByDescending(p => p.IsSignature)
                                                .ToList();
                if (passives != null && passives.Count >= 2)
                {
                    BindSpecPill(chip.Q<VisualElement>("SpecOptA"), cls, passives[0]);
                    BindSpecPill(chip.Q<VisualElement>("SpecOptB"), cls, passives[1]);
                }

                // Deliberately no click handler on the chip root itself: Maestro's
                // one-tap requirement means class + specialization are chosen together
                // by tapping a pill (below), so the chip body has no separate
                // "class only" action any more.
            }

            _previewChicken    = _classSelect.Q<VisualElement>("PreviewChicken");
            _previewGlow       = _classSelect.Q<VisualElement>("PreviewGlow");
            _previewDisc       = _classSelect.Q<VisualElement>("PreviewDisc");
            _previewName       = _classSelect.Q<Label>("PreviewName");
            _previewQuote      = _classSelect.Q<Label>("PreviewQuote");
            _roleCalloutStrong = _classSelect.Q<Label>("RoleCalloutStrong");
            _roleCalloutWeak   = _classSelect.Q<Label>("RoleCalloutWeak");

            ApplyEmojiFont(_classSelect);

            Bind<Button>(_classSelect, "HomeBtn", b => b.clicked += ShowMainMenu);
            // Always enabled — BuildAll already seeds a default SelectedClass, so
            // there is never a state where Step 1 has nothing chosen yet.
            Bind<Button>(_classSelect, "NextBtn", b => b.clicked += ShowLoadout);
        }

        /// <summary>
        /// Wires one SpecOptA/SpecOptB pill: fills its name/description/signature-line
        /// children by CSS class (the pill names repeat across all four chips, so these
        /// are queried per-chip, not globally) and registers the one-tap
        /// class+specialization select.
        /// </summary>
        private void BindSpecPill(VisualElement pill, ChickenClass cls, PassiveAbilitySO passive)
        {
            if (pill == null || passive == null) return;

            var nameLbl = pill.Q<Label>(className: "cw-spec-pill__name");
            var descLbl = pill.Q<Label>(className: "cw-spec-pill__desc");
            var sigLbl  = pill.Q<Label>(className: "cw-spec-pill__signature");

            if (nameLbl != null) nameLbl.text = passive.DisplayName.ToUpperInvariant();
            // Never ShortLabel — that is the ≤4-char HUD abbreviation.
            if (descLbl != null) descLbl.text = passive.Description;

            if (sigLbl != null)
            {
                // Accurate language: this occupies one of the four ability slots, it is
                // never a free bonus.
                string label = ForcedAbilityLabel(cls, passive);
                string sig = label != null ? $"Starts with {label} equipped" : null;
                sigLbl.text = sig ?? string.Empty;
                sigLbl.style.display = sig != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            pill.RegisterCallback<ClickEvent>(evt =>
            {
                // Without this the click would bubble to the (handler-less) chip root
                // too; StopPropagation just makes that explicit rather than relying on
                // there being nothing to run.
                evt.StopPropagation();
                SelectClassAndPassive(cls, passive);
            });

            _specPills.Add((pill, passive));
        }

        /// <summary>
        /// Selects a class and its specialization (passive) in one tap — Maestro's hard
        /// requirement, replacing the old two-step SelectClass/SelectPassive. Old picks
        /// only get cleared when the CLASS actually changes; re-selecting the same
        /// class's other pill must not wipe an in-progress loadout.
        /// </summary>
        private void SelectClassAndPassive(ChickenClass cls, PassiveAbilitySO passive)
        {
            if (_selection == null) return;

            if (cls != _selection.SelectedClass)
            {
                // Old picks may be illegal for the new class.
                _focusedAbility = null;
                _selection.Ability0 = null;
                _selection.Ability1 = null;
                _selection.Ability2 = null;
                _selection.Ability3 = null;
            }

            _selection.SelectedClass = cls;
            _selection.Passive = passive;

            // Seed the mandatory Peck and/or this specialization's signature ability so the
            // picker shows the same loadout the spawner would build. Without this the player
            // composes four abilities, hits READY, and MatchBootstrapper silently swaps one
            // out — a UI that lied.
            SeedForcedAbilities();
            AutoArmFirstEmpty();

            RefreshClassSelect();
        }

        private ChickenClass Cls => _selection?.SelectedClass ?? ChickenClass.Warrior;
        /// <summary>
        /// Total active-ability slots the loadout has to fill — four for every class as
        /// of v0.7, which retired COMBO's old job of granting a third. Purely a slot
        /// COUNT: any class-legal ability, Common or Character, may land in any slot.
        /// Peck and/or the chosen specialization's signature are the only forced
        /// occupants — see <see cref="SeedForcedAbilities"/>.
        /// </summary>
        private int ActiveSlotsForClass => AbilityController.SlotCount;

        private Color TintOf(ChickenClass cls)
        {
            if (_classRegistry != null && _classRegistry.TryGet(cls, out var e) && e.TintColor.a > 0f)
                return e.TintColor;
            return Meta[cls].Tint;
        }

        private void RefreshClassSelect()
        {
            var cls = Cls;
            var m = Meta[cls];

            foreach (var kv in _classChips)
            {
                bool sel = kv.Key == cls;
                kv.Value.EnableInClassList("cw-card--selected", sel);
                SetBorder(kv.Value, sel ? TintOf(kv.Key) : UiGfx.CardBorder);
                // Selected chip carries a faint class-color wash (design active chip),
                // not just a tinted border.
                kv.Value.style.backgroundColor = sel ? Fade(TintOf(kv.Key), 0.22f) : UiGfx.CardTop;
            }

            foreach (var (pill, passive) in _specPills)
                pill.EnableInClassList("cw-spec-pill--selected", _selection?.Passive == passive);

            if (_previewChicken != null)
            {
                if (!string.IsNullOrEmpty(_previewChickenClass))
                    _previewChicken.RemoveFromClassList(_previewChickenClass);
                _previewChickenClass = "cw-chicken--" + KeyOf(cls);
                _previewChicken.AddToClassList(_previewChickenClass);
            }
            if (_previewGlow != null)
                _previewGlow.style.unityBackgroundImageTintColor = Fade(TintOf(cls), 0.35f);
            if (_previewDisc != null)
            {
                var tint = TintOf(cls);
                SetBorder(_previewDisc, Fade(tint, 0.55f));
                _previewDisc.style.backgroundColor = Fade(tint, 0.10f); // subtle class-tinted platform
            }
            // Raw class tint as text on the near-black screen bg is too dark to read
            // — Warrior's #C04030 lands at 2.5:1, under the 3:1 large-text minimum.
            // Lighten for type only; the disc border and glow keep the pure tint.
            if (_previewName != null) { _previewName.text = m.Name; _previewName.style.color = Lighten(TintOf(cls), 0.35f); }
            if (_previewQuote != null)
            {
                if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry) && !string.IsNullOrEmpty(entry.LoreQuote))
                    _previewQuote.text = $"\"{entry.LoreQuote}\"";
                else
                    _previewQuote.text = "";
            }

            RefreshRoleCallouts(cls);
            RefreshPreFilledTags();
        }

        /// <summary>
        /// Paints every class chip's #PreFilledTag from whichever ability
        /// <see cref="ForcedAbilityLabel"/> reports for it — the selected class's actual chosen
        /// passive, or the other three classes' default (signature) passive as a preview of
        /// what tapping in would force. Hidden for a class with nothing forced, so the tag
        /// never renders as an empty pill.
        /// </summary>
        private void RefreshPreFilledTags()
        {
            var cls = Cls;
            foreach (var (c, tag) in _preFilledTags)
            {
                var passive = c == cls ? _selection?.Passive : DefaultPassiveFor(c);
                string label = ForcedAbilityLabel(c, passive);

                if (label != null)
                {
                    // Real language: it occupies one of the four active slots, never a
                    // free bonus on top of them — see PassiveAbilitySO.SignatureAbility.
                    tag.text = $"One slot pre-filled: {label}";
                    tag.style.display = DisplayStyle.Flex;
                }
                else
                {
                    tag.text = string.Empty;
                    tag.style.display = DisplayStyle.None;
                }
            }
        }

        /// <summary>
        /// Paints RoleCalloutStrong/RoleCalloutWeak from the authored <see cref="RoleCallouts"/>
        /// copy. A class with no Weak line (Warrior — flat {3,3,3}) collapses to a single
        /// strong-slot callout rather than showing an empty second line.
        /// </summary>
        private void RefreshRoleCallouts(ChickenClass cls)
        {
            if (_roleCalloutStrong == null && _roleCalloutWeak == null) return;
            if (!RoleCallouts.TryGetValue(cls, out var callout)) return;

            if (_roleCalloutStrong != null)
            {
                _roleCalloutStrong.text = callout.Strong;
                _roleCalloutStrong.style.display = DisplayStyle.Flex;
            }
            if (_roleCalloutWeak != null)
            {
                bool hasWeak = !string.IsNullOrEmpty(callout.Weak);
                _roleCalloutWeak.text = callout.Weak ?? string.Empty;
                _roleCalloutWeak.style.display = hasWeak ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// Paints the shared detail strip from <see cref="_focusedAbility"/>. Until
        /// a card is tapped it shows a prompt rather than an ability, so the strip
        /// never renders as an empty box the player has to interpret.
        /// </summary>
        private void RefreshAbilityDetail()
        {
            if (_abilityDetailText == null) return;

            var ab = _focusedAbility;
            if (ab == null)
            {
                if (_abilityDetailName != null)
                {
                    _abilityDetailName.text = string.Empty;
                    _abilityDetailName.style.display = DisplayStyle.None;
                }
                _abilityDetailText.text = "Tap an ability to see what it does.";
                _abilityDetailText.style.color = UiGfx.TextSecondary;
                if (_abilityDetail != null) SetBorder(_abilityDetail, UiGfx.CardBorder);
                return;
            }

            if (_abilityDetailName != null)
            {
                string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
                _abilityDetailName.text = label.ToUpperInvariant();
                _abilityDetailName.style.color = ab.AccentColor;
                _abilityDetailName.style.display = DisplayStyle.Flex;
            }
            _abilityDetailText.text = ab.Description;
            _abilityDetailText.style.color = UiGfx.TextPrimary;
            if (_abilityDetail != null) SetBorder(_abilityDetail, Fade(ab.AccentColor, 0.6f));
        }

        // ======================================================================
        //  STEP 2 — LOADOUT ("Build Your Loadout")
        // ======================================================================
        private void BuildLoadout()
        {
            _commonCards    = _loadout.Q<VisualElement>("CommonCards");
            _classCards     = _loadout.Q<VisualElement>("ClassCards");
            _commonHint     = _loadout.Q<Label>("CommonHint");
            _commonCount    = _loadout.Q<Label>("CommonCount");
            _classHint      = _loadout.Q<Label>("ClassHint");
            _classCount     = _loadout.Q<Label>("ClassCount");
            _abilityDetail     = _loadout.Q<VisualElement>("AbilityDetail");
            _abilityDetailName = _loadout.Q<Label>("AbilityDetailName");
            _abilityDetailText = _loadout.Q<Label>("AbilityDetailText");
            _readyBtn       = _loadout.Q<Button>("ReadyBtn");
            if (_readyBtn != null) _readyBtn.clicked += OnReady;

            _slotBoxes = new VisualElement[4];
            for (int i = 0; i < _slotBoxes.Length; i++)
            {
                var box = _loadout.Q<VisualElement>("SlotBox" + i);
                _slotBoxes[i] = box;
                if (box == null) continue;
                int captured = i; // closures capture by reference — copy the loop var
                box.RegisterCallback<ClickEvent>(_ => OnSlotBoxTapped(captured));
            }

            ApplyEmojiFont(_loadout);

            Bind<Button>(_loadout, "BackBtn", b => b.clicked += ShowClassSelect);
            BindRangeGuidesToggle();
            BindDeveloperModeToggle();
        }

        /// <summary>
        /// Wires #DeveloperModeToggle to <see cref="PlayerPreferences.DeveloperModeEnabled"/>,
        /// which is what reveals the Ability Lab entry on the main menu.
        /// </summary>
        /// <remarks>
        /// Seeded with <c>SetValueWithoutNotify</c> for the same reason as the range-guides
        /// toggle below: the seed is not a player choice, and letting it raise a ChangeEvent
        /// would echo the stored value back to <c>PlayerPrefs</c> on every visit to this
        /// screen. Here that would also write the developer-mode key on the machine of every
        /// player who ever opened the Loadout screen, turning "never chosen" into "explicitly
        /// chosen off" — harmless in effect, but it makes the pref file lie about what the
        /// player has actually touched.
        ///
        /// Nothing is refreshed here on toggle: the only thing this preference controls is
        /// #DevRow on the main menu, and <see cref="ShowMainMenu"/> re-reads it on the way
        /// back. Doing it there rather than here also covers the preference being changed by
        /// any other route.
        /// </remarks>
        private void BindDeveloperModeToggle()
        {
            Bind<Toggle>(_loadout, "DeveloperModeToggle", t =>
            {
                t.SetValueWithoutNotify(PlayerPreferences.DeveloperModeEnabled);
                t.RegisterValueChangedCallback(evt =>
                {
                    PlayerPreferences.DeveloperModeEnabled = evt.newValue;
                    _log?.Info(Source, $"Developer mode {(evt.newValue ? "enabled" : "disabled")}.");
                });
            });
        }

        /// <summary>
        /// Wires #RangeGuidesToggle to <see cref="PlayerPreferences.AbilityRangeGuidesEnabled"/>.
        /// </summary>
        /// <remarks>
        /// Initialised FROM the preference rather than from the UXML, because the preference
        /// defaults to true when unwritten and the control must agree with what the player is
        /// about to see in the match. <c>SetValueWithoutNotify</c>, not <c>value</c>: the seed
        /// is not a player choice, and letting it raise a ChangeEvent would echo the stored
        /// value straight back to <c>PlayerPrefs</c> on every visit to this screen, turning
        /// "never chosen, defaulting to on" into "explicitly chosen".
        ///
        /// Bound once in <see cref="BuildLoadout"/>, not per refresh — the control is
        /// static markup, and a second RegisterValueChangedCallback on the same Toggle would
        /// run the setter twice per click.
        /// </remarks>
        private void BindRangeGuidesToggle()
        {
            Bind<Toggle>(_loadout, "RangeGuidesToggle", t =>
            {
                t.SetValueWithoutNotify(PlayerPreferences.AbilityRangeGuidesEnabled);
                t.RegisterValueChangedCallback(evt =>
                {
                    PlayerPreferences.AbilityRangeGuidesEnabled = evt.newValue;
                    _log?.Info(Source, $"Ability range guides {(evt.newValue ? "enabled" : "disabled")}.");
                });
            });
        }

        /// <summary>
        /// Entering Step 2 (or returning to it) always re-arms the first empty slot before
        /// anything else refreshes, so there is never a moment where the screen is visible
        /// but no slot is a visible tap target.
        /// </summary>
        private void RefreshLoadout()
        {
            AutoArmFirstEmpty();
            RebuildPickRows(Cls);
            RefreshAbilityDetail();
            RefreshEquippedState();
            RefreshSlotBoxes();
        }

        // ----------------------------------------------------------------------
        //  Slot-arming state machine — the actual fix for "feels like a lottery".
        // ----------------------------------------------------------------------
        //  _armedSlot is the slot the next ability-card tap acts on. Tapping a
        //  slot box just re-arms; tapping an ability card resolves against
        //  whichever slot is currently armed. Nothing ever compacts — a slot
        //  that goes empty stays empty until the player deliberately fills it.
        // ----------------------------------------------------------------------

        /// <summary>
        /// Arms the lowest-indexed empty slot, or slot 0 if every slot is full. Called on
        /// every entry to Step 2 and after any pick that fills a slot, so there is always a
        /// visible target for the next tap without the player having to manually re-arm.
        /// </summary>
        private void AutoArmFirstEmpty()
        {
            int n = ActiveSlotsForClass;
            for (int i = 0; i < n; i++)
            {
                if (GetEquipped(i) == null) { _armedSlot = i; return; }
            }

            if (n <= 0)
            {
                // Unreachable today — ActiveSlotsForClass is AbilityController.SlotCount, a
                // compile-time constant > 0 — but if that ever stops being true there is no
                // valid slot to arm. Log rather than leave _armedSlot pointing at nothing.
                _log?.Warn(Source, "AutoArmFirstEmpty: ActiveSlotsForClass <= 0, defaulting to slot 0.");
            }
            _armedSlot = 0;
        }

        /// <summary>Tapping a slot box (filled or empty) only re-arms it — no equip/unequip.</summary>
        private void OnSlotBoxTapped(int index)
        {
            _armedSlot = index;
            RefreshSlotBoxes();
        }

        /// <summary>
        /// One tap on an ability card does one of three things depending on where
        /// <paramref name="ab"/> currently sits relative to <see cref="_armedSlot"/>:
        /// <list type="bullet">
        /// <item>already IN the armed slot → clear it (armed slot stays armed, now empty)</item>
        /// <item>equipped in some OTHER slot → swap it into the armed slot</item>
        /// <item>not equipped anywhere → place it in the armed slot, then auto-advance
        /// arming to the next open slot as a convenience</item>
        /// </list>
        /// This subsumes the old Peck-only "tap to move" special case — every ability now
        /// moves the same way — and there is no "all full, drop the oldest" branch any more:
        /// every case resolves into exactly one of the four slots without needing to make
        /// room by shifting.
        /// </summary>
        private void OnAbilityCardTapped(AbilityBaseSO ab)
        {
            if (_selection == null || ab == null) return;

            _focusedAbility = ab;
            RefreshAbilityDetail();

            int existing = SlotOf(ab);

            if (existing == _armedSlot)
            {
                // Case A — tap the ability sitting in the armed slot to clear it.
                SetEquipped(_armedSlot, null);
            }
            else if (existing >= 0)
            {
                // Case B — ab is equipped elsewhere; swap it into the armed slot.
                var displaced = GetEquipped(_armedSlot);
                SetEquipped(existing, displaced);
                SetEquipped(_armedSlot, ab);
            }
            else
            {
                // Case C — ab is unequipped; place it in the armed slot (whatever was
                // there returns to being pickable), then auto-advance for the next tap.
                SetEquipped(_armedSlot, ab);
                AutoArmFirstEmpty();
            }

            RebuildPickRows(Cls);
            RefreshSlotBoxes();
            RefreshEquippedState();
        }

        /// <summary>Redraws all four slot boxes: equipped ability (icon/name/numbered badge)
        /// or an empty state, plus the armed-slot highlight.</summary>
        private void RefreshSlotBoxes()
        {
            for (int i = 0; i < _slotBoxes.Length; i++)
            {
                var box = _slotBoxes[i];
                if (box == null) continue;

                box.EnableInClassList("cw-slot-box--armed", i == _armedSlot);

                var ab = GetEquipped(i);
                box.Clear();
                box.EnableInClassList("cw-slot-box--empty", ab == null);

                if (ab == null)
                {
                    SetBorder(box, UiGfx.CardBorder);
                    box.style.backgroundColor = UiGfx.CardTop;
                    var empty = new Label("EMPTY");
                    empty.AddToClassList("cw-slot-box__empty-label");
                    box.Add(empty);
                    continue;
                }

                SetBorder(box, ab.AccentColor);
                box.style.backgroundColor = Fade(ab.AccentColor, 0.20f);

                string iconCls = AbilityIconStyle.ClassFor(ab);
                if (!string.IsNullOrEmpty(iconCls))
                {
                    var sprite = new VisualElement();
                    sprite.AddToClassList("cw-ability-card__sprite");
                    sprite.AddToClassList(iconCls);
                    box.Add(sprite);
                }
                else
                {
                    var icon = new Label(ab.ResolveIcon());
                    icon.AddToClassList("cw-ability-card__icon");
                    var ef = UiGfx.EmojiFont();
                    if (ef != null) icon.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
                    icon.style.color = ab.AccentColor;
                    box.Add(icon);
                }

                string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
                var name = new Label(label.ToUpperInvariant());
                name.AddToClassList("cw-ability-card__name");
                box.Add(name);

                // Numbered badge = the in-match button that fires it. Always accurate now
                // — nothing compacts, so a slot's index never silently changes under it.
                var badge = new Label((i + 1).ToString());
                badge.AddToClassList("cw-ability-badge");
                badge.style.backgroundColor = ab.AccentColor;
                badge.style.color = InkOn(ab.AccentColor);
                box.Add(badge);
            }
        }

        // ======================================================================
        //  LOADOUT PICK ROWS
        // ----------------------------------------------------------------------
        //  Two flat rows, both fully visible, selection direct:
        //
        //      COMMON  — any chicken can take these (pool of 3)
        //      CLASS   — legal only for the selected class (pool of 3)
        //
        //  Design override of GDD §7.1-7.2 (explained once, reaffirmed by the
        //  user — not re-litigated here): Common is OPTIONAL, not a mandatory
        //  1st slot. Ability0/Ability1/Ability2/Ability3 are purely positional
        //  bookkeeping for the four active-ability slots. Any legal ability
        //  from EITHER row can land in ANY slot, freely mixed — 0 Common + 4
        //  Character, 3 Common + 1 Character (bounded by the pool sizes), or
        //  any mix in between are all equally valid. There is no per-category
        //  minimum; only the total count (== 4) gates READY.
        //
        //  The two rows stay as a discoverability grouping — legality/category
        //  is still visually separated — but which physical slot a tap lands in
        //  is governed entirely by the slot-arming state machine above, not by
        //  which row the card came from.
        // ======================================================================

        /// <summary>Rough hint for the CLASS pool's counter text: how many Character picks a
        /// loadout typically has room for once Peck has taken its slot. Not a rule and not a
        /// per-row minimum — any legal ability can fill any open slot (see ActiveSlotsForClass).</summary>
        private int ClassPicksAllowed => Mathf.Max(1, ActiveSlotsForClass - (ClassCanForage ? 2 : 1));

        private void RebuildPickRows(ChickenClass cls)
        {
            var all = _abilityRegistry?.All;
            if (all == null)
            {
                FillMissingRegistryNote(_commonCards);
                FillMissingRegistryNote(_classCards);
                return;
            }

            var pool = all.Where(a => a != null && !(a is PassiveAbilitySO)).ToList();
            var flag = AbilityRegistrySO.FlagOf(cls);

            // BOTH rows filter on AllowedClasses. The Common row used not to, on the
            // assumption that "Common" means "legal for everyone" — true until Peck shipped
            // as a Common restricted to the three foraging classes. Without this filter an
            // Assassin could equip Peck, reach READY at 4/4, and have MatchBootstrapper
            // silently strip it and backfill something else at spawn: a picker that lied.
            var common    = pool.Where(a => a.SlotKind == AbilitySlotKind.Common
                                            && (a.AllowedClasses & flag) != 0).ToList();
            var character = pool.Where(a => a.SlotKind == AbilitySlotKind.Character
                                            && (a.AllowedClasses & flag) != 0).ToList();

            FillRow(_commonCards, common);
            FillRow(_classCards,  character);

            if (_commonHint != null) _commonHint.text = "optional · any chicken can take these";
            if (_classHint != null)
            {
                // "optional · " dropped from the COMBO variant — it already runs
                // right up against the row width budget (measured live: 927px of
                // content in a 902px head with "1 EQUIPPED" showing) and clipped.
                // The non-combo variant has room to spare.
                string clsName = Meta[cls].Name.Replace(" CHICKEN", string.Empty);
                _classHint.text = ClassPicksAllowed > 1
                    ? $"{clsName} only · COMBO lets you take two"
                    : $"optional · {clsName} only";
            }
        }

        private void FillRow(VisualElement host, List<AbilityBaseSO> abilities)
        {
            if (host == null) return;
            host.Clear();
            if (abilities.Count == 0)
            {
                FillMissingRegistryNote(host);
                return;
            }
            foreach (var ab in abilities) host.Add(MakeAbilityCard(ab));
        }

        private static void FillMissingRegistryNote(VisualElement host)
        {
            if (host == null) return;
            host.Clear();
            var note = new Label("No abilities in registry.\nAssign AbilityRegistrySO in ProjectInstaller.");
            note.AddToClassList("cw-body");
            host.Add(note);
        }

        private VisualElement MakeAbilityCard(AbilityBaseSO ab)
        {
            var card = new VisualElement();
            card.AddToClassList("cw-ability-card");

            // Exported design icon sprite; fall back to the emoji glyph only for an
            // ability with no sprite mapping (shouldn't happen for shipped abilities).
            string iconCls = AbilityIconStyle.ClassFor(ab);
            if (!string.IsNullOrEmpty(iconCls))
            {
                var s = new VisualElement();
                s.AddToClassList("cw-ability-card__sprite");
                s.AddToClassList(iconCls);
                card.Add(s);
            }
            else
            {
                var lbl = new Label(ab.ResolveIcon());
                lbl.AddToClassList("cw-ability-card__icon");
                var ef = UiGfx.EmojiFont();
                if (ef != null) lbl.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
                lbl.style.color = ab.AccentColor;
                card.Add(lbl);
            }

            string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
            var name = new Label(label.ToUpperInvariant());
            name.AddToClassList("cw-ability-card__name");
            card.Add(name);

            // The description does NOT live on the card — see #AbilityDetail below.
            // Six cards each carrying their own wrapped description needed ~137px of
            // a 263px card and left nothing for the icon, which flexbox then
            // collapsed to zero height. One shared strip shows the tapped ability's
            // text once, at the full 34px reading size, instead of six times at 24.

            // Category + cooldown share ONE footer row. They used to be two stacked
            // full-width rows; at ~43px each that cost 86px of a 263px card, which
            // is what squeezed the icon to zero once descriptions arrived. Side by
            // side they cost 43px and both stay as text, so the category is not
            // reduced to a colour the way a bare accent stripe would.
            var footer = new VisualElement();
            footer.AddToClassList("cw-card-footer");

            if (Cats.TryGetValue(ab.Category, out var cat))
            {
                var tag = new Label(cat.Label);
                tag.AddToClassList("cw-cat-tag");
                tag.style.backgroundColor = Fade(cat.Color, 0.85f);
                tag.style.color = InkOn(cat.Color);
                footer.Add(tag);
            }

            bool shortCd = ab.Cooldown <= 6f;
            var badge = new Label(shortCd ? "SHORT" : "MED");
            badge.AddToClassList("cw-cd-badge");
            badge.AddToClassList(shortCd ? "cw-cd-badge--short" : "cw-cd-badge--med");
            footer.Add(badge);
            card.Add(footer);

            int slot = SlotOf(ab);
            if (slot >= 0)
            {
                SetBorder(card, ab.AccentColor);
                card.style.backgroundColor = Fade(ab.AccentColor, 0.20f);
                card.AddToClassList("cw-ability-card--picked");

                // Numbered badge = the in-match button that fires it, matching the
                // touch HUD hex badges. Also the non-color half of the selected
                // state, so the pick reads without relying on the accent border.
                var slotBadge = new Label((slot + 1).ToString());
                slotBadge.AddToClassList("cw-ability-badge");
                slotBadge.style.backgroundColor = ab.AccentColor;
                slotBadge.style.color = InkOn(ab.AccentColor);
                card.Add(slotBadge);
            }

            // One tap does both jobs: equips/unequips/swaps into the armed slot AND
            // reveals what the ability does in the detail strip. Deliberately NOT a
            // long-press — that has no affordance on a touch screen, and a player who
            // never discovers the gesture is back to "equip it and find out in a match".
            card.RegisterCallback<ClickEvent>(_ => OnAbilityCardTapped(ab));
            return card;
        }

        /// <summary>The Peck (foraging) ability asset, or null if the registry has none.</summary>
        private AbilityBaseSO PeckAbility =>
            _abilityRegistry?.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);

        /// <summary>True when <paramref name="cls"/> may forage. Read off Peck's own
        /// AllowedClasses rather than naming classes here, so the picker, the spec-pill
        /// signature line, and MatchBootstrapper's sanitiser can never disagree about who
        /// gets a mandatory Peck.</summary>
        private bool CanForage(ChickenClass cls)
        {
            var peck = PeckAbility;
            return peck != null && AbilityRegistrySO.IsAllowedFor(peck, cls);
        }

        /// <summary>True when the currently selected class may forage.</summary>
        private bool ClassCanForage => CanForage(Cls);

        /// <summary>The class's default (signature) specialization, or null if the registry has
        /// none for it. Used to preview a not-yet-selected class chip's PreFilledTag.</summary>
        private PassiveAbilitySO DefaultPassiveFor(ChickenClass cls) => _abilityRegistry?.GetDefaultPassiveForClass(cls);

        /// <summary>
        /// The display name of whichever ability <paramref name="passive"/> force-equips for
        /// <paramref name="cls"/> — its own <see cref="PassiveAbilitySO.SignatureAbility"/> when
        /// it has one legal for the class, else Peck for a class that can forage, else null when
        /// nothing is forced. Shared by the spec-pill "Starts with X equipped" line and
        /// PreFilledTag so the two can never disagree with each other or with
        /// <see cref="SeedForcedAbilities"/>.
        /// </summary>
        private string ForcedAbilityLabel(ChickenClass cls, PassiveAbilitySO passive)
        {
            if (passive == null) return null;

            var signature = passive.SignatureAbility;
            if (signature != null && AbilityRegistrySO.IsAllowedFor(signature, cls))
                return signature.DisplayName;

            if (!CanForage(cls)) return null;
            return PeckAbility?.DisplayName ?? "Peck";
        }

        private AbilityBaseSO GetEquipped(int slot) => slot switch
        {
            0 => _selection?.Ability0,
            1 => _selection?.Ability1,
            2 => _selection?.Ability2,
            3 => _selection?.Ability3,
            _ => null,
        };

        private void SetEquipped(int slot, AbilityBaseSO ab)
        {
            if (_selection == null) return;
            if (slot == 0) _selection.Ability0 = ab;
            else if (slot == 1) _selection.Ability1 = ab;
            else if (slot == 2) _selection.Ability2 = ab;
            else if (slot == 3) _selection.Ability3 = ab;
        }

        private int SlotOf(AbilityBaseSO ab)
        {
            if (ab == null) return -1;
            for (int i = 0; i < ActiveSlotsForClass; i++) if (GetEquipped(i) == ab) return i;
            return -1;
        }

        /// <summary>
        /// Ensures both of the game's forced abilities are equipped exactly where
        /// <c>MatchBootstrapper.ResolveLegalLoadout</c> would force them: the mandatory Peck for
        /// a foraging class, and the current specialization's <see cref="PassiveAbilitySO.SignatureAbility"/>
        /// if it has one legal for this class. So what the player composes is what actually
        /// spawns — without this the player could compose four abilities, hit READY, and have
        /// the spawner silently swap one out, a UI that lied.
        /// </summary>
        /// <remarks>
        /// <b>Deterministic slots, not "the next open slot".</b> Peck (when forced) always lands
        /// in slot 0, and a non-Peck-variant signature (when both apply) in slot 1 — mirroring
        /// ResolveLegalLoadout's own forced-insert order, where Peck is inserted after the
        /// signature and pushes it along. Determinism is what lets the spec-pill's
        /// "Starts with X equipped" line and PreFilledTag name a specific slot truthfully.
        /// <para>
        /// A signature that IS itself a Peck variant replaces the plain Peck rather than joining
        /// it, so a forager doesn't burn two of four slots on forced picks for one mechanic (see
        /// <see cref="PassiveAbilitySO.SignatureAbility"/>) — the stray plain Peck is released in
        /// that case, and also when the class cannot forage at all.
        /// </para>
        /// <para>
        /// Only forces an ability that is not <i>already</i> equipped somewhere. No special-case
        /// movement logic is needed once placed — the generic swap in
        /// <see cref="OnAbilityCardTapped"/> covers moving a forced ability the player later
        /// drags elsewhere, and this must not snap it back.
        /// </para>
        /// </remarks>
        private void SeedForcedAbilities()
        {
            if (_selection == null) return;

            var peck = PeckAbility;
            var passive = _selection.Passive;
            var signature = passive?.SignatureAbility;
            // Illegal-for-this-class signatures are pinned out by DataIntegrityTests at the
            // asset level, but a picker that trusted that blindly would silently force an
            // ability the sanitiser would reject.
            if (signature != null && !AbilityRegistrySO.IsAllowedFor(signature, Cls))
                signature = null;

            bool signatureIsPeckVariant = signature is PeckAbilitySO;
            bool wantPeck = ClassCanForage && !signatureIsPeckVariant && peck != null;

            // Release a plain Peck that no longer belongs — class can't forage, or the
            // signature itself stands in for it.
            if (!wantPeck && peck != null && peck != signature)
            {
                int at = SlotOf(peck);
                if (at >= 0) SetEquipped(at, null);
            }

            if (wantPeck)
            {
                if (SlotOf(peck) < 0) SetEquipped(0, peck); // no room: Peck outranks whatever was there
                if (signature != null && SlotOf(signature) < 0)
                    SetEquipped(GetEquipped(0) == peck ? 1 : 0, signature); // no room: the signature outranks whatever was there
            }
            else if (signature != null && SlotOf(signature) < 0)
            {
                SetEquipped(0, signature); // no room: the signature outranks whatever was there
            }
        }

        /// <summary>
        /// READY gates on total distinct equipped abilities == 4 + a Passive —
        /// there is no per-category (Common vs Class) minimum any more. The two
        /// row counters are read-outs of "how many of this row's pool are
        /// currently equipped", not a "/1" or "/N" requirement — Common is
        /// entirely optional, per the design override of GDD §7.1-7.2.
        /// </summary>
        private void RefreshEquippedState()
        {
            int n = ActiveSlotsForClass;
            int totalPicked = 0, commonPicked = 0, classPicked = 0;
            for (int i = 0; i < n; i++)
            {
                var eq = GetEquipped(i);
                if (eq == null) continue;
                totalPicked++;
                if (eq.SlotKind == AbilitySlotKind.Common) commonPicked++;
                else classPicked++;
            }

            // No implied floor: "0 equipped" reads as "optional, none taken yet",
            // not as a missing requirement.
            if (_commonCount != null) _commonCount.text = $"{commonPicked} EQUIPPED";
            if (_classCount  != null) _classCount.text  = $"{classPicked} EQUIPPED";

            int missing = Mathf.Max(0, n - totalPicked);
            bool ready  = missing == 0 && _selection?.Passive != null;

            if (_readyBtn != null)
            {
                _readyBtn.text = ready
                    ? "READY ▶"
                    : (missing == 1 ? "PICK 1 MORE ABILITY" : $"PICK {missing} MORE ABILITIES");
                _readyBtn.SetEnabled(ready);
                _readyBtn.EnableInClassList("cw-btn--green", ready);
                _readyBtn.EnableInClassList("cw-btn--neutral", !ready);
            }
        }

        private void OnReady()
        {
            if (_isBusy) return;
            ShowLobby();
        }

        // ======================================================================
        //  LOBBY
        // ======================================================================
        // Okabe-Ito player colors (design cluckwars-tokens-v3 CW_PLAYERS_V3).
        private static readonly Color[] PlayerColors =
        {
            UiGfx.Hex32("E8751A"), UiGfx.Hex32("1A7FC4"),
            UiGfx.Hex32("C4286F"), UiGfx.Hex32("0D9E7A"),
        };
        // Sample CPU bots shown in the Solo lobby (you always spawn vs 3 bots).
        private static readonly ChickenClass[] BotClasses = { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };
        private static readonly string[]       BotNames   = { "DashFox", "BrunoB", "PeckNoir" };

        private string _joinCode = string.Empty;

        private void BuildLobby()
        {
            Bind<Button>(_lobby, "BackBtn",  b => b.clicked += ShowLoadout);
            Bind<Button>(_lobby, "StartBtn", b => b.clicked += OnStartMatch);
            Bind<Button>(_lobby, "CopyBtn",  b => b.clicked += CopyJoinCode);
            Bind<Button>(_lobby, "ShareBtn", b => b.clicked += CopyJoinCode);
        }

        private async void RefreshLobby()
        {
            if (_selection == null) return;
            var mode = _selection.Mode;
            bool isHost = mode == SessionMode.Host;
            bool isJoin = mode == SessionMode.Join;
            bool isSolo = !isHost && !isJoin;

            var inviteCard = _lobby.Q<VisualElement>("InviteCard");
            var joinCard   = _lobby.Q<VisualElement>("JoinCard");
            var status     = _lobby.Q<Label>("LobbyStatus");
            if (inviteCard != null) inviteCard.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
            if (joinCard   != null) joinCard.style.display   = isJoin ? DisplayStyle.Flex : DisplayStyle.None;
            if (status != null) status.text = string.Empty;

            BuildPlayerGrid(isSolo, isHost);
            UpdateLobbyStatus(isSolo, isHost, isJoin);
            RefreshMatchSettings();

            // Host pre-creates the UGS lobby so its join code populates the tiles before START.
            if (isHost)
            {
                _joinCode = string.Empty;
                SetCodeTiles("·····");
                try
                {
                    var info = await _ugs.CreateLobbyAsync("CluckWars Match", 4);
                    _joinCode = info.JoinCode;
                    _selection.SessionName = info.JoinCode;
                    SetCodeTiles(info.JoinCode);
                }
                catch (Exception e)
                {
                    _log?.Error(Source, $"CreateLobby failed: {e.Message}");
                    if (status != null) { status.style.color = UiGfx.Hex32("ff786e"); status.text = "Could not create lobby."; }
                }
            }
        }

        /// <summary>
        /// Paints the TIME / GOAL rows of the MATCH SETTINGS card from the live
        /// <see cref="MatchConfigSO"/>, so the lobby advertises the rules the match will
        /// actually enforce. (ARENA and MODE stay authored: there is no map registry and
        /// FFA is the only mode, so there is nothing to bind them to.)
        /// </summary>
        private void RefreshMatchSettings()
        {
            if (_matchConfig == null)
            {
                // Only reachable when nothing installed the project container (EditMode /
                // headless). Leave the authored UXML text — an EditMode test pins it to
                // these same values, so it is right rather than merely stale.
                _log?.Debug(Source, "MatchConfig not injected; lobby settings keep their authored text.");
                return;
            }

            var time = _lobby.Q<Label>("SetTime");
            var goal = _lobby.Q<Label>("SetGoal");
            if (time != null) time.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds);
            if (goal != null) goal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin);
        }

        private void UpdateLobbyStatus(bool isSolo, bool isHost, bool isJoin)
        {
            var dot   = _lobby.Q<VisualElement>("StatusDot");
            var count = _lobby.Q<Label>("StatusCount");
            var text  = _lobby.Q<Label>("StatusText");
            var start = _lobby.Q<Button>("StartBtn");

            string c, t; Color dotColor; bool readyish;
            if (isSolo)      { c = "4/4"; t = "Solo · 3 CPU"; dotColor = UiGfx.Hex32("4ae66a"); readyish = true;  }
            else if (isHost) { c = "1/4"; t = "Waiting…";     dotColor = UiGfx.Gold;             readyish = false; }
            else             { c = "—";   t = "Enter code";   dotColor = UiGfx.Gold;             readyish = false; }

            if (count != null) count.text = c;
            if (text  != null) { text.text = t; text.style.color = readyish ? UiGfx.Hex32("7cd99a") : UiGfx.Gold; }
            if (dot   != null) dot.style.backgroundColor = dotColor;
            if (start != null) start.text = isJoin ? "JOIN MATCH ▶" : "START MATCH ▶";
        }

        private void SetCodeTiles(string code)
        {
            var tiles = _lobby.Q<VisualElement>("CodeTiles");
            if (tiles == null) return;
            tiles.Clear();
            if (string.IsNullOrEmpty(code)) return;
            foreach (var ch in code.ToUpperInvariant())
            {
                var t = new Label(ch.ToString());
                t.AddToClassList("cw-code-tile");
                tiles.Add(t);
            }
        }

        private void CopyJoinCode()
        {
            if (string.IsNullOrEmpty(_joinCode)) return;
            GUIUtility.systemCopyBuffer = _joinCode;
            var status = _lobby.Q<Label>("LobbyStatus");
            if (status != null) { status.style.color = UiGfx.Gold; status.text = $"Copied {_joinCode}."; }
        }

        // ---- Player grid (2×2) ------------------------------------------------
        private void BuildPlayerGrid(bool isSolo, bool isHost)
        {
            var grid = _lobby.Q<VisualElement>("PlayerGrid");
            if (grid == null) return;
            grid.Clear();

            // Slot 0 — you.
            var mine = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++) { var a = GetEquipped(i); if (a != null) mine.Add(a); }
            grid.Add(MakeLobbyCard(0, Cls, "You", isHost, false, ready: true, abilities: mine, empty: false, slots: ActiveSlotsForClass));

            // Slots 1-3 — solo fills CPU bots; host/join show open seats.
            for (int i = 1; i < 4; i++)
            {
                if (isSolo)
                {
                    var bc = BotClasses[i - 1];
                    grid.Add(MakeLobbyCard(i, bc, BotNames[i - 1], false, true, ready: true, abilities: SampleBotAbilities(bc, i), empty: false, slots: ActiveSlotsForClass));
                }
                else
                {
                    grid.Add(MakeLobbyCard(i, ChickenClass.Warrior, null, false, false, ready: false, abilities: null, empty: true, slots: 2));
                }
            }
        }

        /// <summary>
        /// Cosmetic loadout preview for a lobby-only CPU card. NOT what the bot actually spawns
        /// with — that is a randomly-rolled preset resolved by
        /// <c>MatchBootstrapper.ResolveLegalLoadout</c> once <c>Game.unity</c> loads, and this
        /// screen (<c>Bootstrap.unity</c>) has no access to that component or its scene-baked
        /// presets to preview exactly. What it must still get right is the shape of a real
        /// loadout: <see cref="ActiveSlotsForClass"/> abilities, every one legal for
        /// <paramref name="cls"/>, Peck present iff the class can forage.
        /// </summary>
        /// <remarks>
        /// Previously indexed into <c>_abilityRegistry.All</c> unfiltered by class and capped
        /// at a hardcoded 2 — so a bot preview could show another class's ability, and every
        /// bot card read as carrying half the abilities the human player's card showed for the
        /// same four slots. Verified live 2026-08-23: a solo lobby with Warrior (4 icons) next
        /// to Speedy/Fatty/Assassin bots (2 icons each).
        /// </remarks>
        private List<AbilityBaseSO> SampleBotAbilities(ChickenClass cls, int seed)
        {
            var list = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (_abilityRegistry == null) return list;

            var peck = PeckAbility;
            if (peck != null && AbilityRegistrySO.IsAllowedFor(peck, cls))
                list.Add(peck);

            // Class pool first, Common as backfill — mirrors ResolveLegalLoadout's own
            // priority order (a real loadout is never short on Character slots while Common
            // sits unused). Rotated by seed so DashFox/BrunoB/PeckNoir don't all show the
            // same three abilities from the front of each list.
            var pool = _abilityRegistry.GetCharacterAbilitiesForClass(cls)
                .Concat(_abilityRegistry.CommonAbilities.Where(a => a != peck))
                .ToList();

            for (int i = 0; i < pool.Count && list.Count < ActiveSlotsForClass; i++)
            {
                var a = pool[(seed + i) % pool.Count];
                if (!list.Contains(a)) list.Add(a);
            }
            return list;
        }

        private VisualElement MakeLobbyCard(int idx, ChickenClass cls, string name, bool isHost, bool cpu, bool ready, List<AbilityBaseSO> abilities, bool empty, int slots)
        {
            var color = PlayerColors[Mathf.Clamp(idx, 0, 3)];

            if (empty)
            {
                var ec = new VisualElement();
                ec.AddToClassList("cw-player-card");
                ec.AddToClassList("cw-player-card--empty");
                var lbl = new Label($"WAITING FOR P{idx + 1}");
                lbl.AddToClassList("cw-player-empty-label");
                ec.Add(lbl);
                return ec;
            }

            var card = new VisualElement();
            card.AddToClassList("cw-player-card");
            SetBorder(card, Fade(color, ready ? 0.85f : 0.45f));
            card.style.unityBackgroundImageTintColor = Fade(color, ready ? 0.18f : 0.07f);

            var accent = new VisualElement();
            accent.AddToClassList("cw-player-accent");
            accent.style.backgroundColor = color;
            card.Add(accent);

            var art = new VisualElement();
            art.AddToClassList("cw-player-art");
            art.AddToClassList("cw-chicken--" + KeyOf(cls));
            card.Add(art);

            var mid = new VisualElement();
            mid.AddToClassList("cw-player-mid");

            var nameRow = new VisualElement();
            nameRow.AddToClassList("cw-player-namerow");
            var nameLbl = new Label(name);
            nameLbl.AddToClassList("cw-player-name");
            var pn = new Label($"P{idx + 1}");
            pn.AddToClassList("cw-player-pn");
            pn.style.color = color;
            nameRow.Add(nameLbl); nameRow.Add(pn);
            if (isHost)
            {
                var host = new Label("HOST");
                host.AddToClassList("cw-player-host");
                nameRow.Add(host);
            }
            else if (cpu)
            {
                var tag = new Label("CPU");
                tag.AddToClassList("cw-player-host");
                tag.style.backgroundColor = color;
                tag.style.color = InkOn(color);
                nameRow.Add(tag);
            }
            mid.Add(nameRow);

            var m = Meta[cls];
            var clsLine = new Label($"{m.Name.Replace(" CHICKEN", string.Empty)} · {m.Role}");
            clsLine.AddToClassList("cw-player-class");
            mid.Add(clsLine);

            var chips = new VisualElement();
            chips.AddToClassList("cw-player-chips");
            for (int i = 0; i < slots; i++)
            {
                AbilityBaseSO ab = (abilities != null && i < abilities.Count) ? abilities[i] : null;
                chips.Add(MakeMiniHex(ab));
            }
            mid.Add(chips);
            card.Add(mid);

            var state = new VisualElement();
            state.AddToClassList("cw-player-state");
            state.AddToClassList(ready ? "cw-player-state--ready" : "cw-player-state--picking");
            var sl = new Label(ready ? "✓ READY" : "PICKING");
            sl.AddToClassList("cw-player-state__label");
            if (!ready) sl.style.color = UiGfx.Hex32("c4a060");
            state.Add(sl);
            card.Add(state);

            return card;
        }

        private VisualElement MakeMiniHex(AbilityBaseSO ab)
        {
            var hex = new VisualElement();
            hex.AddToClassList("cw-mini-hex");
            if (ab == null) { hex.style.unityBackgroundImageTintColor = HexEmptyTint; return hex; }
            hex.style.unityBackgroundImageTintColor = ab.AccentColor;

            string iconCls = AbilityIconStyle.ClassFor(ab);
            if (!string.IsNullOrEmpty(iconCls))
            {
                var s = new VisualElement();
                s.AddToClassList("cw-mini-hex__sprite");
                s.AddToClassList(iconCls);
                hex.Add(s);
            }
            else
            {
                var icon = new Label(ab.ResolveIcon());
                icon.AddToClassList("cw-mini-hex__icon");
                var ef = UiGfx.EmojiFont();
                if (ef != null) icon.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
                icon.style.color = UiGfx.TextPrimary;
                hex.Add(icon);
            }
            return hex;
        }

        private async void OnStartMatch()
        {
            if (_isBusy || _selection == null) return;
            var status = _lobby.Q<Label>("LobbyStatus");

            switch (_selection.Mode)
            {
                case SessionMode.Solo:
                case SessionMode.Host:
                    _sceneLoader?.LoadNext();
                    break;

                case SessionMode.Join:
                    var field = _lobby.Q<TextField>("LobbyJoinField");
                    var code = field?.value?.Trim();
                    if (string.IsNullOrEmpty(code)) { if (status != null) status.text = "Enter a join code first."; return; }
                    _isBusy = true;
                    if (status != null) status.text = "Joining…";
                    try
                    {
                        var info = await _ugs.JoinLobbyByCodeAsync(code);
                        _selection.SessionName = info.JoinCode;
                        _sceneLoader?.LoadNext();
                    }
                    catch (Exception e)
                    {
                        _log?.Error(Source, $"JoinByCode failed: {e.Message}");
                        if (status != null) status.text = "Could not join that code.";
                    }
                    _isBusy = false;
                    break;
            }
        }

        // ======================================================================
        //  Helpers
        // ======================================================================
        private static string KeyOf(ChickenClass cls) => cls.ToString().ToLowerInvariant();

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Blend a colour <paramref name="t"/> of the way toward white, for use as text on the dark screen bg.</summary>
        private static Color Lighten(Color c, float t) =>
            new Color(Mathf.Lerp(c.r, 1f, t), Mathf.Lerp(c.g, 1f, t), Mathf.Lerp(c.b, 1f, t), 1f);

        /// <summary>WCAG 2.1 relative luminance of an sRGB colour (alpha ignored).</summary>
        private static float Luminance(Color c)
        {
            static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        /// <summary>
        /// Ink colour to draw on top of <paramref name="bg"/> — whichever of the cream and
        /// dark-brown text colours actually contrasts with it.
        /// <para>
        /// Required because ability accent colours span the whole luminance range: Egg Shell
        /// is <c>(0.92, 0.94, 1.00)</c>, i.e. near-white, so the fixed cream label made its
        /// numbered slot badge unreadable, while Root Egg's dark brown needs the cream.
        /// Anything that fills an element with an authored accent and then writes text on it
        /// must go through this rather than assuming a fixed ink.
        /// </para>
        /// </summary>
        private static Color InkOn(Color bg)
        {
            float bgL = Luminance(bg);
            float Ratio(Color fg)
            {
                float a = Luminance(fg), b = bgL;
                if (a < b) (a, b) = (b, a);
                return (a + 0.05f) / (b + 0.05f);
            }
            return Ratio(UiGfx.TextPrimary) >= Ratio(UiGfx.TextDark) ? UiGfx.TextPrimary : UiGfx.TextDark;
        }

        private static void SetBorder(VisualElement ve, Color c)
        {
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
        }

        private static void Bind<T>(VisualElement root, string name, Action<T> act) where T : VisualElement
        {
            var e = root?.Q<T>(name);
            if (e != null) act(e);
        }
    }
}
