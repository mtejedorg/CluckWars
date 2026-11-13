using CluckWars.Injection;
using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;


namespace CluckWars.UI
{
    /// <summary>
    /// <see cref="MenuUiController"/>: step 2 of the flow, GEAR UP: the slots, the fixed-kit view, the arming state machine, the fly-to-slot and the deck (A21, architecture audit 2026-11-12).
    /// </summary>
    /// <remarks>
    /// One component, several files. The Bootstrap scene references this type by name and every serialized
    /// field and bound element reference has to stay on it, so the split is a partial class, never a second
    /// component. Navigation stays in the main file: it owns the page list, ShowPage, the back stack and the
    /// build order, and calls into each page's Build / Refresh here.
    /// </remarks>
    public sealed partial class MenuUiController
    {
        // ======================================================================
        //  STEP 2 — GEAR UP
        // ======================================================================
        private sealed class SlotView
        {
            public Button Root;
            public AbilityIconView Icon;
            public VisualElement IconHost;
            public Label Name, Empty, CaretLabel;
            public VisualElement StarterTag;
        }

        private sealed class DeckCardView
        {
            public AbilityBaseSO Ability;
            public VisualElement Root, Glow, StarterTag, IconHost;
            public Label Badge;
        }

        private void BuildLoadout()
        {
            _gearBody          = _loadout.Q<VisualElement>("Body");
            _kitTitle          = _loadout.Q<Label>("KitTitle");
            if (_gearBody == null || _kitTitle == null)
                _log?.Error(Source, "CharacterSelect.uxml has no #Body / #KitTitle; Fixed-mode GEAR UP keeps showing the deck and no specialization name.");
            _commonCards       = _loadout.Q<VisualElement>("CommonCards");
            _classCards        = _loadout.Q<VisualElement>("ClassCards");
            _commonTitle       = _loadout.Q<Label>("CommonTitle");
            _classTitle        = _loadout.Q<Label>("ClassTitle");
            _abilityDetail     = _loadout.Q<VisualElement>("AbilityDetail");
            _abilityDetailName = _loadout.Q<Label>("AbilityDetailName");
            _abilityDetailText = _loadout.Q<Label>("AbilityDetailText");
            _abilityDetailCat  = _loadout.Q<Label>("AbilityDetailCat");
            _abilityDetailCd   = _loadout.Q<Label>("AbilityDetailCd");
            _readyBtn          = _loadout.Q<Button>("ReadyBtn");
            _gearChicken       = _loadout.Q<VisualElement>("GearChicken");
            if (_gearChicken == null)
                _log?.Error(Source, "CharacterSelect.uxml has no #GearChicken; GEAR UP shows no bird.");
            var doorway = _loadout.Q<VisualElement>("GearDoorway");
            if (doorway == null)
                _log?.Error(Source, "CharacterSelect.uxml has no #GearDoorway; the GEAR UP bird has no layout slot.");
            else if (_gearChicken != null)
                // The doorway is the height left between the slots and the deck; a whole bird or none.
                doorway.RegisterCallback<GeometryChangedEvent>(e =>
                    _gearChicken.EnableInClassList("cw-gear-chicken--nofit", !GearDoorway.Fits(e.newRect.height)));
            if (_readyBtn != null) { _readyBtn.clicked += OnReady; OwnCue(_readyBtn); }
            // WP9 (C7): the deck scrolls in portrait. Pad / keyboard focus moving onto a card below the fold brings it into view.
            var deckScroll = _loadout.Q<ScrollView>("DeckScroll");
            if (deckScroll == null)
                _log?.Error(Source, "CharacterSelect.uxml has no #DeckScroll; in portrait the deck cannot scroll and READY can be pushed off screen.");
            else
                deckScroll.RegisterCallback<FocusInEvent>(e =>
                {
                    if (e.target is VisualElement ve && deckScroll.contentContainer.Contains(ve)) deckScroll.ScrollTo(ve);
                });

            for (int i = 0; i < _slots.Length; i++)
            {
                var box = _loadout.Q<Button>("SlotBox" + i);
                if (box == null)
                {
                    _log?.Error(Source, $"CharacterSelect.uxml has no #SlotBox{i}; slot {i + 1} cannot be shown or armed.");
                    continue;
                }
                _slots[i] = BuildSlot(box, i);
                int captured = i;
                OwnCue(box);
                box.clicked += () => OnSlotBoxTapped(captured);
            }

            Bind<Button>(_loadout, "BackBtn", b => { b.clicked += ShowClassSelect; BackCue(b); });
        }

