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
    /// <see cref="MenuUiController"/>: THE COOP: hosting and joining, the seat grid, the join code and the bot roster (A21, architecture audit 2026-11-12).
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
        //  THE COOP
        // ======================================================================
        // Seat colours come from PlayerPalette by the corner each seat will spawn on (CornerAssignment),
        // so "You" is the same colour here, in the HUD and on the podium (round-2 finding 1).
        private int[] _lobbyPermutation;   // null = corners not known yet (a guest, or a host before its code)
        // Sample CPU bots shown in the Solo lobby (you always spawn vs 3 bots).
        private static readonly ChickenClass[] BotClasses = ChickenClasses.SoloBotRoster;

        private string _joinCode = string.Empty;

        private sealed class SeatView
        {
            public VisualElement Root, Pedestal, Chicken, Plate, NameRow, Hexes, State;
            public Label Name, Pn, Tag, ClassLine, Waiting, StateLabel, PedestalTag;
            public readonly List<(VisualElement Hex, AbilityIconView Icon)> HexViews = new();
            public string ChickenCss;
        }

        private void BuildLobby()
        {
            Bind<TextField>(_lobby, "LobbyJoinField", f =>
            {
                f.maxLength = JoinCodeEntry.MaxLength;
                f.RegisterValueChangedCallback(evt =>
                {
                    string upper = (evt.newValue ?? string.Empty).ToUpperInvariant();
                    if (upper != evt.newValue) f.SetValueWithoutNotify(upper);
                    if (_selection != null && _selection.Mode == SessionMode.Join) UpdateLobbyStatus(false, false, true);
                });
                // Keyboard Enter / Done submits like JOIN MATCH, only once the code is whole (round-2
                // finding 10). TrickleDown: the field's own input child sees the key first otherwise.
                f.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.character != '\n') return;
                    evt.StopPropagation();
                    if (_selection != null && _selection.Mode == SessionMode.Join && JoinCodeEntry.IsComplete(f.value))
                        OnStartMatch();
                }, TrickleDown.TrickleDown);
            });
            Bind<Button>(_lobby, "BackBtn",  b => { _lobbyBackBtn = b; b.clicked += OnLobbyBack; BackCue(b); });
            Bind<Button>(_lobby, "StartBtn", b => { b.clicked += OnStartMatch; OwnCue(b); });
            Bind<Button>(_lobby, "CopyBtn",  b => b.clicked += CopyJoinCode);
            Bind<Button>(_lobby, "ShareBtn", b => b.clicked += CopyJoinCode);
            _readyBanner = _lobby.Q<VisualElement>("ReadyBanner");
            _getReadyCard = _lobby.Q<VisualElement>("GetReadyCard");
            if (_getReadyCard == null)
                _log?.Error(Source, "Lobby.uxml has no #GetReadyCard; START MATCH will load with no GET READY card over the menu.");

            var grid = _lobby.Q<VisualElement>("PlayerGrid");
            if (grid == null)
            {
                _log?.Error(Source, "Lobby.uxml has no #PlayerGrid; THE COOP cannot show the lineup.");
                return;
            }
            grid.Clear();
            for (int i = 0; i < _seats.Length; i++)
            {
                _seats[i] = BuildSeat(i);
                grid.Add(_seats[i].Root);
            }
        }

        /// <summary>Builds one lineup seat once: pedestal + chicken, nameplate, four move hexes, ready badge.</summary>
        private SeatView BuildSeat(int idx)
        {
            var v = new SeatView { Root = new VisualElement() };
            v.Root.AddToClassList("cw-seat");

            var stage = new VisualElement { pickingMode = PickingMode.Ignore };
            stage.AddToClassList("cw-seat__stage");
            v.Pedestal = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Pedestal.AddToClassList("cw-seat__pedestal");
            v.Pedestal.Add(new DashedRim());   // shown on an empty seat only (.cw-seat--empty)
            v.Chicken = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Chicken.AddToClassList("cw-chicken");
            v.Chicken.AddToClassList("cw-seat__chicken");
            // P# on the pedestal in the player colour: tells two birds of the same class apart.
            // Text and colours are the seat's corner, set on every fill (SetSeatCorner).
            v.PedestalTag = new Label { pickingMode = PickingMode.Ignore };
            v.PedestalTag.AddToClassList("cw-seat__pedestal-tag");
            stage.Add(v.Pedestal); stage.Add(v.Chicken); stage.Add(v.PedestalTag);
            v.Root.Add(stage);

            v.Plate = new VisualElement();
            v.Plate.AddToClassList("cw-seat__plate");
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.AddToClassList("cw-glow");
            v.Plate.Add(glow);

            v.NameRow = new VisualElement();
            v.NameRow.AddToClassList("cw-seat__namerow");
            v.Name = new Label();
            v.Name.AddToClassList("cw-seat__name");
            v.Pn = new Label();
            v.Pn.AddToClassList("cw-seat__pn");
            v.Tag = new Label();
            v.Tag.AddToClassList("cw-seat__tag");
            v.NameRow.Add(v.Name); v.NameRow.Add(v.Pn); v.NameRow.Add(v.Tag);
            v.Plate.Add(v.NameRow);

            v.ClassLine = new Label();
            v.ClassLine.AddToClassList("cw-seat__class");
            v.Plate.Add(v.ClassLine);

            v.Waiting = new Label();
            v.Waiting.AddToClassList("cw-seat__waiting");
            v.Plate.Add(v.Waiting);

            v.Hexes = new VisualElement();
            v.Hexes.AddToClassList("cw-seat__hexes");
            for (int i = 0; i < ActiveSlotsForClass; i++)
            {
                var hex = new VisualElement();
                hex.AddToClassList("cw-mini-hex");
                var iconHost = new VisualElement { pickingMode = PickingMode.Ignore };
                iconHost.AddToClassList("cw-mini-hex__icon");
                hex.Add(iconHost);
                v.Hexes.Add(hex);
                v.HexViews.Add((hex, new AbilityIconView(iconHost)));
            }
            v.Plate.Add(v.Hexes);

            v.State = new VisualElement();
            v.State.AddToClassList("cw-seat__state");
            var rosette = new VisualElement { pickingMode = PickingMode.Ignore };
            rosette.AddToClassList("cw-seat__rosette");
            v.State.Add(rosette);
            v.StateLabel = new Label();
            v.StateLabel.AddToClassList("cw-seat__state-label");
            v.State.Add(v.StateLabel);
            v.Plate.Add(v.State);

            v.Root.Add(v.Plate);
            return v;
        }

        /// <summary>
        /// Paints a seat in the player colour of the <paramref name="corner"/> it will spawn on (P{corner+1} on
        /// the pedestal and the name tag, the plate's top edge, the tag stripe), or neutral with no P# when the
        /// corner is not known yet (-1).
        /// </summary>
        private static void SetSeatCorner(SeatView v, int corner)
        {
            var color = PlayerPalette.ForCorner(corner);
            bool known = corner >= 0;
            string tag = known ? UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1)) : string.Empty;
            v.Pedestal.style.unityBackgroundImageTintColor = color;   // Pedestal_Ring is white art
            v.PedestalTag.text = tag;
            v.PedestalTag.style.backgroundColor = color;
            v.PedestalTag.style.color = AbilityPalette.InkOn(color);
            v.PedestalTag.style.visibility = known ? Visibility.Visible : Visibility.Hidden;
            v.Plate.style.borderTopColor = color;
            v.Pn.text = tag;
            v.Pn.style.borderLeftColor = color;   // player colour as the tag's stripe: text stays cream-on-ink (~15:1)
            UiElements.SetShown(v.Pn, known);
            v.Waiting.text = known
                ? UiText.Format(UiKeys.LobbyWaitingFor, ("n", corner + 1))
                : UiText.Get(UiKeys.LobbyWaitingForPlayer);
        }

        /// <summary>Fills one seat. <paramref name="name"/> null = an open seat (waiting for a player).
        /// <paramref name="corner"/> = the spawn corner it will get (-1 = not known yet).</summary>
        private void SetSeat(int idx, int corner, ChickenClass cls, string name, bool isHost, bool cpu, bool ready, IReadOnlyList<AbilityBaseSO> abilities, bool showState = true)
        {
            var v = _seats[idx];
            if (v == null) return;
            bool empty = name == null;
            bool you = idx == 0 && !empty;
            v.Root.EnableInClassList("cw-seat--empty", empty);
            v.Root.EnableInClassList("cw-seat--you", you);
            v.Name.EnableInClassList(PlayerPalette.YouMarkClass, you);
            SetSeatCorner(v, corner);

            if (!string.IsNullOrEmpty(v.ChickenCss)) v.Chicken.RemoveFromClassList(v.ChickenCss);
            v.ChickenCss = empty ? null : "cw-chicken--" + KeyOf(cls);
            if (v.ChickenCss != null) v.Chicken.AddToClassList(v.ChickenCss);
            _seatClasses[idx] = empty ? null : cls;
            ShowSeatOnStage(idx);

            UiElements.SetShown(v.NameRow, !(empty));
            UiElements.SetShown(v.ClassLine, !(empty));
            UiElements.SetShown(v.Hexes, !(empty));
            UiElements.SetShown(v.Waiting, empty);
            // No READY / PICKING pill until the seat is really taken: an open seat, and a guest's own seat
            // before they have joined the host (WaitingRoomRules.ShowsStatePill).
            UiElements.SetShown(v.State, !(empty || !showState));
            if (empty) return;

            v.Name.text = name;
            v.Tag.text = isHost ? UiText.Get(UiKeys.TagHost) : cpu ? UiText.Get(UiKeys.TagCpu) : string.Empty;
            UiElements.SetShown(v.Tag, isHost || cpu);
            v.ClassLine.text = UiText.Format(UiKeys.LobbyClassLine, ("cls", ClassShortName(cls)), ("role", RoleName(cls)));

            for (int i = 0; i < v.HexViews.Count; i++)
            {
                var ab = abilities != null && i < abilities.Count ? abilities[i] : null;
                var (hex, icon) = v.HexViews[i];
                hex.style.unityBackgroundImageTintColor = ab != null ? AbilityPalette.HexColor(ab) : AbilityPalette.EmptyHex;
                PaintIcon(icon, ab, disc: false);
            }

            v.State.EnableInClassList("cw-seat__state--picking", !ready);
            v.StateLabel.text = UiText.Get(ready ? UiKeys.StateReady : UiKeys.StatePicking);
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
            if (inviteCard != null) UiElements.SetShown(inviteCard, isHost);
            if (joinCard   != null) { joinCard.RemoveFromClassList("cw-hidden"); UiElements.SetShown(joinCard, isJoin); }
            if (status != null) status.text = string.Empty;

            // The corners this lineup will spawn on (CornerAssignment): solo rolls a fresh layout each time
            // THE COOP opens; a host's is known once its join code exists (below); a guest's is not known.
            if (isSolo)
            {
                _selection.SoloCornerSeed = CluckWars.Diagnostics.DevSeed.TryGet(out int devSeed) ? devSeed : new System.Random().Next();
                _lobbyPermutation = CornerAssignment.Permutation(
                    CornerAssignment.SeedFor(SessionMode.Solo, null, _selection.SoloCornerSeed));
            }
            else _lobbyPermutation = null;

            bool allReady = RefreshLineup(isSolo, isHost);
            RevealReadyBanner(allReady);
            UpdateLobbyStatus(isSolo, isHost, isJoin);
            RefreshMatchSettings();

            // Host pre-creates the UGS lobby so its join code populates the tiles before START.
            if (isHost)
            {
                _joinCode = string.Empty;
                SetCodeTiles(UiText.Get(UiKeys.LobbyCodePlaceholder));
                try
                {
                    var info = await _ugs.CreateLobbyAsync("CluckWars Match", 4);
                    _joinCode = info.JoinCode;
                    _selection.SessionName = info.JoinCode;
                    SetCodeTiles(info.JoinCode);
                    // The code is the session name, which seeds the corners: colour the seats now
                    // (unless the player already left for another mode while the lobby was being made).
                    if (_selection.Mode == SessionMode.Host)
                    {
                        _lobbyPermutation = CornerAssignment.Permutation(
                            CornerAssignment.SeedFor(SessionMode.Host, info.JoinCode, 0));
                        RefreshLineup(isSolo: false, isHost: true);
                    }
                }
                catch (Exception e)
                {
                    _log?.Error(Source, $"CreateLobby failed: {e.Message}");
                    if (status != null) { status.style.color = UiGfx.Hex32("ff786e"); status.text = UiText.Get(UiKeys.LobbyErrCreate); }
                }
            }
        }

        /// <summary>Fills the four seats; returns true when every seat is taken and ready.</summary>
        private bool RefreshLineup(bool isSolo, bool isHost)
        {
            var mine = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++) { var a = GetEquipped(i); if (a != null) mine.Add(a); }
            var mode = _selection != null ? _selection.Mode : SessionMode.Solo;
            SetSeat(0, CornerAssignment.LobbySeatCorner(mode, 0, _lobbyPermutation), Cls, UiText.Get(UiKeys.LabelYou), isHost, false, ready: true, mine,
                showState: WaitingRoomRules.ShowsStatePill(mode, seat: 0));

            // Fixed: the lineup the spawner will roll from the same seed (FixedKit.RollBotLineup), kits and all.
            var lineup = isSolo && LoadoutModeNow == LoadoutMode.Fixed
                ? FixedKit.RollBotLineup(_selection != null ? _selection.SoloCornerSeed : 0, _seats.Length - 1,
                    _abilityRegistry?.Passives, _selection?.DevBotSubclasses)
                : null;

            bool allReady = true;
            for (int i = 1; i < _seats.Length; i++)
            {
                if (isSolo)
                {
                    var rolled = lineup?[i - 1];
                    var bc = rolled != null ? rolled.Subclass.ClassOf() : BotClasses[i - 1];
                    var kit = rolled != null && FixedKit.TryResolve(LoadoutMode.Fixed, rolled, out var rolledKit, out _) ? rolledKit : null;
                    SetSeat(i, CornerAssignment.LobbySeatCorner(mode, i, _lobbyPermutation), bc, MatchStandings.DisplayName(false, true, bc, i), false, true, ready: true,
                        (IReadOnlyList<AbilityBaseSO>)kit ?? SampleBotAbilities(bc, i));
                }
                else
                {
                    SetSeat(i, CornerAssignment.LobbySeatCorner(mode, i, _lobbyPermutation), ChickenClass.Warrior, null, false, false, ready: false, null);
                    allReady = false;
                }
            }
            OrderSeatsByCorner(mode);
            return allReady;
        }

        /// <summary>
        /// Lays the Coop's seats out by spawn corner, P1..P4 left to right (round-3 finding 6): seats are FILLED in
        /// join order (you, then the others), but the cards carry corner labels, so the row is re-ordered to match.
        /// A guest's corners are unknown: seat order. Only moves cards when the order actually changes.
        /// </summary>
        private void OrderSeatsByCorner(SessionMode mode)
        {
            var grid = _seats[0]?.Root.parent;
            if (grid == null) return;
            var corners = new int[_seats.Length];
            for (int i = 0; i < corners.Length; i++) corners[i] = CornerAssignment.LobbySeatCorner(mode, i, _lobbyPermutation);
            var order = CornerAssignment.LobbySeatOrder(corners);
            bool inOrder = true;
            for (int pos = 0; pos < order.Length; pos++)
                if (grid.IndexOf(_seats[order[pos]].Root) != pos) { inOrder = false; break; }
            if (inOrder) return;
            for (int pos = 0; pos < order.Length; pos++) grid.Insert(pos, _seats[order[pos]].Root);
        }

        /// <summary>
        /// Shows the READY! banner when every seat is ready: a one-shot pop-in (scale + fade, USS
        /// transition) each time it appears; instant under reduced motion. Hidden otherwise. When
        /// the player just pressed READY the stamp (<see cref="PlayReadyStamp"/>) reveals it instead.
        /// </summary>
        private void RevealReadyBanner(bool show)
        {
            _lobbyAllReady = show;
            if (_readyBanner == null) return;
            _readyBanner.style.opacity = StyleKeyword.Null;   // a hold-then-fade stamp may have left it at 0
            if (!show)
            {
                UiElements.SetShown(_readyBanner, false);
                return;
            }
            UiElements.SetShown(_readyBanner, true);
            if (PlayerPreferences.ReducedMotionEnabled)
            {
                _readyBanner.RemoveFromClassList("cw-ready-banner--hidden");
                return;
            }
            _readyBanner.AddToClassList("cw-ready-banner--hidden");
            if (_stampPending) return;   // the stamp reveals it when the page is on screen
            // After the page has slid in, release the hidden state so the transition plays.
            _readyBanner.schedule.Execute(() => _readyBanner.RemoveFromClassList("cw-ready-banner--hidden"))
                        .ExecuteLater(PageOutMs + 260);
        }

        /// <summary>
        /// READY pressed -> THE COOP: the READY! ribbon slams in (scale 1.6 -> 1, a slight rotation)
        /// and stamps. It is the existing banner, not a second element: the ribbon is the biggest
        /// "READY" on the page, already carries the copy, and a seal-shaped rosette would only
        /// duplicate the small one on the player's own seat. Solo (every seat ready) it stays, as
        /// before; with open seats it holds for a moment and fades. The Ready cue, dust and sparkles
        /// fire on the hit frame. Reduced Motion: no slam, the cue plays at once.
        /// </summary>
        private void PlayReadyStamp()
        {
            if (_readyBanner == null || _juice == null) { _audio.Ready(); return; }
            var previousDisplay = _readyBanner.style.display;
            UiElements.SetShown(_readyBanner, true);
            _readyBanner.RemoveFromClassList("cw-ready-banner--hidden");
            if (!_juice.Stamp(_readyBanner, MenuJuicePolicy.StampStartDelaySeconds, OnStampHit))
            {
                _readyBanner.style.display = previousDisplay;
                _audio.Ready();
                return;
            }
            if (_lobbyAllReady) return;

            int id = ++_stampHideId;
            long holdMs = (long)((MenuJuicePolicy.StampStartDelaySeconds + MenuJuicePolicy.StampSeconds + MenuJuicePolicy.StampHoldSeconds) * 1000f);
            _readyBanner.schedule.Execute(() =>
            {
                if (id != _stampHideId) return;
                _readyBanner.style.opacity = 0f;   // fades through the banner's own opacity transition
                _readyBanner.schedule.Execute(() =>
                {
                    if (id != _stampHideId) return;
                    UiElements.SetShown(_readyBanner, false);
                    _readyBanner.style.opacity = StyleKeyword.Null;
                }).ExecuteLater(400);
            }).ExecuteLater(holdMs);
        }

        /// <summary>Stamp lands: the Ready cue, and dust + sparkles thrown from the label's bottom
        /// edge (not its centre), so the hit frame never covers the word.</summary>
        private void OnStampHit()
        {
            _audio.Ready();
            if (_readyBanner == null || _juice == null) return;
            var b = _readyBanner.worldBound;
            if (float.IsNaN(b.x) || float.IsNaN(b.height) || b.width <= 0f) return;   // not laid out: no anchor
            var foot = new Vector2(b.center.x, b.yMax);
            _juice.Burst(foot, ParticleKind.Dust, 5, 110f);
            _juice.Burst(foot, ParticleKind.Sparkle, 5, 150f);
        }

        /// <summary>
        /// Paints the TIME / GOAL chips from the live <see cref="MatchConfigSO"/>, so the lobby
        /// advertises the rules the match will actually enforce. (ARENA and MODE stay authored.)
        /// </summary>
        private void RefreshMatchSettings()
        {
            if (_matchConfig == null)
            {
                // Only reachable when nothing installed the project container (EditMode / headless).
                _log?.Warn(Source, "MatchConfig not injected; lobby TIME and GOAL are blank.");
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
            int seats = 4;
            var codeState = isJoin ? JoinCodeEntry.StateOf(_lobby.Q<TextField>("LobbyJoinField")?.value) : JoinCodeState.Empty;
            bool codeComplete = codeState == JoinCodeState.Complete;
            if (isSolo)      { c = UiText.Format(UiKeys.LobbyCount, ("n", seats), ("max", seats)); t = UiText.Get(UiKeys.LobbyStatusSolo);    dotColor = UiGfx.Hex32("4ae66a"); readyish = true;  }
            else if (isHost) { c = UiText.Format(UiKeys.LobbyCount, ("n", 1),     ("max", seats)); t = UiText.Get(UiKeys.LobbyStatusWaiting); dotColor = UiGfx.Gold;             readyish = false; }
            // Join: the seat count is unknown until the lobby answers, so the pill shows no count
            // at all (it used to print a bare "-") - just what the player has to do.
            else             { c = string.Empty; t = UiText.Get(JoinCodeEntry.PillKey(codeState)); dotColor = codeComplete ? UiGfx.Hex32("4ae66a") : UiGfx.Gold; readyish = codeComplete; }

            if (count != null)
            {
                count.text = c;
                UiElements.SetShown(count, !(string.IsNullOrEmpty(c)));
            }
            // Dark green / dark amber clear 4.5:1 on the cream inset; the dot is a lamp, not text.
            if (text  != null) { text.text = t; text.style.color = readyish ? UiGfx.Hex32("1e6a1e") : UiGfx.Hex32("6e4800"); }
            if (dot   != null) dot.style.backgroundColor = dotColor;
            if (start != null)
            {
                start.text = UiText.Get(isJoin ? UiKeys.BtnJoinMatch : UiKeys.BtnStart);
                // JOIN MATCH is a disabled wood plank until there is a whole code to join with.
                bool enabled = !isJoin || codeComplete;
                start.SetEnabled(enabled);
                start.EnableInClassList("cw-btn--green", enabled);
                start.EnableInClassList("cw-btn--wood", !enabled);
            }
        }

        private void SetCodeTiles(string code)
        {
            var tiles = _lobby.Q<VisualElement>("CodeTiles");
            if (tiles == null) return;
            CodeTileFit.Fill(tiles, code);
        }

        private void CopyJoinCode()
        {
            if (string.IsNullOrEmpty(_joinCode)) return;
            GUIUtility.systemCopyBuffer = _joinCode;
            var status = _lobby.Q<Label>("LobbyStatus");
            if (status != null) { status.style.color = UiGfx.Gold; status.text = UiText.Format(UiKeys.LobbyCopied, ("code", _joinCode)); }
        }

        /// <summary>
        /// Cosmetic loadout preview for a lobby-only CPU seat. NOT what the bot actually spawns with
        /// (a randomly-rolled preset resolved by <c>MatchBootstrapper.ResolveLegalLoadout</c> in
        /// Game.unity); it only has to have a real loadout's shape: four abilities, all legal for
        /// <paramref name="cls"/>, Peck present iff the class can forage.
        /// </summary>
        private List<AbilityBaseSO> SampleBotAbilities(ChickenClass cls, int seed)
        {
            var list = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (_abilityRegistry == null) return list;

            var peck = PeckAbility;
            if (peck != null && CanForage(cls))
                list.Add(peck);

            // Class abilities first, shared as backfill, rotated by seed so the bots differ.
            var pool = _abilityRegistry.GetClassAbilitiesForClass(cls)
                .Concat(_abilityRegistry.SharedAbilities.Where(a => a != peck))
                .ToList();

            for (int i = 0; i < pool.Count && list.Count < ActiveSlotsForClass; i++)
            {
                var a = pool[(seed + i) % pool.Count];
                if (!list.Contains(a)) list.Add(a);
            }
            return list;
        }

        /// <summary>
        /// The menu-to-match handoff: duck the menu loop to a low bed, sting, fade the GET READY card in over THE
        /// COOP, then load the Game scene in the background (it used to load synchronously and freeze
        /// the menu). The menu stays busy until the scene swaps: no second START, no BACK under the card.
        /// </summary>
        private void LoadMatchScene()
        {
            if (_sceneLoader == null)
            {
                _log?.Error(Source, "No SceneLoader in the menu scene; the match cannot start.");
                return;
            }
            _isBusy = true;
            _audio.DuckMenuMusicForMatch();
            _audio.MatchSting();
            StartCoroutine(ShowGetReadyThenLoad());
        }

        private System.Collections.IEnumerator ShowGetReadyThenLoad()
        {
            if (_getReadyCard != null)
            {
                // Shown this frame at opacity 0, faded in from the next: a class change in the same
                // frame as display:flex would not transition (Reduced Motion zeroes the fade anyway).
                _getReadyCard.AddToClassList(GetReadyHiddenClass);
                UiElements.SetShown(_getReadyCard, true);
                yield return null;
                _getReadyCard.RemoveFromClassList(GetReadyHiddenClass);
            }

            float loadAt = Time.unscaledTime + GetReadyFadeSeconds;
            while (Time.unscaledTime < loadAt) yield return null;

            if (_sceneLoader.LoadNextAsync() != null) yield break;

            // LoadNextAsync already logged why; give the player the menu back with a visible reason.
            if (_getReadyCard != null) UiElements.SetShown(_getReadyCard, false);
            var status = _lobby?.Q<Label>("LobbyStatus");
            if (status != null) status.text = UiText.Get(UiKeys.LobbyErrLoad);
            _audio.StartMenuMusic();
            _isBusy = false;
        }

        private async void OnStartMatch()
        {
            if (_isBusy || _selection == null) return;
            var status = _lobby.Q<Label>("LobbyStatus");

            switch (_selection.Mode)
            {
                case SessionMode.Solo:
                case SessionMode.Host:
                    LoadMatchScene();
                    break;

                case SessionMode.Join:
                    var field = _lobby.Q<TextField>("LobbyJoinField");
                    var code = field?.value?.Trim();
                    if (!JoinCodeEntry.IsComplete(code)) { _audio.Tap(); if (status != null) status.text = UiText.Get(UiKeys.LobbyErrNoCode); return; }
                    _audio.Tap();
                    _isBusy = true;
                    if (status != null) status.text = UiText.Get(UiKeys.LobbyJoining);
                    try
                    {
                        var info = await _ugs.JoinLobbyByCodeAsync(code);
                        _selection.SessionName = info.JoinCode;
                        _isBusy = false;
                        LoadMatchScene(); // re-claims _isBusy until the scene swaps
                    }
                    catch (Exception e)
                    {
                        _autoTestJoinFailures++;
                        _log?.Error(Source, $"JoinByCode failed: {e.Message}");
                        if (status != null) status.text = UiText.Get(UiKeys.LobbyErrJoin);
                        _isBusy = false;
                    }
                    break;
            }
        }


    }
}
