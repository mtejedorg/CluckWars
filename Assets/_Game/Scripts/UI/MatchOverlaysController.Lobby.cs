using CluckWars.Injection;
using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using CluckWars.Settings;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;


namespace CluckWars.UI
{
    /// <summary>
    /// <see cref="MatchOverlaysController"/>'s the waiting room (A21, architecture audit 2026-11-12).
    /// </summary>
    /// <remarks>
    /// The seat grid, the join-code tiles, the host's START and the two ways out of it. Shown before the
    /// match in a multiplayer session; the solo path never sees it.
    /// <para>
    /// One component, four files. The scene references this type by name and every serialized field and
    /// bound element reference has to stay on it, so the split is a partial class, never a second
    /// component: the main file keeps the services, the element references, the lifecycle, the binding and
    /// the coordinator (<c>Update</c> decides which overlay is up and calls one Refresh per frame). Each
    /// overlay file owns only its own members.
    /// </para>
    /// </remarks>
    public sealed partial class MatchOverlaysController
    {
        /// <summary>Waiting room LEAVE (and Esc / Android back): the same exit as BACK TO LOBBY, with the back cue.</summary>
        private void OnLobbyLeave()
        {
            if (_leavingToLobby) return;
            _audio.Back();
            LeaveSession(changeBird: false, "LEAVE");
        }

        /// <summary>Waiting room CHANGE BIRD: leave the session and open the menu on PICK YOUR BIRD.</summary>
        private void OnLobbyChangeBird()
        {
            if (_leavingToLobby) return;
            _audio.Tap();
            LeaveSession(changeBird: true, "CHANGE BIRD");
        }

        private void RefreshLobby(GameManager gm, bool poll)
        {
            bool show = gm != null && gm.State == MatchState.WaitingForPlayers;
            // Fill on the frame it opens, not on the next poll: its TIME / GOAL / status labels are
            // authored blank (bound from MatchConfig here), so a late fill would flash empty rows.
            bool opening = show && !IsShown(_lobbyOverlay);
            UiElements.SetShown(_lobbyOverlay, show);
            if (!show) return;

            if (!(poll || opening)) return;

            var runner = _network?.Runner;
            bool solo = runner != null && runner.GameMode == GameMode.Single;
            bool isHost = runner != null && (solo || runner.IsSharedModeMasterClient);
            int maxPlayers = _matchConfig != null ? _matchConfig.MaxPlayers : 4;

            // Invite code: only a hosted session has anyone to invite, so never in solo.
            bool showInvite = isHost && !solo;
            UiElements.SetShown(_lobbyInviteCard, showInvite);
            if (showInvite) SetCodeTiles(_selection?.SessionName);

            // House rules.
            if (_lobbySettingTime != null && _matchConfig != null)
                _lobbySettingTime.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds);
            if (_lobbySettingGoal != null && _matchConfig != null)
                _lobbySettingGoal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin);

            var master = gm.Object != null && gm.Object.IsValid ? gm.Object.StateAuthority : PlayerRef.None;
            RefreshSeats(Mathf.Clamp(maxPlayers, 1, _seats.Length), solo, master);

            // Status pill: READY seats over the room size (the menu Coop's count and colours).
            int ready = WaitingRoomRules.ReadyCount(_seatFilled);
            bool full = ready >= Mathf.Clamp(maxPlayers, 1, _seats.Length);
            if (_lobbyStatusCount != null)
                _lobbyStatusCount.text = UiText.Format(UiKeys.LobbyCount, ("n", ready), ("max", maxPlayers));
            if (_lobbyStatusText != null)
            {
                _lobbyStatusText.text = UiText.Get(solo ? UiKeys.LobbyStatusSolo : full ? UiKeys.LobbyAllReady : UiKeys.LobbyStatusWaiting);
                // Dark green / dark amber clear 4.5:1 on the cream inset; the dot is a lamp, not text.
                _lobbyStatusText.style.color = full ? StatusInkReady : StatusInkWaiting;
            }
            if (_lobbyStatusDot != null) _lobbyStatusDot.style.backgroundColor = full ? StatusLampReady : StatusLampWaiting;