        /// <summary>Builds one slot's children once: number, icon, name / EMPTY, STARTER tag, NEXT caret.</summary>
        private SlotView BuildSlot(Button box, int index)
        {
            box.text = string.Empty;
            var v = new SlotView { Root = box };

            var num = new Label((index + 1).ToString()) { pickingMode = PickingMode.Ignore };
            num.AddToClassList("cw-slot-box__num");

            v.IconHost = new VisualElement { pickingMode = PickingMode.Ignore };
            v.IconHost.AddToClassList("cw-slot-box__icon");
            v.Icon = new AbilityIconView(v.IconHost);

            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("cw-slot-box__texts");
            v.Name = new Label { pickingMode = PickingMode.Ignore };
            v.Name.AddToClassList("cw-slot-box__name");
            v.Empty = new Label(UiText.Get(UiKeys.SlotEmpty)) { pickingMode = PickingMode.Ignore };
            v.Empty.AddToClassList("cw-slot-box__empty-label");
            v.StarterTag = MakeStarterTag();
            texts.Add(v.Name); texts.Add(v.Empty); texts.Add(v.StarterTag);

            // NEXT caret: word + pointer, shown above the armed slot (not colour-only).
            var caret = new VisualElement { pickingMode = PickingMode.Ignore };
            caret.AddToClassList("cw-next-caret");
            var caretLabel = v.CaretLabel = new Label(UiText.Get(UiKeys.LoadoutNext)) { pickingMode = PickingMode.Ignore };
            caretLabel.AddToClassList("cw-next-caret__label");
            var tip = new VisualElement { pickingMode = PickingMode.Ignore };
            tip.AddToClassList("cw-next-caret__tip");
            caret.Add(caretLabel); caret.Add(tip);

            box.Add(v.IconHost); box.Add(texts); box.Add(num); box.Add(caret);
            return v;
        }

        /// <summary>Padlock + STARTER on an ink pill (built once per owner, toggled by display).</summary>
        private static VisualElement MakeStarterTag()
        {
            var tag = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("cw-starter-tag");
            var padlock = new VisualElement { pickingMode = PickingMode.Ignore };   // Glyph_Lock sprite (USS)
            padlock.AddToClassList("cw-starter-tag__lock");
            var starter = new Label(UiText.Get(UiKeys.LabelStarter)) { pickingMode = PickingMode.Ignore };
            starter.AddToClassList("cw-starter-tag__text");
            tag.Add(padlock); tag.Add(starter);
            return tag;
        }

        /// <summary>Entering Step 2: make sure the deck matches the class, re-arm, repaint.</summary>
        private void RefreshLoadout()
        {
            RefreshGearChicken();
            EnsureDeck(Cls);
            ApplyFixedKitView();
            SyncSlotModel();
            _slotModel.AutoArmFirstEmpty();
            RefreshLoadoutState();
        }

        // ---- Fixed-mode GEAR UP (LoadoutMode.Fixed): a read-only view of the kit -------------------
        // The slot boxes stay (tapping one shows its details); every one is locked, so nothing arms and no caret shows,
        // and the deck is hidden by USS (.cw-gear-body--fixed). A malformed kit leaves all of this off: the Free picker
        // comes back, matching the Free path the spawner falls to.

        private LoadoutMode LoadoutModeNow => _matchConfig != null ? _matchConfig.LoadoutMode : LoadoutMode.Free;

        private bool TryGetKit(out AbilityBaseSO[] kit) =>
            FixedKit.TryResolve(LoadoutModeNow, _selection?.Passive, out kit, out _);

