using CluckWars.Localization;
using CluckWars.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The Warrior kits' half of the mark HUD (BL-227, design 2026-11-25 section 16 slice G9): a tag beside each rival's nameplate for the local Enforcer (the FRESH
    /// pill while the rival is fresh for it, the debt pips with their decay bar) or the local Bully (the lock while a second hit would take nothing), and the
    /// local victim's own YOU OWE cue. Draws only <see cref="WarriorFeedback"/>'s outputs; every decision (who sees what, how full a bar is) is its and
    /// <see cref="WarriorFeedbackRules"/>'s. Placement is the nameplate's own viewport rect (a rival whose plate is off the panel gets none).
    /// </summary>
    public sealed partial class MarkHudController
    {
        /// <summary>Tag boxes in MarkHud.uxml: a match holds at most three rivals; the fourth is slack.</summary>
        private const int WarriorTagSlots = 4;

        private struct WarriorTagRefs
        {
            public VisualElement Root, Fresh, Debt, Lock, FreshFill, DebtFill, LockFill;
            public VisualElement[] Pips;
            public float AppliedFresh, AppliedDebt, AppliedLock;
            public int AppliedSlots, AppliedOn;
            public bool AppliedFreshLow;
        }

        /// <summary>Diagnostics (the <c>-cw_warriorshots</c> harness): what this frame really drew. A state the state says is on can be off the panel.</summary>
        public static bool FreshPillShownNow { get; private set; }
        public static bool DebtPipsShownNow { get; private set; }
        public static bool LockShownNow { get; private set; }
        public static bool OweCueShownNow { get; private set; }
        /// <summary>The most debts any drawn rival tag showed this frame.</summary>
        public static int MostDebtsShownNow { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarriorStatics() => FreshPillShownNow = DebtPipsShownNow = LockShownNow = OweCueShownNow = false;

        private readonly WarriorTagRefs[] _wTags = new WarriorTagRefs[WarriorTagSlots];
        private VisualElement _wOwe, _wOweBar;
        private VisualElement[] _wOwePips;
        private Label _wOweLabel;
        private bool _warriorBound, _reportedWarriorMissing;
        private int _oweShownCount = -1, _oweAppliedSlots = -1, _oweAppliedOn = -1;
        private float _oweAppliedFill = -1f;
        private Color _oweShownColour;

        private void BindWarrior(VisualElement root)
        {
            bool ok = true;
            for (int i = 0; i < WarriorTagSlots; i++)
            {
                ref var t = ref _wTags[i];
                t.Root = root.Q<VisualElement>("WarriorTag" + i);
                t.Fresh = t.Root?.Q<VisualElement>(className: "cw-wtag-fresh");
                t.Debt = t.Root?.Q<VisualElement>(className: "cw-wtag-debt");
                t.Lock = t.Root?.Q<VisualElement>(className: "cw-wtag-lock");
                t.FreshFill = t.Fresh?.Q<VisualElement>(className: "cw-wbar-fill");
                t.DebtFill = t.Debt?.Q<VisualElement>(className: "cw-wbar-fill");
                t.LockFill = t.Lock?.Q<VisualElement>(className: "cw-wbar-fill");
                t.Pips = t.Debt != null ? t.Debt.Query<VisualElement>(className: "cw-wpip").ToList().ToArray() : null;
                var label = t.Fresh?.Q<Label>();
                if (t.Root == null || t.Fresh == null || t.Debt == null || t.Lock == null || t.FreshFill == null || t.DebtFill == null || t.LockFill == null ||
                    t.Pips == null || t.Pips.Length < FeedbackTuning.WarriorPipSlotsMax || label == null)
                {
                    ok = false;
                    break;
                }
                label.text = UiText.Get(UiKeys.HudBadgeFresh);
                t.FreshFill.style.backgroundColor = FeedbackTuning.WarriorFreshColor;
                t.Fresh.style.borderLeftColor = t.Fresh.style.borderRightColor = t.Fresh.style.borderTopColor = t.Fresh.style.borderBottomColor = FeedbackTuning.WarriorFreshColor;
                t.DebtFill.style.backgroundColor = FeedbackTuning.WarriorDebtColor;
                t.LockFill.style.backgroundColor = FeedbackTuning.WarriorLockColor;
                t.AppliedSlots = t.AppliedOn = -1;
                t.AppliedFresh = t.AppliedDebt = t.AppliedLock = -1f;
            }

            _wOwe = root.Q<VisualElement>("WarriorOwe");
            _wOweBar = root.Q<VisualElement>("WarriorOweBar");
            _wOweLabel = root.Q<Label>("WarriorOweLabel");
            _wOwePips = _wOwe != null ? _wOwe.Query<VisualElement>(className: "cw-wpip").ToList().ToArray() : null;
            if (_wOwe == null || _wOweBar == null || _wOweLabel == null || _wOwePips == null || _wOwePips.Length < FeedbackTuning.WarriorPipSlotsMax) ok = false;

            if (!ok)
            {
                if (!_reportedWarriorMissing)
                {
                    _reportedWarriorMissing = true;
                    _log?.Error(Source, "MarkHud.uxml is missing a Warrior element (WarriorTag0-3 with their fresh pill, debt row, lock and " + FeedbackTuning.WarriorPipSlotsMax +
                        " pips each, or WarriorOwe, WarriorOweLabel, WarriorOweBar): the Enforcer's and Bully's rival tags and the victim's YOU OWE cue are not drawn.");
                }
                return;
            }
            _wOweBar.style.backgroundColor = FeedbackTuning.WarriorDebtColor;
            _warriorBound = true;
            HideWarrior();
        }

        private void HideWarrior()
        {
            FreshPillShownNow = DebtPipsShownNow = LockShownNow = OweCueShownNow = false;
            MostDebtsShownNow = 0;
            if (!_warriorBound) return;
            for (int i = 0; i < WarriorTagSlots; i++) UiElements.SetShown(_wTags[i].Root, false);
            UiElements.SetShown(_wOwe, false);
        }

        private void UpdateWarrior(Camera cam, Vector2 panel)
        {
            if (!_warriorBound) return;
            FreshPillShownNow = DebtPipsShownNow = LockShownNow = false;
            MostDebtsShownNow = 0;
            var o = WarriorFeedback.Current;
            int n = WarriorFeedback.TagCount;
            for (int i = 0; i < WarriorTagSlots; i++)
            {
                if (i < n) UpdateWarriorTag(ref _wTags[i], WarriorFeedback.TagAt(i), in o, cam, panel);
                else UiElements.SetShown(_wTags[i].Root, false);
            }
            UpdateOwe(in o, cam, panel);
        }

        private void UpdateWarriorTag(ref WarriorTagRefs t, in WarriorTag tag, in WarriorOutputs o, Camera cam, Vector2 panel)
        {
            var rival = tag.Rival;   // Unity null once it despawned
            var plate = rival != null ? rival.Nameplate() : null;
            if (plate == null || !plate.TryGetPlateViewport(cam, out Rect vp))
            {
                UiElements.SetShown(t.Root, false);
                return;
            }
            Rect at = MarkerPlacement.ToPanel(vp, panel);
            if (!OnPanel(at.center, panel))
            {
                UiElements.SetShown(t.Root, false);   // an off-screen rival has no plate to sit beside: the rival arrows already point at it
                return;
            }
            UiElements.SetShown(t.Root, true);

            UiElements.SetShown(t.Fresh, tag.FreshShown);
            if (tag.FreshShown)
            {
                FreshPillShownNow = true;
                SetFill(t.FreshFill, tag.FreshFill, ref t.AppliedFresh);
                bool low = tag.FreshFill < FeedbackTuning.WarriorFreshLowFill;
                if (low != t.AppliedFreshLow)
                {
                    t.AppliedFreshLow = low;
                    t.Fresh.style.opacity = low ? FeedbackTuning.WarriorFreshLowOpacity : 1f;
                }
            }

            bool debts = tag.Debts > 0;
            UiElements.SetShown(t.Debt, debts);
            if (debts)
            {
                DebtPipsShownNow = true;
                MostDebtsShownNow = Mathf.Max(MostDebtsShownNow, tag.Debts);
                SetPips(t.Pips, o.TagPipSlots, tag.Debts, ref t.AppliedSlots, ref t.AppliedOn);
                SetFill(t.DebtFill, tag.DecayFill, ref t.AppliedDebt);
            }

            UiElements.SetShown(t.Lock, tag.LockShown);
            if (tag.LockShown)
            {
                LockShownNow = true;
                SetFill(t.LockFill, tag.LockFill, ref t.AppliedLock);
            }

            // Beside the rival's plate (the stack above it is the status badges' and the pops'): to its right, or to its left when the panel ends there.
            float w = t.Root.resolvedStyle.width > 0f ? t.Root.resolvedStyle.width : 150f;
            float h = t.Root.resolvedStyle.height > 0f ? t.Root.resolvedStyle.height : 110f;
            float x = at.xMax + FeedbackTuning.WarriorTagGapPx;
            if (x + w > panel.x) x = at.xMin - FeedbackTuning.WarriorTagGapPx - w;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, panel.x - w));
            float y = Mathf.Clamp(at.center.y - h * 0.5f, 0f, Mathf.Max(0f, panel.y - h));
            t.Root.style.translate = new Translate(x, y);
        }

        private void UpdateOwe(in WarriorOutputs o, Camera cam, Vector2 panel)
        {
            var local = LocalChicken();
            var plate = local != null ? local.Nameplate() : null;
            if (o.OweCount <= 0 || plate == null || !plate.TryGetPlateViewport(cam, out Rect vp))
            {
                UiElements.SetShown(_wOwe, false);
                _oweShownCount = -1;
                OweCueShownNow = false;
                return;
            }
            UiElements.SetShown(_wOwe, true);
            OweCueShownNow = true;
            if (o.OweCount != _oweShownCount)
            {
                _oweShownCount = o.OweCount;
                _wOweLabel.text = UiText.Format(UiKeys.HudBadgeOwe, ("n", o.OweCount));
            }
            if (o.OweColor != _oweShownColour)
            {
                _oweShownColour = o.OweColor;
                _wOwe.style.borderLeftColor = _wOwe.style.borderRightColor = _wOwe.style.borderTopColor = _wOwe.style.borderBottomColor = o.OweColor;
            }
            SetPips(_wOwePips, o.OwePipSlots, o.OweCount, ref _oweAppliedSlots, ref _oweAppliedOn);
            SetFill(_wOweBar, o.OweFill, ref _oweAppliedFill);

            Rect you = MarkerPlacement.ToPanel(vp, panel);
            float w = _wOwe.resolvedStyle.width > 0f ? _wOwe.resolvedStyle.width : 190f;
            float h = _wOwe.resolvedStyle.height > 0f ? _wOwe.resolvedStyle.height : 100f;
            // Beside the "You" plate (above it sit the status badges, the pops and the Thief's loot badge), to its right, or its left when the panel ends there.
            float x = you.xMax + FeedbackTuning.WarriorTagGapPx;
            if (x + w > panel.x) x = you.xMin - FeedbackTuning.WarriorTagGapPx - w;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, panel.x - w));
            float y = Mathf.Clamp(you.center.y - h * 0.5f, 0f, Mathf.Max(0f, panel.y - h));
            _wOwe.style.translate = new Translate(x, y);
        }

        /// <summary>Is a panel point inside the panel (a plate outside it belongs to a rival that is off screen).</summary>
        private static bool OnPanel(Vector2 p, Vector2 panel) => p.x >= 0f && p.y >= 0f && p.x <= panel.x && p.y <= panel.y;

        /// <summary>A draining bar's width in percent, written only when it moved by a visible step.</summary>
        private static void SetFill(VisualElement fill, float fill01, ref float applied)
        {
            if (Mathf.Abs(fill01 - applied) < 0.01f) return;
            applied = fill01;
            fill.style.width = new Length(fill01 * 100f, LengthUnit.Percent);
        }

        /// <summary>The drawn pips (the cap) and the filled ones (the held count), rewritten only when either changes.</summary>
        private static void SetPips(VisualElement[] pips, int slots, int held, ref int appliedSlots, ref int appliedOn)
        {
            slots = WarriorFeedbackRules.PipSlots(slots, pips.Length);
            int on = WarriorFeedbackRules.PipsFilled(held, slots);
            if (slots == appliedSlots && on == appliedOn) return;
            appliedSlots = slots;
            appliedOn = on;
            for (int k = 0; k < pips.Length; k++)
            {
                UiElements.SetShown(pips[k], k < slots);
                pips[k].style.backgroundColor = k < on ? new StyleColor(FeedbackTuning.WarriorDebtColor) : new StyleColor(StyleKeyword.Null);
            }
        }
    }
}
