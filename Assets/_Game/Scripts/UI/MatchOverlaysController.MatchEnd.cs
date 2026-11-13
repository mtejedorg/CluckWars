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
    /// <see cref="MatchOverlaysController"/>'s the match-end overlay (A21, architecture audit 2026-11-12).
    /// </summary>
    /// <remarks>
    /// The podium, the standings rows, the crown and the live chicken stage behind them, plus the two
    /// buttons that leave it (PLAY AGAIN, BACK TO LOBBY).
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
        private static readonly Color StageClear = new Color(0.84f, 0.67f, 0.43f, 1f); // warm barn tone behind the podium

        private SafeAreaPadding _safeArea;

        private void RefreshMatchEnd(GameManager gm)
        {
            bool show = gm != null && gm.State == MatchState.Ended;
            UiElements.SetShown(_matchEndOverlay, show);
            if (!show)
            {
                if (_matchEndPopulated) DisposeStage();
                _matchEndPopulated = false;
                _podiumBedStarted = false;
                _celebration?.Stop();
                return;
            }

            // Scores freeze once the match ends, so build the podium + rows once on entry; only
            // the action buttons track live state (authority can migrate with the host).
            if (!_matchEndPopulated)
            {
                PopulateMatchEnd(gm);
                _matchEndPopulated = true;
                _podiumShownAt = Time.unscaledTime;
            }

            // The match loop fades out at the round's end (GameManager); the menu loop comes in under the podium
            // and stays through the solo PLAY AGAIN countdown (the match loop replaces it at GO).
            if (!_leavingToLobby && MatchPresentationRules.StartsPodiumBed(true, _podiumBedStarted, Time.unscaledTime - _podiumShownAt))
            {
                _podiumBedStarted = true;
                _audio.StartMenuBed();
            }

            if (_leavingToLobby) return; // buttons stay disabled while the session shuts down
            bool canPlayAgain = gm.CanRequestPlayAgain;
            if (_mePlayAgainBtn != null) _mePlayAgainBtn.SetEnabled(canPlayAgain);
            UiElements.SetShown(_meHostNote, !canPlayAgain);
        }

        /// <summary>
        /// PLAY AGAIN: host / solo only. Solo goes straight to GET READY, multiplayer back to the waiting room
        /// (<see cref="MatchFlowRules.StateAfterPlayAgain"/>); never straight into a running match.
        /// </summary>
        private void OnPlayAgain()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            _audio.Tap();
            _log?.Info(Source, "Match end: PLAY AGAIN → GameManager.RequestPlayAgain().");
            gm.RequestPlayAgain();
        }

        /// <summary>
        /// BACK TO LOBBY (any player): leave the session through <see cref="INetworkService"/>, then load
        /// Bootstrap, which opens on THE COOP via <see cref="ISessionSelectionService.OpenLobbyOnMenuLoad"/>
        /// (a joiner lands on the main menu - decided on the menu side from the stored mode).
        /// </summary>
        private void OnBackToLobby()
        {
            if (_leavingToLobby) return; // a double tap would otherwise start two scene loads
            _audio.Tap();
            LeaveSession(changeBird: false, "BACK TO LOBBY");
        }

        private void PopulateMatchEnd(GameManager gm)
        {
            var ranked = MatchStandings.Ranked(CollectStandings(), gm.WinnerCorner);
            int[] places = MatchStandings.Places(ranked);
            int winnerIdx = ranked.FindIndex(e => e.Corner == gm.WinnerCorner);
            if (gm.WinnerCorner >= 0 && winnerIdx < 0)
                _log?.Error(Source, $"Winner corner {gm.WinnerCorner} has no base in the standings; showing MATCH ENDED without a winner.");
            // Nobody banked a thing: GameManager still names a corner (its tie-breaks), but the screen
            // shows a draw - no podium, no crown, no celebration (round-2 finding 6).
            bool draw = MatchStandings.IsDraw(ranked);
            bool hasWinner = gm.WinnerCorner >= 0 && winnerIdx >= 0 && !draw;
            var winner = hasWinner ? ranked[winnerIdx] : default;
            // The crown rides the centre step's bird; Ranked puts the declared winner there unless someone
            // out-banked them (not a real outcome: GameManager names the most food), then nobody is crowned.
            // A tie for 1st nothing visible settles crowns EVERY tied-1st bird (Maestro, round 4): "TIED AT THE TOP!".
            bool[] crowned = MatchStandings.Crowned(ranked, gm.WinnerCorner);
            for (int slot = 0; slot < _crownSlots.Length; slot++)
            {
                int rank = MatchStandings.PodiumRankBySlot[slot];
                _crownSlots[slot] = !draw && rank < crowned.Length && crowned[rank];
                _crownClasses[slot] = _crownSlots[slot] ? ranked[rank].Class : default;
            }
            bool tiedAtTop = MatchStandings.IsTiedAtTop(ranked, gm.WinnerCorner);

            // Banner: YOU WIN! / {NAME} WINS! / EMPTY NESTS! / MATCH ENDED, then the winner's class (or how a
            // tie for 1st was settled) under it. Both come from the standings alone (MatchStandings decides
            // the draw), so no path here can show MATCH ENDED over an all-zero result.
            if (_meRibbon != null) _meRibbon.text = MatchStandings.WinBanner(ranked, gm.WinnerCorner);
            if (_meWinSub != null)
                _meWinSub.text = MatchStandings.WinSubLine(ranked, gm.WinnerCorner) ?? UiText.Get(UiKeys.PostmatchNoWinner);
            // "You · 2nd" goes on the local player's own podium step; only a bird off the podium (4th) gets
            // it on the headline row, never as a caption under the winner's class.
            string chip = MatchStandings.YouPlaceChip(ranked, gm.WinnerCorner);
            int youIdx = ranked.FindIndex(e => e.IsLocal);
            bool chipOnPodium = chip != null && youIdx >= 0 && youIdx < MatchStandings.PodiumRankBySlot.Length;
            if (_meYouChip != null)
            {
                bool headline = chip != null && !chipOnPodium;
                _meYouChip.text = headline ? chip : string.Empty;
                _meYouChip.style.display = headline ? DisplayStyle.Flex : DisplayStyle.None;
            }
            for (int slot = 0; slot < _crowns.Length; slot++)
            {
                var crown = _crowns[slot];
                if (crown == null) continue;
                crown.style.display = _crownSlots[slot] ? DisplayStyle.Flex : DisplayStyle.None;
                crown.style.visibility = Visibility.Hidden;   // PlaceCrowns shows it once it is on the head
                _pods[slot]?.Root?.EnableInClassList("cw-me-pod--crowned", _crownSlots[slot]);
            }
            if (_meWinGlow != null) _meWinGlow.style.display = hasWinner ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasWinner) _celebration?.Start();   // no-op under Reduced Motion
            _meHero?.EnableInClassList("cw-me-hero--draw", draw);

            // GOAL 40 · REACHED / TIME'S UP. The goal comes from MatchConfig only; without it the note
            // is hidden rather than showing a made-up number.
            if (_meTargetNote != null)
            {
                if (_matchConfig == null)
                {
                    _log?.Error(Source, "No MatchConfigSO injected: the post-match goal note is hidden.");
                    _meTargetNote.style.display = DisplayStyle.None;
                }
                else
                {
                    int target = Mathf.Max(1, _matchConfig.FoodTargetToWin);
                    bool reached = hasWinner && MatchStandings.GoalReached(winner.Total, target);
                    _meTargetNote.style.display = DisplayStyle.Flex;
                    _meTargetNote.text = UiText.Format(
                        reached ? UiKeys.PostmatchTargetReached : UiKeys.PostmatchTargetTimeout, ("n", target));
                }
            }

            // A draw stands all the birds level under the banner (no steps, medals or crown).
            PopulatePodium(ranked, places, chipOnPodium ? chip : null, draw);

            // Standings rows.
            if (_meRows != null)
            {
                _meRows.Clear();
                _meRows.EnableInClassList("cw-me-rows--no-ko", !MatchStandings.ShowKoColumn(ranked));
                // Bars are the share of the goal, like the HUD (a share of the leader drew a near-full bar
                // for 0.6 food when nobody scored); without a MatchConfig they fall back to the leader.
                float maxTotal = _matchConfig != null
                    ? Mathf.Max(1f, _matchConfig.FoodTargetToWin)
                    : (ranked.Count > 0 ? Mathf.Max(1f, ranked[0].Total) : 1f);
                for (int i = 0; i < ranked.Count; i++)
                    _meRows.Add(BuildMeRow(draw ? 0 : places[i], ranked[i], maxTotal, isWinner: hasWinner && ranked[i].Corner == winner.Corner));
            }

            _log?.Info(Source, $"Match end: {(tiedAtTop ? "tied at the top (all tied-1st crowned), GameManager named corner " + winner.Corner : hasWinner ? $"winner corner {winner.Corner} ({MatchStandings.DisplayName(winner)})" : draw ? "draw (nobody banked)" : "no winner")}, " +
                $"{ranked.Count} in the standings, podium {(_stage != null ? "live" : "static")}.");
        }

        /// <summary>
        /// Fills the podium steps (2nd, 1st, 3rd) and binds the live stage if it is on. A step with
        /// nobody to show leaves the layout, so a 2-bird podium is two steps centred. Step height and
        /// medal follow the shared place (MatchStandings.Places), so tied birds stand level.
        /// </summary>
        private void PopulatePodium(List<MatchStandings.Entry> ranked, int[] places, string youChip, bool draw)
        {
            if (MenuStagePolicy.WantsLive(PlayerPreferences.PerformanceModeEnabled, _stageFailed)) TryCreateStage();
            _mePodium?.EnableInClassList("cw-me-podium--draw", draw);

            // Draw: every bird on level ground in corner order (the 4th pod exists only for this); otherwise
            // slot -> rank per PodiumRankBySlot, and the 4th pod stays empty.
            var order = ranked;
            if (draw)
            {
                order = new List<MatchStandings.Entry>(ranked);
                order.Sort((a, b) => a.Corner.CompareTo(b.Corner));
            }

            for (int slot = 0; slot < _pods.Length; slot++)
            {
                var v = _pods[slot];
                if (v?.Root == null) continue;
                int idx = draw ? slot : slot < MatchStandings.PodiumRankBySlot.Length ? MatchStandings.PodiumRankBySlot[slot] : int.MaxValue;
                bool filled = idx < order.Count;
                v.Root.EnableInClassList("cw-me-pod--hidden", !filled);
                if (!filled) { ShowPodChicken(slot, v, null, false); continue; }

                var e = order[idx];
                int place = draw ? 0 : places[idx];
                for (int t = 1; t <= 3; t++) v.Root.EnableInClassList($"cw-me-pod--tier-{t}", t == place);
                if (v.Medal != null)
                    for (int t = 1; t <= 3; t++) v.Medal.EnableInClassList($"cw-me-medal--{t}", t == place);
                Color color = PlayerPalette.ForCorner(e.Corner);
                if (v.Ring != null) v.Ring.style.unityBackgroundImageTintColor = color;
                if (v.Plate != null) v.Plate.style.borderTopColor = color;
                if (v.Name != null)
                {
                    v.Name.text = e.IsLocal && youChip != null ? youChip : MatchStandings.DisplayName(e);
                    v.Name.EnableInClassList(PlayerPalette.YouMarkClass, e.IsLocal);
                }
                if (v.Score != null) v.Score.text = MatchStandings.Score(e).ToString();
                if (v.Place != null) v.Place.text = place > 0 ? place.ToString() : string.Empty;
                ShowPodChicken(slot, v, e.HasChicken ? e.Class : (ChickenClass?)null, cheer: slot < _crownSlots.Length && _crownSlots[slot]);
            }
            ApplyPodiumScales();
        }

        /// <summary>
        /// Every bird at the same drawn size and the crowned one at PodiumLayout.WinnerScale times it
        /// (USS scale from the bottom centre, so feet stay on the ring and nothing re-lays out). Runs
        /// whenever a podium chicken element is laid out again (resize, rotation).
        /// </summary>
        private void ApplyPodiumScales()
        {
            var sides = new float[_pods.Length];
            for (int i = 0; i < _pods.Length; i++)
            {
                var c = _pods[i]?.Chicken;
                bool shown = c != null && _pods[i].Root != null && !_pods[i].Root.ClassListContains("cw-me-pod--hidden");
                sides[i] = shown ? Mathf.Min(c.layout.width, c.layout.height) : float.NaN;
            }
            var scales = PodiumLayout.ChickenScales(sides, _crownSlots);
            for (int i = 0; i < _pods.Length; i++)
            {
                var c = _pods[i]?.Chicken;
                if (c == null) continue;
                PodiumLayout.ApplyScale(c, scales[i]);
                _stage?.SetRenderScale(i, scales[i]);
            }
        }

        /// <summary>Static render class on the element, plus the live model over it when the stage is on.</summary>
        private void ShowPodChicken(int slot, PodView v, ChickenClass? cls, bool cheer)
        {
            if (v.Chicken == null) return;
            if (!string.IsNullOrEmpty(v.ChickenCss)) v.Chicken.RemoveFromClassList(v.ChickenCss);
            v.ChickenCss = cls.HasValue ? "cw-chicken--" + KeyOf(cls.Value) : null;
            if (v.ChickenCss != null) v.Chicken.AddToClassList(v.ChickenCss);

            if (_stage == null) return;
            try
            {
                if (cls.HasValue) _stage.Show(slot, v.Chicken, cls.Value, hop: cheer, sway: true, clear: StageClear);
                else _stage.Clear(slot);
            }
            catch (Exception e) { FailStage("a podium chicken could not be staged", e); }
        }

        private void TryCreateStage()
        {
            if (_stage != null) return;
            if (_classRegistry == null)
            {
                FailStage("ChickenClassRegistrySO not injected", null);
                return;
            }
            try
            {
                _stage = new MenuChickenStage(_classRegistry, _log, _pods.Length, PanelPixelsPerPoint);
            }
            catch (Exception e)
            {
                FailStage("the stage could not be created", e);
            }
        }

        /// <summary>Stage failure: log once, keep the static renders for the rest of this scene.</summary>
        private void FailStage(string reason, Exception e)
        {
            _stageFailed = true;
            _log?.Warn(Source, $"Live podium unavailable ({reason}{(e != null ? ": " + e.Message : "")}); showing the static renders.");
            DisposeStage();
        }

        private void DisposeStage()
        {
            if (_stage == null) return;
            _stage.Dispose();
            _stage = null;
        }

        /// <summary>Screen pixels per panel point (sizes the stage textures), as in the menu.</summary>
        private float PanelPixelsPerPoint()
        {
            float rootW = _root?.layout.width ?? 0f;
            if (float.IsNaN(rootW) || rootW <= 0f) return 1f;
            var ps = GetComponent<UIDocument>().panelSettings;
            float targetW = ps != null && ps.targetTexture != null ? ps.targetTexture.width : Screen.width;
            return targetW / rootW;
        }

        /// <summary>
        /// Keeps the crown on the winner's head, every frame while it shows: the live stage reports
        /// where the Head bone is in its texture (it follows the idle, the sway and the hop); the
        /// static cheer render uses the head position measured from the PNG (PodiumLayout).
        /// </summary>
        private void PlaceCrown()
        {
            if (!IsShown(_matchEndOverlay)) return;
            for (int slot = 0; slot < _crowns.Length; slot++)
            {
                var crown = _crowns[slot];
                if (crown == null || !_crownSlots[slot]) continue;
                var chicken = _pods[slot]?.Chicken;
                if (chicken == null) continue;
                float w = chicken.layout.width, h = chicken.layout.height;
                float cw = crown.resolvedStyle.width, ch = crown.resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h) || w <= 1f || h <= 1f || float.IsNaN(cw) || float.IsNaN(ch)) continue;

                Vector2 anchor = PodiumLayout.StaticHeadAnchor(_crownClasses[slot]);
                if (_stage != null && _stage.TryGetHeadAnchor(slot, out var live)) anchor = live;
                var pos = PodiumLayout.CrownTopLeft(w, h, anchor, cw, ch);
                crown.style.left = pos.x;
                crown.style.top = pos.y;
                if (crown.style.visibility != Visibility.Visible) crown.style.visibility = Visibility.Visible;
            }
        }

        /// <param name="place">Shared placing (1-based, MatchStandings.Places); 0 = a draw: plain medal, no number.</param>
        private VisualElement BuildMeRow(int place, in MatchStandings.Entry e, float maxTotal, bool isWinner)
        {
            Color color = PlayerPalette.ForCorner(e.Corner);

            var row = new VisualElement();
            row.AddToClassList("cw-me-row");
            row.EnableInClassList("cw-me-row--winner", isWinner);

            // Placement medal (gold / silver / bronze, plain cream after the podium) with its number.
            var medal = new VisualElement();
            medal.AddToClassList("cw-me-medal");
            if (place >= 1 && place <= 3) medal.AddToClassList($"cw-me-medal--{place}");
            var placeLabel = new Label(place > 0 ? place.ToString() : string.Empty);
            placeLabel.AddToClassList("cw-me-medal__num");
            medal.Add(placeLabel);
            row.Add(medal);

            // Player identity dot: the only player colour in the text area.
            var dot = new VisualElement();
            dot.AddToClassList("cw-me-dot");
            dot.style.unityBackgroundImageTintColor = color;
            row.Add(dot);

            // Name (+ CPU tag) over the class, ink and brown on cream.
            var mid = new VisualElement();
            mid.AddToClassList("cw-me-rowmid");
            var name = new Label(MatchStandings.DisplayName(e));
            name.AddToClassList("cw-me-rowname");
            name.EnableInClassList(PlayerPalette.YouMarkClass, e.IsLocal);
            mid.Add(name);
            if (e.HasChicken)
            {
                var classRow = new VisualElement();
                classRow.AddToClassList("cw-me-rowclassrow");
                var cls = new Label(ClassName(e.Class));
                cls.AddToClassList("cw-me-rowpn");
                classRow.Add(cls);
                if (e.IsBot)
                {
                    var tag = new Label(UiText.Get(UiKeys.TagCpu));
                    tag.AddToClassList("cw-me-rowtag");
                    classRow.Add(tag);
                }
                mid.Add(classRow);
            }
            row.Add(mid);

            // Progress bar (share of the leader's total).
            var trough = new VisualElement();
            trough.AddToClassList("cw-me-bartrough");
            var fill = new VisualElement();
            fill.AddToClassList("cw-me-barfill");
            fill.style.width = Length.Percent(Mathf.Clamp01(e.Total / maxTotal) * 100f);
            fill.style.unityBackgroundImageTintColor = color;
            trough.Add(fill);
            row.Add(trough);

            // Food score.
            var food = new VisualElement();
            food.AddToClassList("cw-me-rowfood");
            var foodIcon = new VisualElement();
            foodIcon.AddToClassList("cw-me-rowfoodicon");
            var score = new Label(MatchStandings.Score(e).ToString());
            score.AddToClassList("cw-me-rowscore");
            food.Add(foodIcon); food.Add(score);
            row.Add(food);

            // Knockouts ("{n} KO"); the whole column hides via .cw-me-rows--no-ko while nobody has one.
            var stats = new Label(MatchStandings.KoText(e.Kills));
            stats.AddToClassList("cw-me-rowstats");
            row.Add(stats);

            return row;
        }

        /// <summary>
        /// One entry per base that a chicken stands on or that banked food (a player who left keeps
        /// their score; an unclaimed corner in a 2-3 player match does not get a row of zeros).
        /// </summary>
        private static List<MatchStandings.Entry> CollectStandings()
        {
            var bases = PlayerBase.ActiveBases;
            var list  = new List<MatchStandings.Entry>(bases.Count);
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null) continue;
                var c = ChickenForCorner(b.CornerIndex);
                if (!MatchStandings.Listed(c != null, b.FoodTotal)) continue;
                list.Add(c != null
                    ? new MatchStandings.Entry(b.CornerIndex, b.FoodTotal, GetKillsForCorner(b.CornerIndex),
                        c.HasInputAuthority, c.IsBot, c.Class)
                    : new MatchStandings.Entry(b.CornerIndex, b.FoodTotal, 0, false, false, ChickenClass.Warrior, hasChicken: false));
            }
            return list;
        }

    }
}