        /// <summary>Puts the kit into the selection and switches the page between its Fixed and Free faces.</summary>
        private void ApplyFixedKitView()
        {
            bool isFixed = TryGetKit(out var kit);
            _gearBody?.EnableInClassList("cw-gear-body--fixed", isFixed);
            if (_kitTitle != null)
            {
                UiElements.SetShown(_kitTitle, isFixed);
                if (isFixed) _kitTitle.text = UiText.Format(UiKeys.LoadoutKitTitle, ("perk", _selection.Passive.DisplayName.ToUpperInvariant()));
            }
            if (!isFixed) return;

            SeedForcedAbilities();
            if (Array.IndexOf(kit, _focusedAbility) < 0) _focusedAbility = kit[0];
        }

        /// <summary>The doorway bird shows the chosen class: the static render, or the live stage.</summary>
        private void RefreshGearChicken()
        {
            if (_gearChicken == null) return;
            if (!string.IsNullOrEmpty(_gearChickenClass)) _gearChicken.RemoveFromClassList(_gearChickenClass);
            _gearChickenClass = "cw-chicken--" + KeyOf(Cls);
            _gearChicken.AddToClassList(_gearChickenClass);
            ShowGearOnStage(hop: false);
        }

        /// <summary>Repaints every GEAR UP surface from the model (no element is created here).</summary>
        private void RefreshLoadoutState()
        {
            RefreshSlotBoxes();
            RefreshDeck();
            RefreshAbilityDetail();
            RefreshEquippedState();
        }

        // ---- Slot-arming state machine (LoadoutSlotModel) ------------------------
        // The selection service owns the four slots; the model is loaded from it before each
        // action and written back after, so it never drifts from what the spawner will read.

        private void SyncSlotModel()
        {
            int n = ActiveSlotsForClass;
            var slots = new AbilityBaseSO[n];
            var locks = new bool[n];
            for (int i = 0; i < n; i++) { slots[i] = GetEquipped(i); locks[i] = IsSlotLocked(i); }
            _slotModel.Load(slots, locks);
        }

        private void WriteSlotModel()
        {
            for (int i = 0; i < ActiveSlotsForClass; i++) SetEquipped(i, _slotModel.Get(i));
        }

        /// <summary>Tapping a slot arms it (locked starters can't be armed) and shows what it holds.</summary>
        private void OnSlotBoxTapped(int index)
        {
            SyncSlotModel();
            var ab = _slotModel.Get(index);
            if (ab != null) _focusedAbility = ab;
            bool wasArmed = _slotModel.Armed == index;
            if (_slotModel.ArmSlot(index) && !wasArmed) _audio.ArmSlot();
            RefreshLoadoutState();
        }

        /// <summary>
        /// One tap on a deck card shows its details AND resolves it against the armed slot:
        /// clear (it was in the armed slot), swap (it was in another slot) or place (then arming
        /// advances). Locked starters only show their details.
        /// </summary>
        private void OnAbilityCardTapped(AbilityBaseSO ab)
        {
            if (_selection == null || ab == null) return;
            _focusedAbility = ab;
            if (TryGetKit(out _)) { RefreshLoadoutState(); return; }   // read-only: details, never a swap
            SyncSlotModel();
            var result = _slotModel.TapCard(ab, IsPreEquipped(ab));
            if (result != CardTapResult.Ignored) WriteSlotModel();
            bool equipped = result == CardTapResult.Placed || result == CardTapResult.Swapped;
            switch (result)
            {
                case CardTapResult.Placed:
                case CardTapResult.Swapped: _audio.EquipTap(); break;   // soft touch now; the thunk plays when the icon lands (LandSlot), or at once without motion
                case CardTapResult.Cleared: _audio.Clear(); break;
                default: _audio.Tap(); break;        // locked starter: details only
            }

            // Model writes above are immediate; only the VISUAL of an equip is delayed. A flight whose
            // slot no longer holds its ability lands now, so nothing is left hovering or out of step.
            SettleFlights();
            int target = equipped ? _slotModel.SlotOf(ab) : -1;
            bool fly = target >= 0 && MenuJuice.Allowed && _juice != null;
            if (fly)
            {
                LandFlight(target);
                HoldSlotContent(target, true);
            }

            RefreshLoadoutState();

            var card = _deckCards.Find(c => c.Ability == ab);
            if (card != null && result != CardTapResult.Ignored)
                _juice?.Pop(card.Root);   // scale-only, settles at 1: picked / focused never move the card

            if (!equipped) return;
            if (!fly || !StartFlight(ab, target, card)) { HoldSlotContent(target, false); _audio.Equip(); ShowGearOnStage(hop: true); }
        }

