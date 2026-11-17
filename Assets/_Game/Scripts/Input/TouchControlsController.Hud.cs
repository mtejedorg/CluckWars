using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using CluckWars.UI;
using CluckWars.Visuals;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;


namespace CluckWars.Input
{
    /// <summary>
    /// <see cref="TouchControlsController"/>'s HUD half (A21, architecture audit 2026-11-12): everything that
    /// DRAWS the ability cluster, the stick and the status strip. The other file is the input contract — the
    /// pointer handlers, the press / hold / cancel state and the properties <c>TouchInputProvider</c> reads.
    /// </summary>
    /// <remarks>
    /// One component, two files, because the scene and the prefab reference this type by name and every
    /// serialized field has to stay on it: the split is a partial class, not a second component. The input
    /// half must keep working with this file deleted in spirit — nothing here is read by the contract.
    /// Both halves share <c>Source</c>, <c>SlotCount</c> and the bound element references declared in the
    /// main file; this one owns no state of its own.
    /// </remarks>
    public sealed partial class TouchControlsController
    {

        /// <summary>Caches the local chicken's <see cref="AbilitySlotOverlay"/> (the source of the "hot" verdict
        /// the pips read). Warns once if the local chicken has none — otherwise the pips are silently dead.</summary>
        private void ResolveLocalOverlay()
        {
            if (_localChicken == _overlayFor) return;
            _overlayFor = _localChicken;
            _localOverlay = _localChicken != null ? _localChicken.GetComponent<AbilitySlotOverlay>() : null;
            if (_localChicken != null && _localOverlay == null && !_reportedMissingOverlay)
            {
                _reportedMissingOverlay = true;
                _log?.Warn(Source, "Local chicken has no AbilitySlotOverlay: the in-range pips will never light.");
            }
        }

        /// <summary>
        /// Held / aiming state of the cluster (Phase 6, A1), driven by the LOCAL pointer state so it answers on
        /// the press frame: the held hex scales to <see cref="FeedbackTuning.HexHeldScale"/> with a bright
        /// (cream) rim, every other hex dims to <see cref="FeedbackTuning.HexOthersWhileHeldAlpha"/>. The
        /// transition is the 60 ms ease-out declared at bind.
        /// </summary>
        private void RefreshHeldVisuals()
        {
            bool any = false;
            for (int i = 0; i < SlotCount; i++) any |= IsHeldVisual(i);

            for (int slot = 0; slot < SlotCount; slot++)
            {
                var hex = _slots[slot].Hex;
                if (hex == null) continue;

                bool held = IsHeldVisual(slot);
                if (held != _appliedHeld[slot])
                {
                    _appliedHeld[slot] = held;
                    hex.EnableInClassList(HeldClass, held);
                }

                // Rest scale is 1 on the thumb arc and 0.75 on the desktop / pad strip; held multiplies it, and so does the
                // Death Mark KILL size (written here, not in USS: this inline scale would overwrite a USS one).
                float k = HudLayoutRules.HexRestScale(_mode) * (held ? FeedbackTuning.HexHeldScale : 1f) *
                          (_markKill[slot] ? MarkHexRules.KillScale(Time.unscaledTime - _markKillSince[slot], PlayerPreferences.ReducedMotionEnabled) : 1f);
                if (!Mathf.Approximately(k, _appliedScale[slot]))
                {
                    _appliedScale[slot] = k;
                    hex.style.scale = new Scale(new Vector3(k, k, 1f));
                }

                bool dim = any && !held;
                if (dim != _appliedDim[slot])
                {
                    _appliedDim[slot] = dim;
                    hex.style.opacity = dim ? FeedbackTuning.HexOthersWhileHeldAlpha : 1f;
                }

                bool showX = held && _cancelArmed[slot];
                if (showX != _appliedCancelX[slot])
                {
                    _appliedCancelX[slot] = showX;
                    hex.EnableInClassList(CancelArmedClass, showX); // dims the icon to ~40% under the X
                    if (_slots[slot].CancelX != null)
                        _slots[slot].CancelX.style.display = showX ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            RefreshEdgeGlow();
        }

        /// <summary>
        /// Is this hex drawn as held? On touch that is the pointer state. On the desktop / pad strip the hexes
        /// take no pointer, so a held KEY or button (read through the input provider, never a device) drives the same
        /// scale-up and dimming.
        /// </summary>
        private bool IsHeldVisual(int slot) =>
            _held[slot] || (_mode != HudDeviceMode.Touch && _input != null && _input.GetAbilityHeld(slot));

        /// <summary>
        /// The cancel zone (Phase 7 B2): once a touch hold outlives the tap threshold all four edge strips show dim, so
        /// the player can see WHERE the cancel is before reaching it; the strip of the armed edge is bright.
        /// </summary>
        private void RefreshEdgeGlow()
        {
            bool zone = false;
            var edge = ScreenEdge.None;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!HoldCancelRules.ZoneVisible(_held[i], _heldSeconds[i], FeedbackTuning.TapHoldThresholdSeconds)) continue;
                zone = true;
                if (_cancelArmed[i]) { edge = _armedEdge[i]; break; }
            }

            if (zone == _appliedZoneShown && edge == _appliedGlowEdge) return;
            _appliedZoneShown = zone;
            _appliedGlowEdge = edge;
            SetGlow(_glowLeft,   zone, edge == ScreenEdge.Left);
            SetGlow(_glowRight,  zone, edge == ScreenEdge.Right);
            SetGlow(_glowTop,    zone, edge == ScreenEdge.Top);
            SetGlow(_glowBottom, zone, edge == ScreenEdge.Bottom);
        }

        private static void SetGlow(VisualElement strip, bool shown, bool armed)
        {
            if (strip == null) return;
            strip.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            strip.EnableInClassList("cw-edge-glow--armed", armed);
        }

        /// <summary>Subscribes the caster stun countdown to <paramref name="chicken"/>'s cast event (null: unsubscribes).
        /// A cast that happened before the bind is never delivered, which is the baseline.</summary>
        private void BindStunCastEvents(ChickenController chicken)
        {
            if (_stunCastEvents != null) _stunCastEvents.Cast -= OnLocalCast;
            _stunCastFor = chicken;
            _stunCastEvents = chicken != null ? chicken.LocalEvents : null;
            _stunCastPending = false;
            if (_stunCastEvents != null) _stunCastEvents.Cast += OnLocalCast;
        }

