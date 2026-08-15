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
    /// UI Toolkit menu front-end: Main Menu → Character Select → Lobby → Game.
    /// Replaces the procedural-UGUI CharacterSelectController. Visuals live in
    /// Assets/UI/*.uxml + Assets/UI/Styles/CluckWarsTheme.uss; this controller
    /// only binds data and drives navigation. All selections are written back to
    /// <see cref="ISessionSelectionService"/> exactly as the old controller did,
    /// so the spawner (Game scene) keeps working unchanged.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuUiController : MonoBehaviour
    {
        private const string Source = "MenuUI";

        [Header("Page templates (assigned in scene)")]
        [SerializeField] private VisualTreeAsset _mainMenuUxml;
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
        private VisualElement _mainMenu, _charSelect, _lobby;
        private bool _isBusy;

        private readonly Dictionary<ChickenClass, VisualElement> _classChips = new();
        private VisualElement _previewChicken, _previewGlow, _previewDisc;
        // The two flat pick rows that replaced the slot-hex row + scrolling grid.
        private VisualElement _commonCards, _classCards;
        private Label _commonHint, _commonCount, _classHint, _classCount;
        // Shared "what does this do" strip — the description's only home. Cards
        // carry icon + name + category/CD; the last-tapped ability's text lands
        // here at full reading size. Null until a card is tapped.
        private VisualElement _abilityDetail;
        private Label _abilityDetailName, _abilityDetailText;
        private AbilityBaseSO _focusedAbility;
        private Label _previewName, _previewQuote, _previewDesc;
        private Button _readyBtn;
        // .cw-chicken--<class> currently on the big preview figure (for swap).
        private string _previewChickenClass;

        // Dim neutral tint for an empty ability hex (no equipped accent).
        private static readonly Color HexEmptyTint = new Color(0.45f, 0.38f, 0.28f, 0.7f);

        private static readonly ChickenClass[] Order =
            { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        private sealed class ClassMeta
        {
            public string Name, Role, PassiveName, PassiveDesc;
            public Color Tint;
            public int[] Stats; // cargo, rate, speed (1..5) — HP/Resist removed in v0.4 (no health)
        }

        private static readonly Dictionary<ChickenClass, ClassMeta> Meta = new()
        {
            [ChickenClass.Warrior]  = new ClassMeta { Name = "WARRIOR CHICKEN",  Role = "All-Rounder",  Tint = UiGfx.Hex32("C04030"), PassiveName = "MIGHTY",     PassiveDesc = "+25% outgoing ability damage.",  Stats = new[]{3,3,3} },
            [ChickenClass.Speedy]   = new ClassMeta { Name = "SPEEDY CHICKEN",   Role = "Hit & Run",    Tint = UiGfx.Hex32("E85A2A"), PassiveName = "SLIPPERY",  PassiveDesc = "Reduced control-effect duration.", Stats = new[]{2,3,5} },
            [ChickenClass.Fatty]    = new ClassMeta { Name = "FATTY CHICKEN",    Role = "Bulk Carrier", Tint = UiGfx.Hex32("F5D75A"), PassiveName = "IMMOVABLE", PassiveDesc = "Greatly reduced knockback.",       Stats = new[]{5,5,2} },
            [ChickenClass.Assassin] = new ClassMeta { Name = "ASSASSIN CHICKEN", Role = "Disruptor",    Tint = UiGfx.Hex32("7B68EE"), PassiveName = "COMBO",     PassiveDesc = "Equips 3 abilities instead of 2.", Stats = new[]{2,2,4} },
        };

        private static readonly string[] StatRowNames = { "StatCargo", "StatRate", "StatSpeed" };
        private static readonly string[] ChipNames    = { "ClassWarrior", "ClassSpeedy", "ClassFatty", "ClassAssassin" };

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

            _mainMenu   = ClonePage(_mainMenuUxml);
            _charSelect = ClonePage(_characterSelectUxml);
            _lobby      = ClonePage(_lobbyUxml);

            BuildMainMenu();
            BuildCharacterSelect();
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

        // ---- Navigation -------------------------------------------------------
        private void ShowMainMenu()        { SetPage(_mainMenu); }
        private void ShowCharacterSelect() { SetPage(_charSelect); RefreshCharacterSelect(); }
        private void ShowLobby()           { SetPage(_lobby); RefreshLobby(); }

        private void SetPage(VisualElement page)
        {
            if (_mainMenu   != null) _mainMenu.style.display   = page == _mainMenu   ? DisplayStyle.Flex : DisplayStyle.None;
            if (_charSelect != null) _charSelect.style.display = page == _charSelect ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lobby      != null) _lobby.style.display      = page == _lobby      ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ======================================================================
        //  MAIN MENU
        // ======================================================================
        private void BuildMainMenu()
        {
            Bind<Button>(_mainMenu, "SoloBtn", b => b.clicked += () => ChooseMode(SessionMode.Solo));
            Bind<Button>(_mainMenu, "HostBtn", b => b.clicked += () => ChooseMode(SessionMode.Host));
            Bind<Button>(_mainMenu, "JoinBtn", b => b.clicked += () => ChooseMode(SessionMode.Join));

            // Build stamp — a tester reporting a bug from a device otherwise has no
            // way to say which build produced it.
            Bind<Label>(_mainMenu, "BuildStamp", l => l.text = $"v{Application.version}");
        }

        private void ChooseMode(SessionMode mode)
        {
            if (_selection != null) _selection.Mode = mode;
            ShowCharacterSelect();
        }

        // ======================================================================
        //  CHARACTER SELECT
        // ======================================================================
        private Button _passiveOpt1, _passiveOpt2;

        private void BuildCharacterSelect()
        {
            _classChips.Clear();
            for (int i = 0; i < Order.Length; i++)
            {
                var cls = Order[i];
                var chip = _charSelect.Q<VisualElement>(ChipNames[i]);
                if (chip == null) continue;
                _classChips[cls] = chip;
                chip.RegisterCallback<ClickEvent>(_ => SelectClass(cls));

                // Class chip art — exported design chicken sprite (Stage-3). Chips
                // are fixed per class, so the modifier is applied once here.
                var art = chip.Q<VisualElement>(ChipNames[i] + "Art");
                if (art != null) art.AddToClassList("cw-chicken--" + KeyOf(cls));
            }

            _previewChicken = _charSelect.Q<VisualElement>("PreviewChicken");
            _previewGlow    = _charSelect.Q<VisualElement>("PreviewGlow");
            _previewDisc    = _charSelect.Q<VisualElement>("PreviewDisc");
            _previewName    = _charSelect.Q<Label>("PreviewName");
            _previewQuote   = _charSelect.Q<Label>("PreviewQuote");
            _previewDesc    = _charSelect.Q<Label>("PreviewDesc");
            _passiveOpt1    = _charSelect.Q<Button>("PassiveOpt1");
            _passiveOpt2    = _charSelect.Q<Button>("PassiveOpt2");
            _commonCards    = _charSelect.Q<VisualElement>("CommonCards");
            _classCards     = _charSelect.Q<VisualElement>("ClassCards");
            _commonHint     = _charSelect.Q<Label>("CommonHint");
            _commonCount    = _charSelect.Q<Label>("CommonCount");
            _classHint      = _charSelect.Q<Label>("ClassHint");
            _classCount     = _charSelect.Q<Label>("ClassCount");
            _abilityDetail     = _charSelect.Q<VisualElement>("AbilityDetail");
            _abilityDetailName = _charSelect.Q<Label>("AbilityDetailName");
            _abilityDetailText = _charSelect.Q<Label>("AbilityDetailText");
            _readyBtn       = _charSelect.Q<Button>("ReadyBtn");
            if (_readyBtn != null) _readyBtn.clicked += OnReady;

            var ef = UiGfx.EmojiFont();
            if (ef != null)
            {
                var emojis = _charSelect.Query<Label>(className: "cw-emoji-text").ToList();
                foreach (var e in emojis) e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
            }

            Bind<Button>(_charSelect, "HomeBtn", b => b.clicked += ShowMainMenu);
            BindRangeGuidesToggle();
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
        /// Bound once in <see cref="BuildCharacterSelect"/>, not per refresh — the control is
        /// static markup, and a second RegisterValueChangedCallback on the same Toggle would
        /// run the setter twice per click.
        /// </remarks>
        private void BindRangeGuidesToggle()
        {
            Bind<Toggle>(_charSelect, "RangeGuidesToggle", t =>
            {
                t.SetValueWithoutNotify(PlayerPreferences.AbilityRangeGuidesEnabled);
                t.RegisterValueChangedCallback(evt =>
                {
                    PlayerPreferences.AbilityRangeGuidesEnabled = evt.newValue;
                    _log?.Info(Source, $"Ability range guides {(evt.newValue ? "enabled" : "disabled")}.");
                });
            });
        }

        private void SelectClass(ChickenClass cls)
        {
            if (_selection == null) return;
            _selection.SelectedClass = cls;
            // The focused ability may be Character-slot and off-pool for the new
            // class, so the strip would keep explaining something no longer on
            // screen. Reset to the prompt.
            _focusedAbility = null;
            _selection.Ability0 = null;
            _selection.Ability1 = null;
            _selection.Ability2 = null;
            _selection.Ability3 = null;

            // GetDefaultPassiveForClass, not passives[0] — the latter is registry
            // authoring order, which made Warrior open on Bracer and Speedy on
            // Second Wind, i.e. both classes defaulted to the alternative fork.
            _selection.Passive = _abilityRegistry?.GetDefaultPassiveForClass(cls);

            // Seed the mandatory Peck so the picker shows the same loadout the spawner
            // would build. Without this the player composes four abilities, hits READY,
            // and MatchBootstrapper silently swaps one out for Peck — a UI that lied.
            SeedMandatoryPeck();

            RefreshCharacterSelect();
        }

        private void SelectPassive(PassiveAbilitySO passive)
        {
            if (_selection == null || passive == null) return;
            _selection.Passive = passive;
            // Passives no longer change the slot count — every class has four buttons as
            // of v0.7, which is what retired COMBO's old job of granting a third.
            RefreshCharacterSelect();
        }

        private ChickenClass Cls => _selection?.SelectedClass ?? ChickenClass.Warrior;
        /// <summary>
        /// Total active-ability slots the loadout has to fill — four for every class as
        /// of v0.7, which retired COMBO's old job of granting a third. Purely a slot
        /// COUNT: any class-legal ability, Common or Character, may land in any slot.
        /// Peck is the one forced occupant, and only for classes that can forage.
        /// </summary>
        private int ActiveSlotsForClass => AbilityController.SlotCount;

        private Color TintOf(ChickenClass cls)
        {
            if (_classRegistry != null && _classRegistry.TryGet(cls, out var e) && e.TintColor.a > 0f)
                return e.TintColor;
            return Meta[cls].Tint;
        }

        private void RefreshCharacterSelect()
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
            if (_previewName != null)    { _previewName.text = m.Name; _previewName.style.color = Lighten(TintOf(cls), 0.35f); }
            if (_previewQuote != null)
            {
                if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry) && !string.IsNullOrEmpty(entry.LoreQuote))
                    _previewQuote.text = $"\"{entry.LoreQuote}\"";
                else
                    _previewQuote.text = "";
            }

            // Signature first, alternative second — registry order would put Bracer
            // ahead of Mighty and Second Wind ahead of Slippery, reading as if the
            // alternative were the class's identity. OrderByDescending is stable, so
            // the two entries keep their relative authoring order within each group.
            var passives = _abilityRegistry?.GetPassivesForClass(cls)
                                            .OrderByDescending(p => p.IsSignature)
                                            .ToList();
            if (passives != null && passives.Count >= 2)
            {
                var p1 = passives[0];
                var p2 = passives[1];

                if (_selection != null && (_selection.Passive == null || (_selection.Passive != p1 && _selection.Passive != p2)))
                {
                    _selection.Passive = p1;
                }

                if (_passiveOpt1 != null)
                {
                    _passiveOpt1.text = p1.DisplayName.ToUpper();
                    _passiveOpt1.clickable = new Clickable(() => SelectPassive(p1));
                    bool sel1 = _selection?.Passive == p1;
                    _passiveOpt1.EnableInClassList("cw-btn--green", sel1);
                    _passiveOpt1.EnableInClassList("cw-btn--neutral", !sel1);
                }
                if (_passiveOpt2 != null)
                {
                    _passiveOpt2.text = p2.DisplayName.ToUpper();
                    _passiveOpt2.clickable = new Clickable(() => SelectPassive(p2));
                    bool sel2 = _selection?.Passive == p2;
                    _passiveOpt2.EnableInClassList("cw-btn--green", sel2);
                    _passiveOpt2.EnableInClassList("cw-btn--neutral", !sel2);
                }
            }

            var curPassive = _selection?.Passive;
            if (_previewDesc != null)
            {
                // Never ShortLabel — that is the ≤4-char HUD abbreviation, so the
                // preview used to explain Bracer as "BRCR". Description is authored
                // on the SO; DataIntegrityTests fails the build if one is blank.
                _previewDesc.text = curPassive != null ? curPassive.Description : string.Empty;
            }

            RefreshStats(cls);
            RebuildPickRows(cls);
            RefreshAbilityDetail();
            RefreshEquippedState();
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

        private void RefreshStats(ChickenClass cls)
        {
            var stats = Meta[cls].Stats;
            var tint  = TintOf(cls);
            for (int i = 0; i < StatRowNames.Length; i++)
            {
                var row = _charSelect.Q<VisualElement>(StatRowNames[i]);
                if (row == null) continue;
                var pips = row.Query(className: "cw-pip").ToList();
                for (int p = 0; p < pips.Count; p++)
                {
                    bool on = p < stats[i];
                    pips[p].EnableInClassList("cw-pip--on", on);
                    // The unfilled track has to be visible or the rating has no
                    // denominator — "3 pips" reads as the whole scale otherwise.
                    // The old near-black rgba(20,12,6,.7) vanished on the panel.
                    pips[p].style.backgroundColor = on ? tint : new Color(1f, 0.96f, 0.88f, 0.20f);
                }
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
        //  1st slot. Ability0/Ability1/Ability2 are now purely positional
        //  bookkeeping for N total active-ability slots (N = ActiveSlotsForClass:
        //  2 normally, 3 under Assassin's COMBO). Any legal ability from EITHER
        //  row can land in ANY open slot, freely mixed — 0 Common + N Character,
        //  N Common + 0 Character (bounded by the 3-ability Common pool), or any
        //  mix in between are all equally valid. There is no per-category
        //  minimum; only the total count (== N) gates READY.
        //
        //  The two rows stay as a discoverability grouping — legality/category
        //  is still visually separated — but tapping a card in either row now
        //  fills the next open slot among all N, not a category-fixed slot.
        //  Ability0/1/2 and every ISessionSelectionService write are unchanged,
        //  so the spawner and the touch HUD's 1/2/3 hex mapping keep working
        //  exactly as before.
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

            var flag = cls switch
            {
                ChickenClass.Warrior  => ChickenClassFlags.Warrior,
                ChickenClass.Speedy   => ChickenClassFlags.Speedy,
                ChickenClass.Fatty    => ChickenClassFlags.Fatty,
                ChickenClass.Assassin => ChickenClassFlags.Assassin,
                _ => ChickenClassFlags.None,
            };

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

            // One tap does both jobs: equips/unequips AND reveals what the ability
            // does in the detail strip. Deliberately NOT a long-press — that has no
            // affordance on a touch screen, and a player who never discovers the
            // gesture is back to "equip it and find out in a match".
            card.RegisterCallback<ClickEvent>(_ => { _focusedAbility = ab; TogglePick(ab); });
            return card;
        }

        /// <summary>The Peck (foraging) ability asset, or null if the registry has none.</summary>
        private AbilityBaseSO PeckAbility =>
            _abilityRegistry?.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);

        /// <summary>True when the selected class may forage. Read off Peck's own AllowedClasses
        /// rather than naming classes here, so the picker and MatchBootstrapper's sanitiser can
        /// never disagree about who gets a mandatory Peck.</summary>
        private bool ClassCanForage
        {
            get
            {
                var peck = PeckAbility;
                return peck != null && AbilityRegistrySO.IsAllowedFor(peck, Cls);
            }
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
        /// Tap an unequipped, legal card (Common or Character — category no
        /// longer matters) to pick it, tap an equipped card again to drop it.
        /// Picking fills the first open slot among all N active slots
        /// (<see cref="ActiveSlotsForClass"/>), regardless of which row the card
        /// came from; when all N are already full it drops the oldest pick,
        /// shifts the rest down, and appends — same "never silently ignore the
        /// tap" behaviour the old Class-row-full case had, just generalized.
        /// Dropping compacts the remaining picks so a slot is never empty while
        /// a higher-indexed slot is occupied (the touch HUD maps slots 1/2/3 to
        /// its three hexes positionally, and COMBO alone unlocks the third).
        /// </summary>
        /// <summary>
        /// Ensures the mandatory Peck is equipped for a foraging class, and absent for one
        /// that cannot forage. Mirrors <c>MatchBootstrapper.ResolveLegalLoadout</c>'s rule so
        /// what the player composes is what actually spawns.
        /// </summary>
        private void SeedMandatoryPeck()
        {
            var peck = PeckAbility;
            if (peck == null || _selection == null) return;

            int n = ActiveSlotsForClass;
            int at = SlotOf(peck);

            if (!ClassCanForage)
            {
                if (at >= 0) SetEquipped(at, null);
                return;
            }

            if (at >= 0) return; // already placed; leave the player's choice alone

            for (int i = 0; i < n; i++)
            {
                if (GetEquipped(i) == null) { SetEquipped(i, peck); return; }
            }
            SetEquipped(0, peck); // no room: Peck outranks whatever was there
        }

        private void TogglePick(AbilityBaseSO ab)
        {
            if (_selection == null || ab == null) return;

            int n = ActiveSlotsForClass;
            int existing = SlotOf(ab);

            // Peck is not optional for a class that can forage — dropping it would mean
            // being unable to collect food for the whole match. So a tap on the equipped
            // Peck card MOVES it to the next button instead of removing it, swapping with
            // whatever sat there. That is what makes its position player-assignable
            // without a drag-and-drop affordance, and the numbered badge on the card is
            // already the readout of which button it will fire from.
            if (existing >= 0 && ab is PeckAbilitySO)
            {
                int to = (existing + 1) % n;
                var displaced = GetEquipped(to);
                SetEquipped(to, ab);
                SetEquipped(existing, displaced);

                RebuildPickRows(Cls);
                RefreshAbilityDetail();
                RefreshEquippedState();
                return;
            }

            if (existing >= 0)
            {
                // Unequip: clear just this slot. It deliberately does NOT compact the rest
                // down any more — compaction would drag Peck out of the button the player
                // deliberately placed it on every time they swapped a neighbouring pick.
                SetEquipped(existing, null);
            }
            else
            {
                int freeSlot = -1;
                for (int i = 0; i < n; i++)
                {
                    if (GetEquipped(i) == null) { freeSlot = i; break; }
                }

                if (freeSlot >= 0)
                {
                    SetEquipped(freeSlot, ab);
                }
                else
                {
                    // All N slots full — drop the oldest, shift down, append.
                    for (int i = 0; i < n - 1; i++) SetEquipped(i, GetEquipped(i + 1));
                    SetEquipped(n - 1, ab);
                }
            }

            RebuildPickRows(Cls);
            RefreshAbilityDetail();
            RefreshEquippedState();
        }

        /// <summary>
        /// READY gates on total distinct equipped abilities == N + a Passive —
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
            Bind<Button>(_lobby, "BackBtn",  b => b.clicked += ShowCharacterSelect);
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
                    grid.Add(MakeLobbyCard(i, bc, BotNames[i - 1], false, true, ready: true, abilities: SampleBotAbilities(i), empty: false, slots: 2));
                }
                else
                {
                    grid.Add(MakeLobbyCard(i, ChickenClass.Warrior, null, false, false, ready: false, abilities: null, empty: true, slots: 2));
                }
            }
        }

        private List<AbilityBaseSO> SampleBotAbilities(int seed)
        {
            var list = new List<AbilityBaseSO>();
            var pool = _abilityRegistry?.All;
            if (pool == null) return list;
            var valid = pool.Where(a => a != null).ToList();
            if (valid.Count == 0) return list;
            list.Add(valid[(seed * 2) % valid.Count]);
            list.Add(valid[(seed * 2 + 1) % valid.Count]);
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
            var clsLine = new Label($"{m.Name.Replace(" CHICKEN", string.Empty)} · {m.PassiveName}");
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

        /// <summary>Ported from the old CharacterSelectController for parity.</summary>
        public static (string name, string desc, string subRole) GetPassiveInfo(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior  => ("MIGHTY",     "+25% outgoing ability damage.", "All-Rounder"),
            ChickenClass.Speedy   => ("SLIPPERY",  "Reduced control-effect duration.", "Hit & Run"),
            ChickenClass.Fatty    => ("IMMOVABLE", "Greatly reduced knockback.", "Bulk Carrier"),
            ChickenClass.Assassin => ("COMBO",     "Equips 3 abilities instead of 2.", "Disruptor"),
            _                     => ("—",         "", ""),
        };
    }
}