        // ---- Fly-to-slot ---------------------------------------------------------------
        /// <summary>Hides (or shows) the new content of a slot while its icon is still in the air.</summary>
        private void HoldSlotContent(int slot, bool hold)
        {
            if (slot < 0 || slot >= _slots.Length || _slots[slot] == null) return;
            _slots[slot].IconHost.EnableInClassList("cw-fx-hold", hold);
            _slots[slot].Name.EnableInClassList("cw-fx-hold", hold);
        }

        private bool StartFlight(AbilityBaseSO ab, int slot, DeckCardView card)
        {
            var view = _slots[slot];
            if (card?.IconHost == null || view == null) return false;
            var handle = _juice.Fly(card.IconHost, view.IconHost, ghost => PaintGhost(ghost, ab), () => LandSlot(slot));
            if (handle == null) return false;
            _flightFor[slot] = handle;
            _flightAbility[slot] = ab;
            return true;
        }

        private void PaintGhost(VisualElement ghost, AbilityBaseSO ab)
        {
            if (ghost.userData is not AbilityIconView view)
            {
                view = new AbilityIconView(ghost);
                ghost.userData = view;
            }
            PaintIcon(view, ab);
        }

        /// <summary>The icon arrived: slot content shows with a small pop and sparkles, and the equip thunk plays.</summary>
        private void LandSlot(int slot)
        {
            _flightFor[slot] = null;
            _flightAbility[slot] = null;
            HoldSlotContent(slot, false);
            var view = _slots[slot];
            if (view != null)
            {
                _juice?.Pop(view.IconHost);
                _juice?.BurstAt(view.IconHost, ParticleKind.Sparkle, 4, 60f);
            }
            _audio.Equip();
            ShowGearOnStage(hop: true);
        }

        private void LandFlight(int slot) => _juice?.LandNow(_flightFor[slot]);

        private void SettleFlights()
        {
            for (int i = 0; i < _flightFor.Length; i++)
                if (_flightFor[i] != null && _slotModel.Get(i) != _flightAbility[i]) LandFlight(i);
        }

        private void RefreshSlotBoxes()
        {
            int caret = _slotModel.NextCaretSlot;
            for (int i = 0; i < _slots.Length; i++)
            {
                var v = _slots[i];
                if (v == null) continue;
                var ab = _slotModel.Get(i);
                bool locked = _slotModel.IsLocked(i);
                v.Root.EnableInClassList("cw-slot-box--armed", i == caret);
                v.Root.EnableInClassList("cw-slot-box--caret", i == caret);
                v.Root.EnableInClassList("cw-slot-box--locked", locked);
                v.Root.EnableInClassList("cw-slot-box--filled", ab != null);

                PaintIcon(v.Icon, ab);
                UiElements.SetShown(v.IconHost, ab != null);
                v.Name.text = ab != null ? AbilityLabel(ab) : string.Empty;
                UiElements.SetShown(v.Name, ab != null);
                UiElements.SetShown(v.Empty, ab == null);
                UiElements.SetShown(v.StarterTag, locked && ab != null);
                // The caret says what the next card tap does to this slot (LoadoutSlotModel.TapCard):
                // an empty armed slot is filled (NEXT); a filled one - always the case once the rail
                // is full - has its move replaced or swapped with the tapped card (TAP TO SWAP).
                if (i == caret) v.CaretLabel.text = UiText.Get(ab != null ? UiKeys.LoadoutSwap : UiKeys.LoadoutNext);
            }
        }

        // ---- Deck ------------------------------------------------------------------
        //  Two groups, both always visible: ANY BIRD (AllowedClasses == All) and {CLS} ONLY
        //  (anything else this class may pick). The groups are a discoverability split only —
        //  which slot a tap lands in is decided by the arming state machine. AllowedClasses ==
        //  None (pre-equip-only, e.g. Mark/Kill) never appears; it is only ever a starter.