        private void OnLocalCast(CastEvent e)
        {
            _stunCast = e;
            _stunCastPending = true;
        }

        /// <summary>Which hexes carry the Death Mark states this frame, by the equipped ability's type.</summary>
        private void UpdateMarkHexes(AbilityController abilities)
        {
            var mark = MarkFeedback.Current;
            _huntOut = mark;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                var a = abilities != null ? SlotAbility(abilities, slot) : null;
                _huntOn[slot] = HuntHexRules.Applies(mark, a.Is(AbilityJob.DeathMark));
                bool kill = mark.KillHex && a.Is(AbilityJob.DeathMark);
                if (kill && !_markKill[slot]) _markKillSince[slot] = Time.unscaledTime;
                _markKill[slot] = kill;
                _markLit[slot] = mark.AmbushLit && FixedKit.EmitsStun(a);
            }
        }

        /// <summary>
        /// A4: watches the local bird's casts and decides, once a frame, whether the Stun hex shows the victims'
        /// remaining stun (<see cref="CasterStunCountdownRules"/>; its remarks hold the "his victims" approximation).
        /// Reads replicated state only (the local bird's cast event from <c>ChickenLocalEvents</c>, the
        /// rivals' <c>StunRemaining</c>); no allocation once the rival buffer fits.
        /// </summary>
        private void UpdateCasterStunCountdown(AbilityController abilities)
        {
            _casterStunShow = false;
            if (abilities == null || _localChicken == null) { BindStunCastEvents(null); return; }

            if (_stunCastFor != _localChicken)
            {
                // Baseline: a cast that happened before this HUD bound to the bird is not "his last stun" to count.
                BindStunCastEvents(_localChicken);
                _stunCastIsWingSlam = false;
            }
            else
            {
                _stunCastEvents.Observe();   // this frame's edge, before the latch is read
                if (_stunCastPending)
                {
                    _stunCastPending = false;
                    var cast = _stunCastEvents.AbilityOf(_stunCast.AbilityCode);
                    var stun = cast.Is(AbilityJob.Stun) ? cast : null;
                    _stunCastIsWingSlam = stun != null;
                    _stunCastHits = _stunCast.HitCount;
                    _stunCastAt = Time.unscaledTime;   // Fusion timers ignore timeScale, so unscaled time tracks StunRemaining
                    _stunCastSeconds = stun != null ? AbilityBaseSO.ResolveOutgoingControlSeconds(stun.EmittedControl.Seconds, _localChicken) : 0f;
                }
            }
            if (!_stunCastIsWingSlam) return;

            var all = ChickenController.ActiveControllers;
            if (_rivalStun.Length < all.Count) _rivalStun = new float[all.Count];
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _localChicken || c.Object == null || !c.Object.IsValid) continue;
                _rivalStun[n++] = c.StunRemaining;
            }

            if (CasterStunCountdownRules.TryGetSeconds(true, _stunCastHits, Time.unscaledTime - _stunCastAt,
                    _stunCastSeconds, _rivalStun, n, out float seconds))
            {
                _casterStunShow = true;
                _casterStunTenths = CasterStunCountdownRules.Tenths(seconds);
            }
        }

        private void ApplyMarkHexClasses(int slot)
        {
            var hex = _slots[slot].Hex;
            if (_markKill[slot] != _appliedMarkKill[slot])
            {
                _appliedMarkKill[slot] = _markKill[slot];
                hex.EnableInClassList(MarkKillClass, _markKill[slot]);
            }
            if (_markLit[slot] != _appliedMarkLit[slot])
            {
                _appliedMarkLit[slot] = _markLit[slot];
                hex.EnableInClassList(MarkLitClass, _markLit[slot]);
            }
            ApplyHuntHex(slot);
        }

        /// <summary>
        /// The Hunt hex's drawn parts (HuntHexRules): the class that shows them, the dot in the target's seat colour, and the one
        /// glyph for what blocks the pounce (a badge, or the red metres chip when the target is too far). Each part is written
        /// only when its input changes.
        /// </summary>
        private void ApplyHuntHex(int slot)
        {
            var refs = _slots[slot];
            bool on = _huntOn[slot];
            if (on != _appliedHuntOn[slot])
            {
                _appliedHuntOn[slot] = on;
                refs.Hex.EnableInClassList(HuntHexRules.HuntClass, on);
                if (!on)
                {
                    _appliedHuntCorner[slot] = int.MinValue;
                    _appliedHuntMetres[slot] = int.MinValue;
                    SetHuntGlyph(slot, null);
                }
            }
            if (!on) return;

            if (refs.HuntDot != null && _huntOut.TargetCorner != _appliedHuntCorner[slot])
            {
                _appliedHuntCorner[slot] = _huntOut.TargetCorner;
                refs.HuntDot.style.backgroundColor = PlayerPalette.ForCorner(_huntOut.TargetCorner);
            }

            string glyph = HuntHexRules.GlyphClass(_huntOut.Blocker);
            SetHuntGlyph(slot, glyph);
            // The marker's chip and this one share HuntRules.MetresKey / ChipText, so they never disagree.
            int metresKey = HuntRules.MetresKey(_huntOut.MarkerDistance);
            if (glyph == HuntHexRules.GlyphFarClass && refs.HuntFar != null && metresKey != _appliedHuntMetres[slot])
            {
                _appliedHuntMetres[slot] = metresKey;
                refs.HuntFar.text = HuntRules.ChipText(metresKey);
            }
        }

        /// <summary>Swaps the blocker glyph class (null = none); "too far" shows the metres chip instead of a badge.</summary>
        private void SetHuntGlyph(int slot, string glyphClass)
        {
            if (glyphClass == _appliedHuntGlyph[slot]) return;
            var refs = _slots[slot];
            if (refs.HuntGlyph != null)
            {
                if (_appliedHuntGlyph[slot] != null) refs.HuntGlyph.RemoveFromClassList(_appliedHuntGlyph[slot]);
                if (glyphClass != null) refs.HuntGlyph.AddToClassList(glyphClass);
            }
            if (refs.HuntFar != null)
            {
                bool far = glyphClass == HuntHexRules.GlyphFarClass;
                refs.HuntFar.style.display = far ? DisplayStyle.Flex : DisplayStyle.None;
                if (!far) _appliedHuntMetres[slot] = int.MinValue;
            }
            _appliedHuntGlyph[slot] = glyphClass;
        }

        private const string HeldClass = "cw-hex--held";
        private const string CancelArmedClass = "cw-hex--cancel-armed";

        private void RefreshSlot(int slot, AbilityController abilities)
        {
            var refs = _slots[slot];
            if (refs.Hex == null) return;

            AbilityBaseSO equipped = abilities != null ? SlotAbility(abilities, slot) : null;

            // Swap icon sprite + short label only when the equipped SO changes.
            if (equipped != _appliedSlots[slot])
            {
                _appliedSlots[slot] = equipped;
                ApplyEquipped(slot, equipped);
            }

            // Slot 3 (Combo) is only meaningful for Assassin — hide when empty. This is
            // also the whole of the SlotUnavailable refusal treatment (§6 case 28): ART
            // §6.6's slot-3 visibility rule already removes the hex from the layout, so
            // there is nothing left to mark.
            if (slot == 2)
            {
                var display = equipped != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (refs.Hex.style.display != display) refs.Hex.style.display = display;
            }

            float remaining = 0f, total = 0f;
            if (equipped != null)
            {
                total = equipped.Cooldown;
                if (total > 0f) remaining = abilities.CooldownRemaining(slot);
            }

            float pct = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            // The one live reason this slot would refuse to fire. Comes from the same
            // precedence table TryActivate refuses through (AbilityRefusalRules.Evaluate),
            // so the button and the gameplay gate can never tell different stories.
            AbilityRefusal refusal = (abilities != null && equipped != null)
                ? abilities.EvaluateRefusal(slot)
                : AbilityRefusal.None;

            ApplyRefusalState(slot, refusal);

            // ART §6.6 layer 6 — black, bottom-up: "this slot is cooling".
            if (refs.Cooldown != null)
                refs.Cooldown.style.height = new Length(pct * 100f, LengthUnit.Percent);

            // ART §6.6 layer 6 again, mirrored — accent, top-down: "this slot is
            // RUNNING". Opposite direction and opposite colour from the cooldown clip on
            // purpose (§6 case 27): the two states routinely coexist on the same hex,
            // because an ability burns its cooldown at activation.
            float active01 = (abilities != null && equipped != null)
                ? abilities.ActiveRemaining01For(slot)
                : 0f;
            if (refs.Drain != null)
                refs.Drain.style.height = new Length(active01 * 100f, LengthUnit.Percent);
            RefreshEggPhase(slot, abilities, equipped);

            // Phase 6 (A1): while the ability runs, a duration ring around the hex rim drains with it.
            // While a hunt is on, the same ring shows the mark window draining in the marker's colour; the hunt takes it over
            // from the ability's own 0.1 s run.
            bool hunt = _huntOn[slot];
            bool ringOn = hunt || active01 > 0f;
            if (refs.Ring != null)
            {
                if (ringOn != _ringShown[slot])
                {
                    _ringShown[slot] = ringOn;
                    refs.Ring.style.display = ringOn ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (ringOn)
                {
                    refs.Ring.SetColour(hunt ? HuntRules.MarkerColour(_huntOut.MarkerMode, _huntOut.TargetCorner) : HexDurationRing.DefaultFill);
                    refs.Ring.SetFraction(hunt ? _huntOut.MarkerFill01 : active01);
                }
            }

            // Phase 6 (A6): the auto-chain is running on this Peck hex. Spins; static under Reduced Motion.
            bool chainOn = abilities != null && abilities.PeckChainActive && Abilities.AbilityJobs.Is(equipped, Abilities.AbilityJob.Peck);
            if (refs.Chain != null)
            {
                if (chainOn != _chainShown[slot])
                {
                    _chainShown[slot] = chainOn;
                    refs.Chain.style.display = chainOn ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (chainOn) refs.Chain.Spin(Time.unscaledTime, PlayerPreferences.ReducedMotionEnabled);
            }

            RefreshPip(slot, equipped, refusal, abilities != null && abilities.IsRearming(slot));

            // The menu's category colour (round-2 finding 5): a move reads the same job in the deck and
            // on the button. AccentColor stays the VFX colour.
            Color accent = equipped != null ? AbilityPalette.HexColor(equipped) : AbilityPalette.EmptyHex;

            if (refs.DrainFill != null && active01 > 0f)
            {
                // Full-strength category colour: the running slot reads bright; the cluster is not dimmed
                // while abilities run (Phase 6 chunk 3).
                var drainTint = accent;
                drainTint.a = FeedbackTuning.HexAlphaReady;
                refs.DrainFill.style.unityBackgroundImageTintColor = drainTint;
            }

            // ART §6.6 layers 2–4. Exactly one refusal applies at a time, so the whole
            // table is a flat switch; every alpha and colour is named in FeedbackTuning.
            Color tint;
            switch (refusal)
            {
                case AbilityRefusal.Cooldown:
                    tint = accent;
                    tint.a = FeedbackTuning.HexAlphaCooldown;
                    break;
                case AbilityRefusal.Stunned:
                    // The illegal-cast wash, the same one the telegraph uses when a hold
                    // goes illegal mid-aim — being stunned and having your aim invalidated
                    // are the same story told at two moments.
                    tint = FeedbackTuning.IllegalCastTintColor;
                    break;
                default:
                    // Ready, and "no target in range" too (Phase 6, A1): nobody around is not a refusal, so
                    // the hex rests at its full category hue, opaque. The in-range pip says when someone is.
                    // The hunt is no refusal either: the Death Mark hex stays opaque while the pounce is not legal.
                    tint = accent;
                    tint.a = HuntHexRules.TintAlpha(refusal, _huntOn[slot]);
                    break;
            }
            // Death Mark: KILL and lit Ambush paint the fill kill-yellow through this same inline path (USS would be overwritten).
            if (_markKill[slot] || _markLit[slot]) tint = FeedbackTuning.MarkKillColor;
            ApplyMarkHexClasses(slot);
            (refs.Fill ?? refs.Hex).style.unityBackgroundImageTintColor = tint;

            // Layer 9. The seconds number is only one of the marks this layer can carry
            // now; the others are drawn shapes toggled by the refusal class in USS.
            RefreshCenterMark(slot, equipped, refusal, remaining);
        }

        /// <summary>
        /// The in-range pip (Phase 6, A1): lit while a rival is inside this slot's shape and the slot is not
        /// cooling down (<see cref="AbilityPreviewRules.InRangePipVisible"/>). "Hot" comes from the local
        /// chicken's <see cref="AbilitySlotOverlay"/> — the same verdict that brightens the ground guide, so the
        /// two never disagree. One 0.2 s scale pop when it lights; static under Reduced Motion.
        /// </summary>
        private void RefreshPip(int slot, AbilityBaseSO equipped, AbilityRefusal refusal, bool rearming)
        {
            var pip = _slots[slot].Pip;
            if (pip == null) return;

            bool hot = equipped != null && _localOverlay != null && _localOverlay.IsHot(slot);
            bool show = AbilityPreviewRules.InRangePipVisible(hot, refusal, rearming);

            if (show != _pipShown[slot])
            {
                _pipShown[slot] = show;
                pip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _pipPopTimer[slot] = show && !PlayerPreferences.ReducedMotionEnabled ? 0f : -1f;
                if (!show || PlayerPreferences.ReducedMotionEnabled)
                    pip.style.scale = new Scale(Vector3.one);
            }

            if (_pipPopTimer[slot] < 0f) return;

            // unscaledDeltaTime: the pop is HUD motion and must not stretch with the hit-stop timeScale dip.
            _pipPopTimer[slot] += Time.unscaledDeltaTime;
            float t = _pipPopTimer[slot] / FeedbackTuning.HexPipPopSeconds;
            if (t >= 1f)
            {
                _pipPopTimer[slot] = -1f;
                pip.style.scale = new Scale(Vector3.one);
                return;
            }
            float k = 1f + (FeedbackTuning.HexPipPopScale - 1f) * Mathf.Sin(t * Mathf.PI);
            pip.style.scale = new Scale(new Vector3(k, k, 1f));
        }

        /// <summary>
        /// ART §6.6 layer 9, generalised from "cooldown seconds" to "state indicator".
        /// Only the seconds branch writes text, and only when the displayed integer
        /// actually moves — a seconds countdown changes at most once a second, well
        /// inside the <see cref="FeedbackTuning.BadgeCountdownTextUpdateHz"/> budget, so
        /// gating on the value itself is strictly tighter than gating on the clock.
        /// </summary>
        private void RefreshCenterMark(int slot, AbilityBaseSO equipped, AbilityRefusal refusal, float remaining)
        {
            var refs = _slots[slot];
            if (refs.CdNum == null) return;

            var mark = HudFeedbackStyle.CenterMark(refusal);

            // A4: the victims' remaining stun takes the centre over the cooldown seconds (the cooldown is necessarily
            // running: he just cast) on the touch arc and the desktop / pad strip alike. The cooldown clip and the
            // duration ring are separate layers and keep drawing. A "you are stunned" cross keeps the centre.
            if (RefreshHuntDigit(slot, mark)) return;
            if (RefreshCasterStunDigit(slot, equipped, mark)) return;
            if (RefreshEggDigit(slot, equipped, mark)) return;

            // With drawn marks, the label is the seconds readout and nothing else; with
            // the glyph fallback it also carries ⃠ / ✕ (text written in ApplyRefusalState).
            bool showLabel = HudFeedbackStyle.UseDrawnRefusalMarks
                ? mark == HexCenterMark.CooldownSeconds
                : mark != HexCenterMark.None;

            var display = showLabel ? DisplayStyle.Flex : DisplayStyle.None;
            if (refs.CdNum.style.display != display) refs.CdNum.style.display = display;

            if (mark != HexCenterMark.CooldownSeconds) return;

            if (_mode != HudDeviceMode.Touch)
            {
                // Desktop / pad strip: whole seconds, then tenths in the last second (HudLayoutRules.CooldownText).
                string text = HudLayoutRules.CooldownText(remaining);
                if (text == _appliedCdText[slot]) return;
                _appliedCdText[slot] = text;
                refs.CdNum.text = text;
                return;
            }

            int seconds = remaining > 0f ? Mathf.CeilToInt(remaining) : 0;
            if (seconds == _appliedCdSeconds[slot]) return;
            _appliedCdSeconds[slot] = seconds;
            refs.CdNum.text = HudLayoutRules.TouchCooldownText(remaining);
        }

        /// <summary>
        /// J3: reads the Egg Shell slot's phase off the replicated run (the badge row's read: remaining run = active fraction x
        /// the resolved duration, egg when it is within <c>EggSeconds</c>) and swaps the hex icon to the cracked egg only when
        /// the phase moves. A hatch press (slot goes inactive) restores the whole egg on the next frame.
        /// </summary>
        private void RefreshEggPhase(int slot, AbilityController abilities, AbilityBaseSO equipped)
        {
            int tenths = 0;
            bool egg = false;
            if (abilities != null && equipped.Is(AbilityJob.Shield))
            {
                bool active = abilities.IsSlotActive(slot);
                float run = active ? abilities.ActiveRemaining01For(slot) * equipped.ResolveActiveDuration(_localChicken) : 0f;
                egg = EggPhaseHexRules.Resolve(active, run, equipped.FollowUpPhaseSeconds, out tenths);
            }
            _eggTenths[slot] = tenths;
            if (egg == _eggIconShown[slot]) return;
            _eggIconShown[slot] = egg;
            _slots[slot].Icon?.EnableInClassList(EggPhaseHexRules.EggIconClass, egg);
        }

        /// <summary>
        /// The hunt's count (<see cref="HuntRules.CountText"/>: 3, 2, 1, then tenths in the live second) owns the layer-9 label on
        /// the Death Mark hex while the hunt is on, in cream (the dot and ring carry the seat colour). Same contract as
        /// <see cref="RefreshEggDigit"/>: it outranks the caster-stun and egg digits and the cooldown seconds, yields to the
        /// stunned cross, writes text only when it changes, and on hand-back restores the stylesheet colour and forces the cooldown
        /// readout to repaint. Taking the label over clears the other owners' "shown" flags so they re-initialise when it returns.
        /// </summary>
        private bool RefreshHuntDigit(int slot, HexCenterMark mark)
        {
            var label = _slots[slot].CdNum;
            bool show = _huntOn[slot] && mark != HexCenterMark.StunnedCross;

            switch (HuntHexRules.DigitStep(_huntDigitShown[slot], show))
            {
                case HuntDigitStep.Idle:
                    return false;
                case HuntDigitStep.HandBack:
                    _huntDigitShown[slot] = false;
                    _appliedHuntText[slot] = null;
                    label.style.color = new StyleColor(StyleKeyword.Null);
                    _appliedCdSeconds[slot] = int.MinValue;
                    _appliedCdText[slot] = null;
                    return false;
                case HuntDigitStep.Acquire:
                    _huntDigitShown[slot] = true;
                    _stunDigitShown[slot] = false;
                    _eggDigitShown[slot] = false;
                    _appliedHuntText[slot] = null;
                    label.style.color = FeedbackTuning.MarkCream;
                    label.style.display = DisplayStyle.Flex;
                    break;
            }

            string text = HuntRules.CountText(_huntOut.Armed, _huntOut.ArmCountdownSeconds, _huntOut.Remaining);
            if (text != _appliedHuntText[slot])
            {
                _appliedHuntText[slot] = text;
                label.text = text;
            }
            return true;
        }

        /// <summary>
        /// J3: while the egg phase runs, the layer-9 label owns the egg's remaining seconds ("1.4s") in the Egg Shell asset's
        /// accent, reformatting only when the shown tenth moves. Returns true while it owns the label; on hand-back it restores
        /// the stylesheet colour and forces the cooldown readout to repaint (same contract as <see cref="RefreshCasterStunDigit"/>).
        /// </summary>
        private bool RefreshEggDigit(int slot, AbilityBaseSO shield, HexCenterMark mark)
        {
            var label = _slots[slot].CdNum;
            bool show = _eggTenths[slot] > 0 && shield != null && mark != HexCenterMark.StunnedCross;

            if (!show)
            {
                if (!_eggDigitShown[slot]) return false;
                _eggDigitShown[slot] = false;
                label.style.color = new StyleColor(StyleKeyword.Null);
                _appliedCdSeconds[slot] = int.MinValue;
                _appliedCdText[slot] = null;
                return false;
            }

            if (!_eggDigitShown[slot])
            {
                _eggDigitShown[slot] = true;
                _appliedEggTenths[slot] = int.MinValue;
                // The digit wears the Egg Shell asset's own accent (text colour only; the hex TINT stays the category palette, PlayerPaletteTests).
                label.style.color = shield.AccentColor();
                label.style.display = DisplayStyle.Flex;
            }
            if (_eggTenths[slot] != _appliedEggTenths[slot])
            {
                _appliedEggTenths[slot] = _eggTenths[slot];
                label.text = EggPhaseHexRules.Format(_eggTenths[slot]);
            }
            return true;
        }

        /// <summary>
        /// A4: writes the caster-side stun digit into the layer-9 label in the stun colour while
        /// <see cref="UpdateCasterStunCountdown"/> says so, reformatting only when the shown tenth moves. Returns true
        /// while the digit owns the label. On hand-back it restores the stylesheet colour and forces the cooldown
        /// readout to repaint.
        /// </summary>
        private bool RefreshCasterStunDigit(int slot, AbilityBaseSO equipped, HexCenterMark mark)
        {
            var label = _slots[slot].CdNum;
            bool show = _casterStunShow && equipped.Is(AbilityJob.Stun) && mark != HexCenterMark.StunnedCross;

            if (!show)
            {
                if (!_stunDigitShown[slot]) return false;
                _stunDigitShown[slot] = false;
                label.style.color = new StyleColor(StyleKeyword.Null);
                _appliedCdSeconds[slot] = int.MinValue;
                _appliedCdText[slot] = null;
                return false;
            }

            if (!_stunDigitShown[slot])
            {
                _stunDigitShown[slot] = true;
                _appliedStunTenths[slot] = int.MinValue;
                label.style.color = FeedbackTuning.CanonicalStunColor;
                label.style.display = DisplayStyle.Flex;
            }
            if (_casterStunTenths != _appliedStunTenths[slot])
            {
                _appliedStunTenths[slot] = _casterStunTenths;
                label.text = CasterStunCountdownRules.Format(_casterStunTenths);
            }
            return true;
        }

        /// <summary>
        /// Applies the refusal's USS class and the drawn mark's colours. Runs only on a
        /// real state change — the class list and the mark colours are static for the
        /// whole time a refusal holds, and rewriting them per-frame would dirty the
        /// element's style every frame for nothing.
        /// </summary>
        private void ApplyRefusalState(int slot, AbilityRefusal refusal)
        {
            if (_appliedRefusal[slot] == refusal) return;
            _appliedRefusal[slot] = refusal;

            var refs = _slots[slot];

            if (!string.IsNullOrEmpty(_appliedRefusalCls[slot]))
                refs.Hex.RemoveFromClassList(_appliedRefusalCls[slot]);

            string cls = HudFeedbackStyle.HexClass(refusal);
            if (!string.IsNullOrEmpty(cls)) refs.Hex.AddToClassList(cls);
            _appliedRefusalCls[slot] = cls;

            // Force the next seconds write through, so returning to a cooldown after any
            // other state repaints the number instead of trusting a stale cache.
            _appliedCdSeconds[slot] = int.MinValue;
            _appliedCdText[slot] = null;

            var mark = HudFeedbackStyle.CenterMark(refusal);

            if (HudFeedbackStyle.UseDrawnRefusalMarks)
            {
                // USS decides which strokes are visible; only the semantic colour is
                // written here, and only from FeedbackTuning.
                if (mark == HexCenterMark.StunnedCross)
                {
                    if (refs.StateBarA != null)
                        refs.StateBarA.style.backgroundColor = FeedbackTuning.RefusalStunnedCrossColor;
                    if (refs.StateBarB != null)
                        refs.StateBarB.style.backgroundColor = FeedbackTuning.RefusalStunnedCrossColor;
                }
                return;
            }

            // Glyph fallback (HudFeedbackStyle.UseDrawnRefusalMarks == false): the same
            // layer-9 label carries the character instead of the drawn shape. Clearing
            // the inline colour hands the label back to the stylesheet's warm white
            // rather than baking a second copy of that colour here.
            _stunDigitShown[slot] = false;   // the writes below replace the A4 digit's colour; let it re-apply next frame
            if (refs.CdNum == null) return;
            switch (mark)
            {
                case HexCenterMark.StunnedCross:
                    refs.CdNum.text        = HudFeedbackStyle.StunnedCrossGlyph;
                    refs.CdNum.style.color = FeedbackTuning.RefusalStunnedCrossColor;
                    break;
                default:
                    refs.CdNum.style.color = new StyleColor(StyleKeyword.Null);
                    break;
            }
        }

        // ---- Denied-press bump (FEEDBACK.md §6, case 29) -----------------------

        /// <summary>E1: the muted click under the bump, once per consumed refused press (the flag is one-shot). Local only:
        /// this HUD only ever reads the local chicken. A null clip is silent; a missing service is reported once.</summary>
        private void PlayDeniedClick()
        {
            if (_audio == null || _sfx == null)
            {
                if (!_missingAudioReported)
                {
                    _missingAudioReported = true;
                    _log?.Warn(Source, "No IAudioService / AudioRegistrySO injected: the denied-press click is silent.");
                }
                return;
            }
            _audio.TryPlaySFX(SfxId.DeniedClick);
        }

        /// <summary>
        /// A press that was refused must never be absorbed silently. <c>AbilityController</c>
        /// latches one flag per refused press; this consumes it and shakes that hex.
        /// </summary>
        private void PollDeniedPress(AbilityController abilities)
        {
            if (abilities != null && abilities.TryConsumeDeniedPress(out int denied)
                && denied >= 0 && denied < SlotCount)
            {
                StartBump(denied);
                PlayDeniedClick();
            }

            // A fizzle (target-gated move released with nobody in range) is its own one-shot: the plain bump plus
            // the slash flash and whiff puff (the whiff SOUND is played by AbilityController, local only).
            if (abilities != null && abilities.TryConsumeFizzle(out int fizzled)
                && fizzled >= 0 && fizzled < SlotCount)
            {
                StartBump(fizzled);
                _fizzleTimer[fizzled] = 0f;
                SetFizzleVisible(fizzled, true);
            }

            for (int slot = 0; slot < SlotCount; slot++)
            {
                UpdateBump(slot);
                UpdateFizzle(slot);
            }
        }

        /// <summary>Starts the denied / fizzle shake. Skipped under Reduced Motion: the refusal marks and the sound
        /// still say "no", only the wobble is dropped (Phase 7 A15).</summary>
        private void StartBump(int slot)
        {
            if (PlayerPreferences.ReducedMotionEnabled) return;
            _bumpTimer[slot] = 0f;
        }

        /// <summary>
        /// Builds the code-made hex overlays: the cancel X (two bars), the fizzle slash and the whiff puff. All
        /// picking-mode Ignore, hidden until driven. Painted above everything else in the hex.
        /// </summary>
        private static void BuildFlashElements(ref HexSlot slot, VisualElement hex)
        {
            var x = new VisualElement { pickingMode = PickingMode.Ignore };
            x.AddToClassList("cw-hex-x");
            var a = new VisualElement { pickingMode = PickingMode.Ignore };
            a.AddToClassList("cw-hex-x-bar"); a.AddToClassList("cw-hex-x-bar--a");
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.AddToClassList("cw-hex-x-bar"); b.AddToClassList("cw-hex-x-bar--b");
            x.Add(a); x.Add(b);
            x.style.display = DisplayStyle.None;

            var puff = new VisualElement { pickingMode = PickingMode.Ignore };
            puff.AddToClassList("cw-hex-puff");
            puff.style.display = DisplayStyle.None;

            var slash = new VisualElement { pickingMode = PickingMode.Ignore };
            slash.AddToClassList("cw-hex-flash-slash");
            slash.style.display = DisplayStyle.None;

            hex.Add(puff);
            hex.Add(slash);
            hex.Add(x);
            slot.CancelX = x;
            slot.Puff = puff;
            slot.FizzleSlash = slash;
        }

        private void SetFizzleVisible(int slot, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            var refs = _slots[slot];
            if (refs.FizzleSlash != null) refs.FizzleSlash.style.display = d;
            if (refs.Puff != null) refs.Puff.style.display = d;
        }

        /// <summary>
        /// The 0.3 s fizzle flash: the slash is a transient flash (full strength, then gone with the puff), and the
        /// whiff puff grows and fades. Under Reduced Motion the puff does not scale; it only fades. Driven by
        /// <c>unscaledDeltaTime</c>, like the bump, so a hit-stop dip cannot stretch it.
        /// </summary>
        private void UpdateFizzle(int slot)
        {
            if (_fizzleTimer[slot] < 0f) return;

            var refs = _slots[slot];
            _fizzleTimer[slot] += Time.unscaledDeltaTime;
            float t = _fizzleTimer[slot] / FeedbackTuning.FizzleFlashSeconds;
            if (t >= 1f)
            {
                _fizzleTimer[slot] = -1f;
                SetFizzleVisible(slot, false);
                if (refs.Puff != null) refs.Puff.style.scale = new Scale(Vector3.one);
                return;
            }

            if (refs.FizzleSlash != null) refs.FizzleSlash.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            if (refs.Puff != null)
            {
                refs.Puff.style.opacity = FeedbackTuning.FizzlePuffStartAlpha * (1f - t);
                float k = PlayerPreferences.ReducedMotionEnabled
                    ? 1f
                    : Mathf.Lerp(FeedbackTuning.FizzlePuffStartScale, FeedbackTuning.FizzlePuffEndScale, t);
                refs.Puff.style.scale = new Scale(new Vector3(k, k, 1f));
            }
        }

        /// <summary>
        /// Decaying horizontal oscillation over
        /// <see cref="FeedbackTuning.DeniedPressBumpDurationSeconds"/>. Driven by
        /// <c>unscaledDeltaTime</c> on purpose: the §3.4 execute hit-stop dips
        /// <c>Time.timeScale</c>, and a refusal cue that stretched out with it would
        /// outlive the press that caused it.
        /// </summary>
        private void UpdateBump(int slot)
        {
            if (_bumpTimer[slot] < 0f) return;

            var hex = _slots[slot].Hex;
            _bumpTimer[slot] += Time.unscaledDeltaTime;
            float t = _bumpTimer[slot] / FeedbackTuning.DeniedPressBumpDurationSeconds;

            if (t >= 1f)
            {
                _bumpTimer[slot] = -1f;
                if (hex != null) hex.style.translate = new Translate(0f, 0f, 0f);
                return;
            }

            float x = FeedbackTuning.DeniedPressBumpAmplitudePx * (1f - t)
                    * Mathf.Sin(t * 2f * Mathf.PI * FeedbackTuning.DeniedPressBumpOscillations);
            if (hex != null) hex.style.translate = new Translate(x, 0f, 0f);
        }

        // ---- Control consequences: the stick + the status strip (§5.2) ---------

        private void RefreshControlConsequences()
        {
            var chicken = _localChicken;
            bool valid  = chicken != null && chicken.Object != null && chicken.Object.IsValid;

            float slow = valid ? chicken.SlowMultiplier : 1f;
            ApplyJoystickState(valid ? chicken.CurrentControlState : ControlState.Free, slow);

            int count = valid
                ? HudFeedbackStyle.CollectStatusRows(
                      chicken.IsStunned, chicken.StunRemaining,
                      chicken.Rooted,    chicken.RootRemaining,
                      slow, ChickenStatusBadges.ShowSlowRow(chicken.ControlFlags), _statusScratch)
                : 0;

            // Forget the cached peak of any status that is no longer running, so the next
            // stun/root measures its own duration instead of draining against a stale
            // one. Keyed by kind, never by row index — a row's kind changes when a more
            // severe status above it expires and the rest shuffle up.
            bool stunLive = false, rootLive = false;
            for (int i = 0; i < count; i++)
            {
                if (_statusScratch[i].Kind == HudStatusKind.Stun) stunLive = true;
                else if (_statusScratch[i].Kind == HudStatusKind.Root) rootLive = true;
            }
            if (!stunLive) _statusPeak[(int)HudStatusKind.Stun] = 0f;
            if (!rootLive) _statusPeak[(int)HudStatusKind.Root] = 0f;

            ApplyStatusStrip(count);
        }

        /// <summary>
        /// §5.2's control-consequence marking on the move stick. Communication only: the
        /// stick stays draggable and <see cref="Movement"/> keeps reporting, because
        /// <c>ControlRules.CanMove</c> is already enforced in <c>ChickenController</c> and
        /// a second gate here would hide input bugs rather than fix them.
        /// </summary>
        private void ApplyJoystickState(ControlState state, float slowMultiplier)
        {
            if (_joyBase == null) return;

            if (state != _appliedJoyState)
            {
                _appliedJoyState = state;

                if (!string.IsNullOrEmpty(_appliedJoyCls)) _joyBase.RemoveFromClassList(_appliedJoyCls);
                string cls = HudFeedbackStyle.JoystickClass(state);
                if (!string.IsNullOrEmpty(cls)) _joyBase.AddToClassList(cls);
                _appliedJoyCls = cls;

                // Greyed = "this control is refused", at the same neutral colour and the
                // same alpha a refused ability hex uses, so a dead stick and a dead button
                // read as one idea. Slowed is TINTED, not greyed: movement still works, it
                // is just worth less, and greying it would say the opposite of the truth.
                Color tint;
                switch (state)
                {
                    case ControlState.Stunned:
                    case ControlState.Rooted:
                        tint = FeedbackTuning.NeutralNoEffectColor;
                        tint.a = FeedbackTuning.HexAlphaNoTarget;
                        break;
                    case ControlState.Slowed:
                        tint = FeedbackTuning.CanonicalSlowColor;
                        break;
                    default:
                        tint = Color.white; // untinted: back to the authored sprite
                        break;
                }
                _joyBase.style.unityBackgroundImageTintColor = tint;
                if (_joyKnob != null) _joyKnob.style.unityBackgroundImageTintColor = tint;

                if (_joyGlyph != null)
                {
                    // Rooted alone gets a glyph. A stunned player already has a red cross
                    // on every ability hex, which is the stronger read for "you can do
                    // nothing"; a second mark on the stick would be noise.
                    string glyph = state == ControlState.Rooted ? HudFeedbackStyle.RootGlyph : string.Empty;
                    if (_joyGlyph.text != glyph) _joyGlyph.text = glyph;
                    if (state == ControlState.Rooted)
                        _joyGlyph.style.color = FeedbackTuning.CanonicalRootColor;
                }

                _appliedSlowQuantity = int.MinValue;
            }

            if (state != ControlState.Slowed || _joySlowLabel == null) return;

            // ×0.45, not a countdown: SlowMultiplier is re-derived every tick from whatever
            // is currently touching the chicken and has no deadline to count down to (see
            // ChickenController.SlowMultiplier's remarks). The magnitude IS the number for
            // this state, by design.
            int quantity = Mathf.RoundToInt(slowMultiplier * 100f);
            if (quantity == _appliedSlowQuantity || !_textGateOpen) return;
            _appliedSlowQuantity = quantity;
            _joySlowLabel.text = string.Empty;   // icon + colour say "slowed"; a x0.45 means nothing to a kid (Phase 7 A12)
            _joySlowLabel.style.color = FeedbackTuning.CanonicalSlowColor;
        }

        /// <summary>
        /// §5.2's status strip: one row per <i>active</i> status, not per dominant state —
        /// stun / root / slow are independent and routinely co-occur, and a strip that
        /// showed only the dominant one would hide the fact that a root is still ticking
        /// under a stun.
        /// </summary>
        private void ApplyStatusStrip(int count)
        {
            if (_statusStrip == null) { StatusRowCount = 0; return; }
            StatusRowCount = count;

            var stripDisplay = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_statusStrip.style.display != stripDisplay) _statusStrip.style.display = stripDisplay;

            for (int i = 0; i < _statusRows.Length; i++)
            {
                var row = _statusRows[i];
                if (row.Root == null) continue;

                if (i >= count)
                {
                    if (row.Root.style.display != DisplayStyle.None)
                        row.Root.style.display = DisplayStyle.None;
                    _appliedRowKind[i] = HudStatusKind.None;
                    continue;
                }

                var data = _statusScratch[i];
                if (row.Root.style.display != DisplayStyle.Flex)
                    row.Root.style.display = DisplayStyle.Flex;

                bool hasBar = HudFeedbackStyle.HasDrainBar(data.Kind);

                if (_appliedRowKind[i] != data.Kind)
                {
                    _appliedRowKind[i]     = data.Kind;
                    _appliedRowQuantity[i] = int.MinValue;

                    Color c = StatusColor(data.Kind);
                    if (row.Glyph != null)
                    {
                        // Sprite glyph when the kind has one (class paints the background image, no text, no
                        // tint: the sprites are two-tone); text fallback otherwise.
                        foreach (var state in StateGlyphStyle.All)
                            row.Glyph.RemoveFromClassList(StateGlyphStyle.UssClassFor(state));
                        string glyphClass = HudFeedbackStyle.GlyphClassFor(data.Kind);
                        if (glyphClass != null)
                        {
                            row.Glyph.AddToClassList(glyphClass);
                            row.Glyph.text = string.Empty;
                        }
                        else
                        {
                            row.Glyph.text = HudFeedbackStyle.GlyphFor(data.Kind);
                            row.Glyph.style.color = c;
                        }
                    }
                    if (row.Name != null)
                    {
                        row.Name.text = HudFeedbackStyle.NameFor(data.Kind);
                        row.Name.style.color = c;
                    }
                    if (row.Value != null) row.Value.style.color = c;

                    if (row.Bar != null)
                    {
                        var barDisplay = hasBar ? DisplayStyle.Flex : DisplayStyle.None;
                        if (row.Bar.style.display != barDisplay) row.Bar.style.display = barDisplay;
                        if (hasBar)
                        {
                            // Same track/arc contrast ratio the world-space drain rings use,
                            // so "how much is left" out-reads "how much there was" identically
                            // in both places.
                            var track = c;
                            track.a *= FeedbackTuning.DrainRingTrackAlphaMultiplier;
                            row.Bar.style.backgroundColor = track;
                            if (row.BarFill != null) row.BarFill.style.backgroundColor = c;
                        }
                    }
                }

                if (hasBar && row.BarFill != null)
                {
                    int k = (int)data.Kind;
                    if (data.Value > _statusPeak[k]) _statusPeak[k] = data.Value;
                    float frac = _statusPeak[k] > 0f ? Mathf.Clamp01(data.Value / _statusPeak[k]) : 0f;
                    // Per-frame is fine here: a width is a cheap style value and, unlike a
                    // text write, does not trigger a text relayout.
                    row.BarFill.style.width = new Length(frac * 100f, LengthUnit.Percent);
                }

                if (row.Value == null) continue;

                int quantity = data.Kind == HudStatusKind.Slow
                    ? Mathf.RoundToInt(data.Value * 100f)     // hundredths of a multiplier
                    : Mathf.CeilToInt(data.Value * 10f);      // tenths of a second

                if (quantity == _appliedRowQuantity[i] || !_textGateOpen) continue;
                _appliedRowQuantity[i] = quantity;
                // Slow shows no number (icon + bar). Seconds are invariant-culture: "1,2s" on an es-ES phone is a bug.
                row.Value.text = data.Kind != HudStatusKind.Slow && quantity > 0
                    ? HudFeedbackStyle.Seconds(quantity)
                    : string.Empty;
            }
        }

        private static Color StatusColor(HudStatusKind kind)
        {
            switch (kind)
            {
                case HudStatusKind.Stun: return FeedbackTuning.CanonicalStunColor;
                case HudStatusKind.Root: return FeedbackTuning.CanonicalRootColor;
                case HudStatusKind.Slow: return FeedbackTuning.CanonicalSlowColor;
                default:                 return Color.white;
            }
        }

        // ---- Equipped-ability swap ---------------------------------------------

        private void ApplyEquipped(int slot, AbilityBaseSO equipped)
        {
            var refs = _slots[slot];
            // J3: a re-equipped hex starts from its whole icon; RefreshEggPhase re-applies the egg class if the new slot needs it.
            _eggIconShown[slot] = false;
            refs.Icon?.RemoveFromClassList(EggPhaseHexRules.EggIconClass);
            CategoryMark.Apply(refs.Mark, equipped != null ? equipped.Category : (AbilityCategory?)null);

            // Icon sprite via USS class (swap the previously applied one). Every mapped
            // ability has a sprite (AbilityIconArtTests), so the icon alone identifies it and
            // the short label is only the safety net for a type with no AbilityIconStyle entry.
            string cls = CluckWars.UI.AbilityIconStyle.ClassFor(equipped);
            bool hasSprite = !string.IsNullOrEmpty(cls);
            if (refs.Icon != null)
            {
                if (!string.IsNullOrEmpty(_appliedIconCls[slot]))
                    refs.Icon.RemoveFromClassList(_appliedIconCls[slot]);
                if (hasSprite) refs.Icon.AddToClassList(cls);
                refs.Icon.style.display = hasSprite ? DisplayStyle.Flex : DisplayStyle.None;
                _appliedIconCls[slot] = cls;
            }
            if (equipped != null && !hasSprite && _reportedMissingIcons.Add(equipped.IconId))
                _log?.Warn(Source, $"Ability '{equipped.name}' ({equipped.IconId}) has no AbilityIconStyle entry; " +
                                   "its hex shows the short label instead of an icon.");

            if (refs.Label != null)
            {
                refs.Label.text = equipped != null && !hasSprite
                    ? (string.IsNullOrEmpty(equipped.ShortLabel) ? equipped.DisplayName : equipped.ShortLabel)
                    : string.Empty;
                refs.Label.style.display = hasSprite ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// The ability in <paramref name="slot"/>, or null. A <c>switch</c> with an explicit
        /// default rather than a chained ternary: the ternary's trailing else silently mapped
        /// slot 3 onto Slot2, so the fourth hex drew the third ability's icon, label and
        /// cooldown constant while its timer and refusal state came from the real slot 3.
        /// </summary>
        private static AbilityBaseSO SlotAbility(AbilityController abilities, int slot) => slot switch
        {
            0 => abilities.Slot0,
            1 => abilities.Slot1,
            2 => abilities.Slot2,
            3 => abilities.Slot3,
            _ => null,
        };

        private static ChickenController FindLocalChicken() => ChickenController.Local;
    }
}