            // START (host only) + the host / guest line.
            UiElements.SetShown(_lobbyStartBtn, isHost);
            if (_lobbyHint != null)
                _lobbyHint.text = UiText.Get(solo ? UiKeys.LobbyHintSolo : isHost ? UiKeys.LobbyHintHost : UiKeys.LobbyHintGuest);
        }

        private static readonly Color StatusInkReady    = new Color32(0x1e, 0x6a, 0x1e, 0xff);

        private static readonly Color StatusInkWaiting  = new Color32(0x6e, 0x48, 0x00, 0xff);

        private static readonly Color StatusLampReady   = new Color32(0x4a, 0xe6, 0x6a, 0xff);

        private static readonly Color StatusLampWaiting = new Color32(0xf5, 0xc8, 0x42, 0xff);

        /// <summary>Builds the four seats once (THE COOP's .cw-seat: pedestal + chicken, nameplate, READY badge).</summary>
        private void BuildSeats()
        {
            _lobbyGrid.Clear();
            for (int corner = 0; corner < _seats.Length; corner++)
            {
                var color = PlayerPalette.ForCorner(corner);
                var v = new SeatView { Root = new VisualElement() };
                v.Root.AddToClassList("cw-seat");

                var stage = new VisualElement { pickingMode = PickingMode.Ignore };
                stage.AddToClassList("cw-seat__stage");
                v.Pedestal = new VisualElement { pickingMode = PickingMode.Ignore };
                v.Pedestal.AddToClassList("cw-seat__pedestal");
                v.Pedestal.style.unityBackgroundImageTintColor = color;   // Pedestal_Ring is white art
                v.Pedestal.Add(new DashedRim());   // shown on an empty seat only (.cw-seat--empty)
                v.Chicken = new VisualElement { pickingMode = PickingMode.Ignore };
                v.Chicken.AddToClassList("cw-chicken");
                v.Chicken.AddToClassList("cw-seat__chicken");
                v.PedestalTag = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1))) { pickingMode = PickingMode.Ignore };
                v.PedestalTag.AddToClassList("cw-seat__pedestal-tag");
                v.PedestalTag.style.backgroundColor = color;
                v.PedestalTag.style.color = AbilityPalette.InkOn(color);
                stage.Add(v.Pedestal); stage.Add(v.Chicken); stage.Add(v.PedestalTag);
                v.Root.Add(stage);

                v.Plate = new VisualElement();
                v.Plate.AddToClassList("cw-seat__plate");
                v.Plate.style.borderTopColor = color;
                var glow = new VisualElement { pickingMode = PickingMode.Ignore };
                glow.AddToClassList("cw-glow");
                v.Plate.Add(glow);

                v.NameRow = new VisualElement();
                v.NameRow.AddToClassList("cw-seat__namerow");
                v.Name = new Label();
                v.Name.AddToClassList("cw-seat__name");
                v.Pn = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1)));
                v.Pn.AddToClassList("cw-seat__pn");
                v.Pn.style.borderLeftColor = color;   // player colour as the tag's stripe; text stays cream-on-ink
                v.Tag = new Label();
                v.Tag.AddToClassList("cw-seat__tag");
                v.NameRow.Add(v.Name); v.NameRow.Add(v.Pn); v.NameRow.Add(v.Tag);
                v.Plate.Add(v.NameRow);

                v.ClassLine = new Label();
                v.ClassLine.AddToClassList("cw-seat__class");
                v.Plate.Add(v.ClassLine);

                v.Waiting = new Label(UiText.Format(UiKeys.LobbyWaitingFor, ("n", corner + 1)));
                v.Waiting.AddToClassList("cw-seat__waiting");
                v.Plate.Add(v.Waiting);

                // The bird's four moves, as on the Coop's seat cards (round-3 finding 12).
                v.Hexes = new VisualElement();
                v.Hexes.AddToClassList("cw-seat__hexes");
                for (int i = 0; i < AbilityController.SlotCount; i++)
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

                // READY: gold rosette badge, ink label (~10.6:1; was cream on green, 3.0:1).
                v.State = new VisualElement();
                v.State.AddToClassList("cw-seat__state");
                var rosette = new VisualElement { pickingMode = PickingMode.Ignore };
                rosette.AddToClassList("cw-seat__rosette");
                v.State.Add(rosette);
                v.StateLabel = new Label(UiText.Get(UiKeys.StateReady));
                v.StateLabel.AddToClassList("cw-seat__state-label");
                v.State.Add(v.StateLabel);
                v.Plate.Add(v.State);

                v.Root.Add(v.Plate);
                _seats[corner] = v;
                _lobbyGrid.Add(v.Root);
            }
        }

        /// <summary>
        /// Fills each corner's seat from the chicken standing on it: "You" / CPU name / "P{n}", HOST on the
        /// master's seat (never in solo), CPU on a bot, "{CLASS} · {role}" like the menu Coop, READY. A corner
        /// with no chicken is an open seat ("WAITING FOR P{n}"); seats past the room size are hidden.
        /// </summary>
        private void RefreshSeats(int seatCount, bool solo, PlayerRef master)
        {
            int localCorner = LocalCorner();
            for (int corner = 0; corner < _seats.Length; corner++)
            {
                var v = _seats[corner];
                if (v == null) continue;
                bool inRoom = corner < seatCount;
                v.Root.style.display = inRoom ? DisplayStyle.Flex : DisplayStyle.None;
                var chicken = inRoom ? ChickenForCorner(corner) : null;
                bool filled = chicken != null;
                _seatFilled[corner] = filled;

                v.Root.EnableInClassList("cw-seat--empty", !filled);
                v.Root.EnableInClassList("cw-seat--you", filled && corner == localCorner);
                v.Name.EnableInClassList(PlayerPalette.YouMarkClass, filled && corner == localCorner);
                string css = filled ? "cw-chicken--" + KeyOf(chicken.Class) : null;
                if (v.ChickenCss != css)
                {
                    if (v.ChickenCss != null) v.Chicken.RemoveFromClassList(v.ChickenCss);
                    if (css != null) v.Chicken.AddToClassList(css);
                    v.ChickenCss = css;
                }

                UiElements.SetShown(v.NameRow, filled);
                UiElements.SetShown(v.ClassLine, filled);
                UiElements.SetShown(v.Hexes, filled);
                UiElements.SetShown(v.State, filled);
                UiElements.SetShown(v.Waiting, !filled);
                if (!filled) continue;

                var cls = chicken.Class;
                v.Name.text = MatchStandings.DisplayName(corner == localCorner, chicken.IsBot, cls, corner);
                bool host = WaitingRoomRules.ShowHostTag(solo, master != PlayerRef.None && chicken.Object.InputAuthority == master, chicken.IsBot);
                v.Tag.text = host ? UiText.Get(UiKeys.TagHost) : chicken.IsBot ? UiText.Get(UiKeys.TagCpu) : string.Empty;
                UiElements.SetShown(v.Tag, host || chicken.IsBot);
                v.ClassLine.text = MatchStandings.ClassRoleLine(cls);
                PaintSeatLoadout(v, chicken);
            }
        }

        /// <summary>The seat's four mini-hexes from the chicken's equipped moves (set on every peer before spawn).</summary>
        private static void PaintSeatLoadout(SeatView v, ChickenController chicken)
        {
            var abilities = chicken != null ? chicken.GetComponent<AbilityController>() : null;
            for (int i = 0; i < v.HexViews.Count; i++)
            {
                var ab = abilities != null ? abilities.GetSlot(i) : null;
                var (hex, icon) = v.HexViews[i];
                hex.style.unityBackgroundImageTintColor = ab != null ? AbilityPalette.HexColor(ab) : AbilityPalette.EmptyHex;
                AbilityIconPainter.Paint(icon, ab, disc: false);
            }
        }

        private void SetCodeTiles(string code)
        {
            if (_codeTiles == null) return;
            CodeTileFit.Fill(_codeTiles, code);   // shrinks / wraps a long code instead of running under SHARE / COPY
        }

        private void OnLobbyStart()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            _log?.Info(Source, "Lobby Start → GameManager.StartMatchNow().");
            gm.StartMatchNow();
        }

        private void CopyJoinCode()
        {
            var code = _selection?.SessionName;
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;
            _log?.Debug(Source, $"Copied join code '{code}' to clipboard.");
        }

    }
}