        /// <summary>Builds the deck cards for <paramref name="cls"/> — once per class change.</summary>
        private void EnsureDeck(ChickenClass cls)
        {
            if (_deckBuiltFor == cls && _deckCards.Count > 0) return;
            _deckBuiltFor = cls;
            _deckCards.Clear();
            _commonCards?.Clear();
            _classCards?.Clear();

            if (_commonTitle != null) _commonTitle.text = UiText.Get(UiKeys.LoadoutRowShared);
            if (_classTitle != null) _classTitle.text = UiText.Format(UiKeys.LoadoutRowClass, ("cls", ClassShortName(cls)));

            var all = _abilityRegistry?.All;
            if (all == null)
            {
                _log?.Error(Source, "AbilityRegistrySO not injected; GEAR UP has no moves to offer.");
                AddRegistryNote(_commonCards);
                return;
            }

            var flag = AbilityRegistrySO.FlagOf(cls);
            var pool = all.Where(a => a != null && !(a is PassiveAbilitySO) && (a.AllowedClasses & flag) != 0).ToList();
            foreach (var ab in pool.Where(AbilityRegistrySO.IsShared)) AddDeckCard(_commonCards, ab);
            foreach (var ab in pool.Where(a => !AbilityRegistrySO.IsShared(a))) AddDeckCard(_classCards, ab);
            if (pool.Count == 0)
            {
                _log?.Error(Source, $"No pickable moves for {cls} in the registry.");
                AddRegistryNote(_classCards);
            }
        }

        private static void AddRegistryNote(VisualElement host)
        {
            if (host == null) return;
            var note = new Label(UiText.Get(UiKeys.LoadoutRegistryMissing));
            note.AddToClassList("cw-body");
            host.Add(note);
        }

        private void AddDeckCard(VisualElement host, AbilityBaseSO ab)
        {
            if (host == null) return;
            var v = new DeckCardView { Ability = ab, Root = new VisualElement() };
            var card = v.Root;
            card.AddToClassList("cw-ability-card");

            // Gold CardGlowFrame = picked; first child so it draws under the content.
            v.Glow = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Glow.AddToClassList("cw-glow");
            card.Add(v.Glow);

            // Category frame: the card border and its top band wear the category colour; the
            // band names it too, so the category never rests on colour alone.
            var catColor = AbilityPalette.CategoryColor(ab.Category);
            SetBorder(card, catColor);
            var band = new Label(CategoryLabel(ab.Category)) { pickingMode = PickingMode.Ignore };
            band.AddToClassList("cw-ability-card__band");
            band.style.backgroundColor = catColor;
            band.style.color = AbilityPalette.InkOn(catColor);
            card.Add(band);

            var iconHost = new VisualElement { pickingMode = PickingMode.Ignore };
            iconHost.AddToClassList("cw-ability-card__icon");
            PaintIcon(new AbilityIconView(iconHost), ab);
            card.Add(iconHost);
            v.IconHost = iconHost;

            var label = AbilityLabel(ab);
            var name = new Label(label) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("cw-ability-card__name");
            // A name too wide for the card at 30 px (DOPPELGANGER on a 1920 card) steps down a size
            // rather than touch the frame.
            card.RegisterCallback<GeometryChangedEvent>(_ => FitCardName(name, card));
            card.Add(name);

            v.StarterTag = MakeStarterTag();
            card.Add(v.StarterTag);

            // Slot number of a picked card = the in-match button that fires it.
            v.Badge = new Label { pickingMode = PickingMode.Ignore };
            v.Badge.AddToClassList("cw-ability-badge");
            var hex = AbilityPalette.HexColor(ab);
            v.Badge.style.backgroundColor = hex;
            v.Badge.style.color = AbilityPalette.InkOn(hex);
            card.Add(v.Badge);

            // One tap does both jobs: equip into the armed slot AND show the details.
            card.RegisterCallback<ClickEvent>(_ => OnAbilityCardTapped(ab));
            host.Add(card);
            _deckCards.Add(v);
        }

