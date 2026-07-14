using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
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

        // ---- Runtime state ----------------------------------------------------
        private VisualElement _root;
        private VisualElement _mainMenu, _charSelect, _lobby;
        private int  _pickerSlot;
        private bool _isBusy;

        private readonly Dictionary<ChickenClass, VisualElement> _classChips = new();
        private VisualElement _previewChicken, _previewGlow, _previewDisc, _slotRow, _abilityGrid;
        private Label _previewName, _previewQuote, _previewPassive, _previewDesc, _equippedLabel;
        private Button _readyBtn;
        private readonly List<VisualElement> _slotHexes = new();
        // Icon USS class currently applied to each slot's sprite element (for swap).
        private readonly List<string> _slotAppliedIcon = new();
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
            public int Slots;
            public int[] Stats; // cargo, rate, hp, resist, speed (1..5)
        }

        private static readonly Dictionary<ChickenClass, ClassMeta> Meta = new()
        {
            [ChickenClass.Warrior]  = new ClassMeta { Name = "WARRIOR CHICKEN",  Role = "All-Rounder",  Tint = UiGfx.Hex32("C04030"), Slots = 2, PassiveName = "MIGHTY",     PassiveDesc = "+25% outgoing ability damage.",  Stats = new[]{3,3,4,3,3} },
            [ChickenClass.Speedy]   = new ClassMeta { Name = "SPEEDY CHICKEN",   Role = "Hit & Run",    Tint = UiGfx.Hex32("E85A2A"), Slots = 2, PassiveName = "SLIPPERY",  PassiveDesc = "Reduced control-effect duration.", Stats = new[]{2,3,2,1,5} },
            [ChickenClass.Fatty]    = new ClassMeta { Name = "FATTY CHICKEN",    Role = "Bulk Carrier", Tint = UiGfx.Hex32("F5D75A"), Slots = 2, PassiveName = "IMMOVABLE", PassiveDesc = "Greatly reduced knockback.",       Stats = new[]{5,5,5,5,2} },
            [ChickenClass.Assassin] = new ClassMeta { Name = "ASSASSIN CHICKEN", Role = "Disruptor",    Tint = UiGfx.Hex32("7B68EE"), Slots = 3, PassiveName = "COMBO",     PassiveDesc = "Equips 3 abilities instead of 2.", Stats = new[]{2,2,2,2,4} },
        };

        private static readonly string[] StatRowNames = { "StatCargo", "StatRate", "StatHp", "StatResist", "StatSpeed" };
        private static readonly string[] ChipNames    = { "ClassWarrior", "ClassSpeedy", "ClassFatty", "ClassAssassin" };

        private sealed class CatMeta { public string Label, Blurb; public Color Color; }
        private static readonly (AbilityCategory cat, CatMeta meta)[] Cats =
        {
            (AbilityCategory.Damage,  new CatMeta { Label = "DAMAGE",  Blurb = "Deals HP — can stun at 0",  Color = UiGfx.Hex32("FF5722") }),
            (AbilityCategory.Control, new CatMeta { Label = "CONTROL", Blurb = "No HP — disrupts movement", Color = UiGfx.Hex32("9C27B0") }),
            (AbilityCategory.Defense, new CatMeta { Label = "DEFENSE", Blurb = "Protect self or cargo",     Color = UiGfx.Hex32("4CAF50") }),
            (AbilityCategory.Utility, new CatMeta { Label = "UTILITY", Blurb = "Non-combat advantage",      Color = UiGfx.Hex32("00BCD4") }),
        };

        // ======================================================================
        [Inject]
        public void Construct(
            ISessionSelectionService selection,
            ILogService log,
            IUGSService ugs,
            [InjectOptional] SceneLoader sceneLoader,
            [InjectOptional] ChickenClassRegistrySO classRegistry,
            [InjectOptional] AbilityRegistrySO abilityRegistry)
        {
            _selection       = selection;
            _log             = log;
            _ugs             = ugs;
            _sceneLoader     = sceneLoader;
            _classRegistry   = classRegistry;
            _abilityRegistry = abilityRegistry;
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
        }

        private void ChooseMode(SessionMode mode)
        {
            if (_selection != null) _selection.Mode = mode;
            ShowCharacterSelect();
        }

        // ======================================================================
        //  CHARACTER SELECT
        // ======================================================================
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
            _previewPassive = _charSelect.Q<Label>("PreviewPassive");
            _previewDesc    = _charSelect.Q<Label>("PreviewDesc");
            _slotRow        = _charSelect.Q<VisualElement>("SlotRow");
            _abilityGrid    = _charSelect.Q<VisualElement>("AbilityGrid");
            _equippedLabel  = _charSelect.Q<Label>("EquippedLabel");
            _readyBtn       = _charSelect.Q<Button>("ReadyBtn");
            if (_readyBtn != null) _readyBtn.clicked += OnReady;

            var ef = UiGfx.EmojiFont();
            if (ef != null)
            {
                var emojis = _charSelect.Query<Label>(className: "cw-emoji-text").ToList();
                foreach (var e in emojis) e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
            }

            Bind<Button>(_charSelect, "HomeBtn", b => b.clicked += ShowMainMenu);
        }

        private void SelectClass(ChickenClass cls)
        {
            if (_selection == null) return;
            _selection.SelectedClass = cls;
            _pickerSlot = 0;
            _selection.Ability0 = null;
            _selection.Ability1 = null;
            _selection.Ability2 = null;
            RefreshCharacterSelect();
        }

        private ChickenClass Cls => _selection?.SelectedClass ?? ChickenClass.Warrior;

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
            if (_previewName != null)    { _previewName.text = m.Name; _previewName.style.color = TintOf(cls); }
            if (_previewQuote != null)
            {
                if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry) && !string.IsNullOrEmpty(entry.LoreQuote))
                    _previewQuote.text = $"\"{entry.LoreQuote}\"";
                else
                    _previewQuote.text = "";
            }
            if (_previewPassive != null) { _previewPassive.text = $"PASSIVE · {m.PassiveName}"; _previewPassive.style.backgroundColor = TintOf(cls); _previewPassive.style.color = UiGfx.TextPrimary; }
            if (_previewDesc != null)    _previewDesc.text = m.PassiveDesc;

            RefreshStats(cls);
            RebuildSlots(cls);
            RebuildAbilityGrid();
            RefreshEquippedState();
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
                    pips[p].style.backgroundColor = on ? tint : new Color(20f / 255f, 12f / 255f, 6f / 255f, 0.7f);
                }
            }
        }

        // ---- Slots ------------------------------------------------------------
        private void RebuildSlots(ChickenClass cls)
        {
            if (_slotRow == null) return;
            _slotRow.Clear();
            _slotHexes.Clear();
            _slotAppliedIcon.Clear();
            int slots = Meta[cls].Slots;
            if (_pickerSlot >= slots) _pickerSlot = 0;

            for (int i = 0; i < slots; i++)
            {
                int idx = i;
                var col = new VisualElement();
                col.style.alignItems = Align.Center;
                col.style.marginLeft = 14; col.style.marginRight = 14;

                var caption = new Label(i == 2 ? "★ S3" : $"S{i + 1}");
                caption.AddToClassList("cw-slot-caption");
                caption.style.marginBottom = 4;

                var hex = new VisualElement();
                hex.AddToClassList("cw-hex");
                // Exported ability-icon sprite (shown when filled) + "+" placeholder
                // (shown when empty); RefreshSlotVisuals toggles between them.
                var sprite = new VisualElement();
                sprite.AddToClassList("cw-hex__sprite");
                sprite.style.display = DisplayStyle.None;
                var icon = new Label("+");
                icon.AddToClassList("cw-hex__icon");
                hex.Add(sprite);
                hex.Add(icon);

                col.Add(caption); col.Add(hex);
                col.RegisterCallback<ClickEvent>(_ => SelectPickerSlot(idx));
                _slotRow.Add(col);
                _slotHexes.Add(hex);
                _slotAppliedIcon.Add(null);
            }

            // Non-Assassin classes show a dimmed, locked slot-3 hex (design §ColumnC).
            if (slots == 2)
            {
                var lockCol = new VisualElement();
                lockCol.style.alignItems = Align.Center;
                lockCol.style.marginLeft = 14; lockCol.style.marginRight = 14;
                lockCol.style.opacity = 0.35f;

                var lockCap = new Label("ASSASSIN");
                lockCap.AddToClassList("cw-slot-caption");
                lockCap.style.marginBottom = 4;

                var lockHex = new VisualElement();
                lockHex.AddToClassList("cw-hex");
                lockHex.style.unityBackgroundImageTintColor = HexEmptyTint;
                var lockIcon = new Label("🔒");
                lockIcon.AddToClassList("cw-hex__icon");
                var ef = UiGfx.EmojiFont();
                if (ef != null) lockIcon.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
                lockHex.Add(lockIcon);

                lockCol.Add(lockCap); lockCol.Add(lockHex);
                _slotRow.Add(lockCol);
            }

            RefreshSlotVisuals();
        }

        private void SelectPickerSlot(int slot)
        {
            _pickerSlot = slot;
            RefreshSlotVisuals();
            RebuildAbilityGrid();
        }

        private AbilityBaseSO GetEquipped(int slot) => slot switch
        {
            0 => _selection?.Ability0,
            1 => _selection?.Ability1,
            2 => _selection?.Ability2,
            _ => null,
        };

        private void SetEquipped(int slot, AbilityBaseSO ab)
        {
            if (_selection == null) return;
            if (slot == 0) _selection.Ability0 = ab;
            else if (slot == 1) _selection.Ability1 = ab;
            else if (slot == 2) _selection.Ability2 = ab;
        }

        private void RefreshSlotVisuals()
        {
            for (int i = 0; i < _slotHexes.Count; i++)
            {
                var hex = _slotHexes[i];
                var ab = GetEquipped(i);
                var plus   = hex.Q<Label>(className: "cw-hex__icon");
                var sprite = hex.Q<VisualElement>(className: "cw-hex__sprite");
                bool active = i == _pickerSlot;

                // Swap the exported icon sprite class on change.
                if (sprite != null)
                {
                    if (!string.IsNullOrEmpty(_slotAppliedIcon[i]))
                        sprite.RemoveFromClassList(_slotAppliedIcon[i]);
                    string cls = AbilityIconStyle.ClassFor(ab);
                    _slotAppliedIcon[i] = cls;
                    if (!string.IsNullOrEmpty(cls))
                    {
                        sprite.AddToClassList(cls);
                        sprite.style.display = DisplayStyle.Flex;
                    }
                    else sprite.style.display = DisplayStyle.None;
                }

                if (ab != null)
                {
                    hex.style.unityBackgroundImageTintColor = ab.AccentColor;
                    if (plus != null) plus.style.display = DisplayStyle.None; // sprite carries it
                }
                else
                {
                    hex.style.unityBackgroundImageTintColor = HexEmptyTint;
                    if (plus != null)
                    {
                        plus.style.display = DisplayStyle.Flex;
                        plus.text = "+";
                        plus.style.color = UiGfx.Gold;
                    }
                }
                hex.EnableInClassList("cw-hex--active", active);
            }
        }

        // ---- Ability grid -----------------------------------------------------
        private void RebuildAbilityGrid()
        {
            if (_abilityGrid == null) return;
            _abilityGrid.Clear();

            var all  = _abilityRegistry?.All;
            var pool = (all == null ? Enumerable.Empty<AbilityBaseSO>() : all.Where(a => a != null)).ToList();
            if (pool.Count == 0)
            {
                var note = new Label("No abilities in registry.\nAssign AbilityRegistrySO in ProjectInstaller.");
                note.AddToClassList("cw-body");
                _abilityGrid.Add(note);
                return;
            }

            foreach (var (cat, cm) in Cats)
            {
                var list = pool.Where(a => a.Category == cat).ToList();
                if (list.Count == 0) continue;

                var header = new VisualElement();
                header.AddToClassList("cw-category-header");
                header.style.borderLeftColor = cm.Color;
                header.style.backgroundColor = Fade(cm.Color, 0.18f); // faint category tint (design)
                var hl = new Label(cm.Label); hl.AddToClassList("cw-category-header__label");
                var hb = new Label(cm.Blurb); hb.AddToClassList("cw-category-header__blurb");
                header.Add(hl); header.Add(hb);
                _abilityGrid.Add(header);

                var grid = new VisualElement();
                grid.AddToClassList("cw-ability-grid");
                foreach (var ab in list) grid.Add(MakeAbilityCard(ab));
                _abilityGrid.Add(grid);
            }
        }

        private VisualElement MakeAbilityCard(AbilityBaseSO ab)
        {
            var card = new VisualElement();
            card.AddToClassList("cw-ability-card");

            // Exported design icon sprite; fall back to the emoji glyph only for an
            // ability with no sprite mapping (shouldn't happen for shipped abilities).
            VisualElement icon;
            string iconCls = AbilityIconStyle.ClassFor(ab);
            if (!string.IsNullOrEmpty(iconCls))
            {
                var s = new VisualElement();
                s.AddToClassList("cw-ability-card__sprite");
                s.AddToClassList(iconCls);
                icon = s;
            }
            else
            {
                var lbl = new Label(ab.ResolveIcon());
                lbl.AddToClassList("cw-ability-card__icon");
                var ef = UiGfx.EmojiFont();
                if (ef != null) lbl.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(ef));
                lbl.style.color = ab.AccentColor;
                icon = lbl;
            }

            // Readable full name on the card (cards are wide enough); fall back to asset name.
            string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
            var name = new Label(label.ToUpperInvariant());
            name.AddToClassList("cw-ability-card__name");

            bool shortCd = ab.Cooldown <= 6f;
            var badge = new Label(shortCd ? "SHORT" : "MED");
            badge.AddToClassList("cw-cd-badge");
            badge.AddToClassList(shortCd ? "cw-cd-badge--short" : "cw-cd-badge--med");

            card.Add(icon); card.Add(name); card.Add(badge);

            int slot = SlotOf(ab);
            if (slot >= 0)
            {
                SetBorder(card, ab.AccentColor);
                card.style.backgroundColor = Fade(ab.AccentColor, 0.20f);

                // Numbered slot badge (design §ColumnC) — shows which slot it fills.
                var slotBadge = new Label((slot + 1).ToString());
                slotBadge.AddToClassList("cw-ability-badge");
                slotBadge.style.backgroundColor = ab.AccentColor;
                card.Add(slotBadge);
            }

            card.RegisterCallback<ClickEvent>(_ => EquipInSelectedSlot(ab));
            return card;
        }

        private int SlotOf(AbilityBaseSO ab)
        {
            int slots = Meta[Cls].Slots;
            for (int i = 0; i < slots; i++) if (GetEquipped(i) == ab) return i;
            return -1;
        }

        private void EquipInSelectedSlot(AbilityBaseSO ab)
        {
            if (_selection == null) return;
            int existing = SlotOf(ab);

            if (existing == _pickerSlot)              // tap equipped-in-active → unequip
            {
                SetEquipped(_pickerSlot, null);
            }
            else if (existing >= 0)                   // dedup: swap between slots
            {
                var cur = GetEquipped(_pickerSlot);
                SetEquipped(existing, cur);
                SetEquipped(_pickerSlot, ab);
            }
            else
            {
                SetEquipped(_pickerSlot, ab);
            }

            // advance picker to next empty slot
            int slots = Meta[Cls].Slots;
            for (int i = 0; i < slots; i++)
                if (GetEquipped(i) == null) { _pickerSlot = i; break; }

            RefreshSlotVisuals();
            RebuildAbilityGrid();
            RefreshEquippedState();
        }

        private void RefreshEquippedState()
        {
            int slots = Meta[Cls].Slots;
            int n = 0;
            for (int i = 0; i < slots; i++) if (GetEquipped(i) != null) n++;
            if (_equippedLabel != null) _equippedLabel.text = $"EQUIPPED · {n}/{slots}";

            bool ready = n >= slots;
            if (_readyBtn != null)
            {
                _readyBtn.text = ready ? "READY ▶" : $"PICK {slots - n} MORE";
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
            for (int i = 0; i < 3; i++) { var a = GetEquipped(i); if (a != null) mine.Add(a); }
            grid.Add(MakeLobbyCard(0, Cls, "You", isHost, false, ready: true, abilities: mine, empty: false));

            // Slots 1-3 — solo fills CPU bots; host/join show open seats.
            for (int i = 1; i < 4; i++)
            {
                if (isSolo)
                {
                    var bc = BotClasses[i - 1];
                    grid.Add(MakeLobbyCard(i, bc, BotNames[i - 1], false, true, ready: true, abilities: SampleBotAbilities(i), empty: false));
                }
                else
                {
                    grid.Add(MakeLobbyCard(i, ChickenClass.Warrior, null, false, false, ready: false, abilities: null, empty: true));
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

        private VisualElement MakeLobbyCard(int idx, ChickenClass cls, string name, bool isHost, bool cpu, bool ready, List<AbilityBaseSO> abilities, bool empty)
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
                tag.style.color = UiGfx.TextPrimary;
                nameRow.Add(tag);
            }
            mid.Add(nameRow);

            var m = Meta[cls];
            var clsLine = new Label($"{m.Name.Replace(" CHICKEN", string.Empty)} · {m.PassiveName}");
            clsLine.AddToClassList("cw-player-class");
            mid.Add(clsLine);

            var chips = new VisualElement();
            chips.AddToClassList("cw-player-chips");
            for (int i = 0; i < m.Slots; i++)
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
