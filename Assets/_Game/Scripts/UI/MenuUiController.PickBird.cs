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
    /// <see cref="MenuUiController"/>: step 1 of the flow, PICK YOUR BIRD: the class tiles, their copy and the preview panel (A21, architecture audit 2026-11-12).
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
        //  STEP 1 — PICK YOUR BIRD
        // ======================================================================
        private sealed class PerkBadgeView
        {
            public Button Root;
            public AbilityIconView Icon;
            public Label Name, Line;
            public PassiveAbilitySO Passive;
        }

        private sealed class StarterChipView
        {
            public VisualElement Root;
            public AbilityIconView Icon;
            public Label Name;
        }

        /// <summary>
        /// Wires the page once: four class tiles (tap = pick that class, keeping its current perk
        /// or its signature perk), the hero, two perk badges and two starter chips. Refreshes only
        /// toggle classes and rewrite labels (<see cref="RefreshClassSelect"/>).
        /// </summary>
        private void BuildClassSelect()
        {
            _classTiles.Clear();
            for (int i = 0; i < Order.Length; i++)
            {
                var cls = Order[i];
                var tile = _classSelect.Q<VisualElement>(ChipNames[i]);
                if (tile == null)
                {
                    _log?.Error(Source, $"CharacterSelectClass.uxml has no #{ChipNames[i]}; {cls} cannot be picked.");
                    continue;
                }
                _classTiles[cls] = tile;
                tile.Q<VisualElement>(ChipNames[i] + "Art")?.AddToClassList("cw-chicken--" + KeyOf(cls));
                // Class colour as the full tile fill, lightened so ink text keeps >= 6.9:1 (pinned by MenuClassTintTests).
                tile.style.backgroundColor = ClassTileFill(cls);
                tile.RegisterCallback<ClickEvent>(_ => OnClassTileTapped(cls));
            }

            _heroStage         = _classSelect.Q<VisualElement>("HeroStage");
            _previewChicken    = _classSelect.Q<VisualElement>("PreviewChicken");
            _previewGlow       = _classSelect.Q<VisualElement>("PreviewGlow");
            _previewName       = _classSelect.Q<Label>("PreviewName");
            _previewQuote      = _classSelect.Q<Label>("PreviewQuote");
            _roleCalloutStrong = _classSelect.Q<Label>("RoleCalloutStrong");
            _roleCalloutWeak   = _classSelect.Q<Label>("RoleCalloutWeak");
            _perkDetail        = _classSelect.Q<Label>("PerkDetail");
            _starterRow        = _classSelect.Q<VisualElement>("StarterRow");

            string[] badgeNames = { "PerkBadgeA", "PerkBadgeB" };
            for (int i = 0; i < _perkBadges.Length; i++)
            {
                var root = _classSelect.Q<Button>(badgeNames[i]);
                if (root == null)
                {
                    _log?.Error(Source, $"CharacterSelectClass.uxml has no #{badgeNames[i]}; that perk cannot be picked.");
                    continue;
                }
                var perkIconHost = root.Q<VisualElement>(className: "cw-perk-badge__icon");
                var view = new PerkBadgeView
                {
                    Root = root,
                    Icon = perkIconHost != null ? new AbilityIconView(perkIconHost) : null,
                    Name = root.Q<Label>(className: "cw-perk-badge__name"),
                    Line = root.Q<Label>(className: "cw-perk-badge__line"),
                };
                _perkBadges[i] = view;
                OwnCue(root);
                root.clicked += () =>
                {
                    if (view.Passive == null) return;
                    bool changed = view.Passive != _selection?.Passive;
                    if (changed) _audio.SelectPerk(); else _audio.Tap();
                    SelectClassAndPassive(Cls, view.Passive);
                    if (changed) _juice?.Pop(view.Root, MenuJuicePolicy.PerkRestScale);
                };
            }

            for (int i = 0; i < _starterChips.Length; i++)
            {
                var root = _classSelect.Q<VisualElement>("StarterChip" + i);
                if (root == null) continue;
                var iconHost = root.Q<VisualElement>(className: "cw-starter-chip__icon");
                _starterChips[i] = new StarterChipView
                {
                    Root = root,
                    Icon = iconHost != null ? new AbilityIconView(iconHost) : null,
                    Name = root.Q<Label>(className: "cw-starter-chip__name"),
                };
            }

            Bind<Button>(_classSelect, "HomeBtn", b => { b.clicked += ShowMainMenu; BackCue(b); });
            // Always enabled — BuildAll seeds a default class + perk, so Step 1 always has a pick.
            Bind<Button>(_classSelect, "NextBtn", b => b.clicked += ShowLoadout);
        }

        /// <summary>A tile picks the class. Re-tapping the current class keeps its perk; a new
        /// class starts on its signature perk (the perk badges then switch it).</summary>
        private void OnClassTileTapped(ChickenClass cls)
        {
            if (_selection == null) return;
            if (cls == _selection.SelectedClass && _selection.Passive != null) { _audio.Tap(); RefreshClassSelect(); return; }
            var passive = DefaultPassiveFor(cls);
            if (passive == null)
            {
                _log?.Error(Source, $"No perk is registered for {cls}; it cannot be picked.");
                return;
            }
            SelectClassAndPassive(cls, passive);
            _audio.SelectClass(cls);
            if (_classTiles.TryGetValue(cls, out var tile))
            {
                _juice?.Pop(tile, MenuJuicePolicy.TileRestScale);
                _juice?.BurstAt(tile, ParticleKind.Sparkle, 4);
            }
        }

        /// <summary>The class's two perks, signature first (registry order would put the alternative
        /// first for some classes, reading as if it were the class's identity).</summary>
        private List<PassiveAbilitySO> PerksFor(ChickenClass cls) =>
            _abilityRegistry?.GetPassivesForClass(cls).OrderByDescending(p => p.IsSignature).ToList()
            ?? new List<PassiveAbilitySO>();

        /// <summary>
        /// Selects a class and its perk together. Old picks are only cleared when the CLASS
        /// changes; switching perk within a class keeps the in-progress loadout.
        /// </summary>
        private void SelectClassAndPassive(ChickenClass cls, PassiveAbilitySO passive)
        {
            if (_selection == null) return;

            bool classChanged = cls != _selection.SelectedClass;
            if (classChanged)
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

            // Seed the perk's starters so the picker shows the loadout the spawner will build.
            SeedForcedAbilities();
            SyncSlotModel();
            _slotModel.AutoArmFirstEmpty();

            RefreshClassSelect();
            if (classChanged) ShowHeroOnStage(hop: true);
        }

        private ChickenClass Cls => _selection?.SelectedClass ?? ChickenClass.Warrior;

        /// <summary>Active-ability slots to fill — four for every class.</summary>
        private int ActiveSlotsForClass => AbilityController.SlotCount;

        private static Color TintOf(ChickenClass cls) => ClassTints[cls];

        /// <summary>The fill of a class tile on PICK YOUR BIRD: the UI class tint, half way to white.</summary>
        public static Color ClassTileFill(ChickenClass cls) => Lighten(TintOf(cls), 0.5f);

        /// <summary>The ink the tile's text is drawn in.</summary>
        public static Color ClassTileInk => Ink;

        private void RefreshClassSelect()
        {
            var cls = Cls;
            var tint = TintOf(cls);

            foreach (var kv in _classTiles)
                kv.Value.EnableInClassList("cw-class-tile--selected", kv.Key == cls);

            if (_previewChicken != null)
            {
                if (!string.IsNullOrEmpty(_previewChickenClass))
                    _previewChicken.RemoveFromClassList(_previewChickenClass);
                _previewChickenClass = "cw-chicken--" + KeyOf(cls);
                _previewChicken.AddToClassList(_previewChickenClass);
            }
            ShowHeroOnStage(hop: false);
            // Painted barn backdrop multiplied by a pale class tint; a near-white soft light on the bird.
            if (_heroStage != null) _heroStage.style.unityBackgroundImageTintColor = Fade(Lighten(tint, 0.5f), 1f);
            if (_previewGlow != null) _previewGlow.style.unityBackgroundImageTintColor = Fade(Lighten(tint, 0.8f), 0.85f);
            // Class name without "CHICKEN", ink on cream.
            if (_previewName != null) _previewName.text = ClassShortName(cls);
            if (_previewQuote != null)
            {
                if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry) && !string.IsNullOrEmpty(entry.LoreQuote))
                    _previewQuote.text = UiText.Format(UiKeys.ClassQuote, ("quote", entry.LoreQuote));
                else
                    _previewQuote.text = "";
                UiElements.SetShown(_previewQuote, !(string.IsNullOrEmpty(_previewQuote.text)));
            }

            RefreshRoleCallouts(cls);
            RefreshPerkBadges(cls);
            RefreshStarterChips(cls, _selection?.Passive);
            // The backdrop on this page is tinted by the selected class.
            if (_currentPage == _classSelect) ApplyBackdrop();
        }

        /// <summary>The two perk badges show the selected class's perks; the selected one is gold,
        /// and #PerkDetail explains it (templated from the passive's fields).</summary>
        private void RefreshPerkBadges(ChickenClass cls)
        {
            var perks = PerksFor(cls);
            if (perks.Count < 2)
                _log?.Warn(Source, $"{cls} has {perks.Count} perk(s) registered; PICK YOUR BIRD expects two.");

            for (int i = 0; i < _perkBadges.Length; i++)
            {
                var b = _perkBadges[i];
                if (b == null) continue;
                var p = i < perks.Count ? perks[i] : null;
                b.Passive = p;
                UiElements.SetShown(b.Root, p != null);
                if (p == null) continue;
                if (b.Name != null) b.Name.text = p.DisplayName.ToUpperInvariant();
                PaintIcon(b.Icon, p);
                // The line is a template filled from the passive's own fields: never a stale number.
                if (b.Line != null) b.Line.text = p.PerkLine(_matchConfig);
                b.Root.EnableInClassList("cw-perk-badge--selected", _selection?.Passive == p);
            }

            if (_perkDetail != null)
            {
                var sel = _selection?.Passive;
                // A perk whose detail template is absent falls back to its line, which the badge
                // already shows: hide the repeat rather than print it twice.
                string detail = sel != null ? sel.PerkDetail(_matchConfig) : string.Empty;
                _perkDetail.text = sel != null && detail == sel.PerkLine(_matchConfig) ? string.Empty : detail;
                UiElements.SetShown(_perkDetail, !(string.IsNullOrEmpty(_perkDetail.text)));
            }
        }

        /// <summary>The selected perk's starters (locked pre-equips), shown once as icon chips.</summary>
        private void RefreshStarterChips(ChickenClass cls, PassiveAbilitySO passive)
        {
            // Fixed: no "starters" among free picks; the whole kit is on GEAR UP, the next page.
            var starters = TryGetKit(out _) ? new List<AbilityBaseSO>() : StartersFor(cls, passive);
            for (int i = 0; i < _starterChips.Length; i++)
            {
                var chip = _starterChips[i];
                if (chip == null) continue;
                var ab = i < starters.Count ? starters[i] : null;
                UiElements.SetShown(chip.Root, ab != null);
                if (ab == null) continue;
                PaintIcon(chip.Icon, ab);
                if (chip.Name != null) chip.Name.text = AbilityLabel(ab);
            }
            if (_starterRow != null)
                UiElements.SetShown(_starterRow, starters.Count > 0);
        }

        /// <summary>
        /// Paints the strong/weak callouts from the dictionary's <c>callout.&lt;class&gt;</c> copy,
        /// hiding a row (glyph included) whose copy is blank.
        /// </summary>
        private void RefreshRoleCallouts(ChickenClass cls)
        {
            var keys = ChickenClasses.Of(cls);
            SetCallout(_roleCalloutStrong, UiText.Get(keys.StrongKey));
            SetCallout(_roleCalloutWeak, UiText.Get(keys.WeakKey));
        }

        private static void SetCallout(Label callout, string text)
        {
            if (callout == null) return;
            bool show = !string.IsNullOrWhiteSpace(text);
            callout.text = show ? text : string.Empty;
            var row = callout.parent ?? callout;
            UiElements.SetShown(row, show);
        }


    }
}