        private void RefreshDeck()
        {
            foreach (var v in _deckCards)
            {
                int slot = _slotModel.SlotOf(v.Ability);
                bool starter = IsPreEquipped(v.Ability);
                v.Root.EnableInClassList("cw-ability-card--picked", slot >= 0);
                v.Root.EnableInClassList("cw-ability-card--locked", starter);
                bool focused = v.Ability == _focusedAbility;
                v.Root.EnableInClassList("cw-ability-card--focused", focused);
                // Focused = ink frame at the same width (the category stays on the band), so the
                // card's content never moves when the focus does.
                SetBorder(v.Root, focused ? Ink : AbilityPalette.CategoryColor(v.Ability.Category));
                v.Badge.text = slot >= 0 ? (slot + 1).ToString() : string.Empty;
                UiElements.SetShown(v.Badge, slot >= 0);
                UiElements.SetShown(v.StarterTag, starter);
            }
        }

        /// <summary>The bottom bar's details: the tapped move's name, category, cooldown and text,
        /// or a prompt until a move is tapped.</summary>
        private void RefreshAbilityDetail()
        {
            if (_abilityDetailText == null) return;
            var ab = _focusedAbility;
            bool has = ab != null;

            if (_abilityDetailName != null)
            {
                _abilityDetailName.text = has ? AbilityLabel(ab) : string.Empty;
                UiElements.SetShown(_abilityDetailName, has);
            }
            if (_abilityDetailCat != null)
            {
                UiElements.SetShown(_abilityDetailCat, has);
                if (has)
                {
                    var catColor = AbilityPalette.CategoryColor(ab.Category);
                    _abilityDetailCat.text = CategoryLabel(ab.Category);
                    _abilityDetailCat.style.backgroundColor = catColor;
                    _abilityDetailCat.style.color = AbilityPalette.InkOn(catColor);
                }
            }
            if (_abilityDetailCd != null)
            {
                UiElements.SetShown(_abilityDetailCd, has);
                if (has)
                {
                    bool shortCd = ab.Cooldown <= 6f;
                    _abilityDetailCd.text = UiText.Get(shortCd ? UiKeys.CooldownShort : UiKeys.CooldownMed);
                    _abilityDetailCd.EnableInClassList("cw-cd-badge--short", shortCd);
                    _abilityDetailCd.EnableInClassList("cw-cd-badge--med", !shortCd);
                }
            }
            _abilityDetailText.text = has ? ab.DescriptionText : UiText.Get(UiKeys.LoadoutDetailEmpty);
            _abilityDetailText.style.color = has ? Ink : InkSoft;
            if (_abilityDetail != null) SetBorder(_abilityDetail, has ? AbilityPalette.CategoryColor(ab.Category) : Ink);
        }

        /// <summary>The Peck (foraging) ability asset, or null if the registry has none.</summary>
        private AbilityBaseSO PeckAbility =>
            _abilityRegistry?.ActiveAbilities.FirstOrDefault(a => a.Is(AbilityJob.Peck));

        /// <summary>True when ANY perk of <paramref name="cls"/> forages (derived from the Peck-slot data
        /// via <see cref="PreEquippedLoadout.ClassForages"/>, so picker and spawner agree).</summary>
        private bool CanForage(ChickenClass cls) =>
            PreEquippedLoadout.ClassForages(cls, _abilityRegistry?.Passives, _abilityRegistry?.All, PeckAbility);

        /// <summary>The class's default (signature) perk, or null if the registry has none.</summary>
        private PassiveAbilitySO DefaultPassiveFor(ChickenClass cls) => _abilityRegistry?.GetDefaultPassiveForClass(cls);

        /// <summary>
        /// The starters <paramref name="passive"/> locks in for <paramref name="cls"/> — its Peck-slot
        /// ability then its Signature-slot ability — via <see cref="PreEquippedLoadout.Resolve"/>, the
        /// same resolution <see cref="SeedForcedAbilities"/> and the spawner use.
        /// </summary>
        private List<AbilityBaseSO> StartersFor(ChickenClass cls, PassiveAbilitySO passive)
        {
            var list = new List<AbilityBaseSO>(2);
            if (passive == null) return list;
            PreEquippedLoadout.Resolve(cls, passive, _abilityRegistry?.All, PeckAbility, out var peckSlot, out var signature);
            if (peckSlot != null) list.Add(peckSlot);
            if (signature != null) list.Add(signature);
            return list;
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

        /// <summary>The resolved Peck-slot and Signature-slot starters for the current selection.</summary>
        private void ResolvePreEquips(out AbilityBaseSO peckSlot, out AbilityBaseSO signature) =>
            PreEquippedLoadout.Resolve(Cls, _selection?.Passive, _abilityRegistry?.All, PeckAbility, out peckSlot, out signature);

        /// <summary>True when <paramref name="slot"/> holds a locked starter: Peck slot 0, signature
        /// 1 (or 0 when there is no Peck slot).</summary>
        private bool IsSlotLocked(int slot)
        {
            if (TryGetKit(out _)) return true;
            ResolvePreEquips(out var peckSlot, out var signature);
            if (peckSlot != null && slot == 0) return true;
            return signature != null && slot == (peckSlot != null ? 1 : 0);
        }

        /// <summary>True when <paramref name="ab"/> is one of the current selection's starters.</summary>
        private bool IsPreEquipped(AbilityBaseSO ab)
        {
            if (ab == null) return false;
            if (TryGetKit(out var kit)) return Array.IndexOf(kit, ab) >= 0;
            ResolvePreEquips(out var peckSlot, out var signature);
            return ab == peckSlot || ab == signature;
        }

        /// <summary>
        /// Puts the perk's starters exactly where <c>MatchBootstrapper.ResolveLegalLoadout</c> forces
        /// them (Peck slot 0, signature next) and keeps the player's other legal picks in order after
        /// them, so what the player composes is what spawns. Starters are locked
        /// (<see cref="IsSlotLocked"/>); anything the class cannot pick is released.
        /// </summary>
        private void SeedForcedAbilities()
        {
            if (_selection == null) return;

            if (TryGetKit(out var kit))
            {
                for (int i = 0; i < ActiveSlotsForClass; i++) SetEquipped(i, kit[i]);
                return;
            }

            PreEquippedLoadout.Resolve(Cls, _selection.Passive, _abilityRegistry?.All, PeckAbility, out var peckSlot, out var signature);

            var others = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++)
            {
                var eq = GetEquipped(i);
                if (eq == null || eq == peckSlot || eq == signature || others.Contains(eq)) continue;
                if (!AbilityRegistrySO.IsAllowedFor(eq, Cls)) continue;
                others.Add(eq);
            }

            var layout = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (peckSlot != null) layout.Add(peckSlot);
            if (signature != null) layout.Add(signature);
            layout.AddRange(others);

            for (int i = 0; i < ActiveSlotsForClass; i++)
                SetEquipped(i, i < layout.Count ? layout[i] : null);
        }

        /// <summary>READY gates on all four slots filled + a perk; otherwise it counts down the picks.</summary>
        private void RefreshEquippedState()
        {
            int missing = _slotModel.Missing;
            bool ready = missing == 0 && _selection?.Passive != null;

            if (_readyBtn != null)
            {
                _readyBtn.text = ready
                    ? UiText.Get(UiKeys.BtnReady)
                    : (missing == 1
                        ? UiText.Get(UiKeys.BtnPickMoreOne)
                        : UiText.Format(UiKeys.BtnPickMoreMany, ("n", missing)));
                _readyBtn.SetEnabled(ready);
                _readyBtn.EnableInClassList("cw-btn--green", ready);
                _readyBtn.EnableInClassList("cw-btn--neutral", !ready);
            }
        }

        private void OnReady()
        {
            if (_isBusy) return;
            SaveLastSetup();
            // The stamp slams in when THE COOP is on screen (OnPageShown) and the Ready cue lands on
            // its hit frame (or at once under Reduced Motion). RefreshLobby reads this flag first.
            _stampPending = true;
            _lobbyViaGearUp = true;
            ShowLobby();
        }


    }
}
