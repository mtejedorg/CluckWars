using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// <c>cw_warriorshots</c> (dev tools): photographs the Warrior kits' feedback (BL-227) the first time each state is on screen, one PNG per state, into the
    /// shot folder. As the Enforcer (<c>-cw_perk Warrior_Enforcer -cw_personality fighter</c>): <c>warrior-fresh</c> (the FRESH pill beside a rival),
    /// <c>warrior-pips</c> (a rival owing two or more), <c>warrior-hex-amount</c> (the Steal hex showing the food the next Steal takes, Steal ready),
    /// <c>warrior-pop-debt</c>, <c>warrior-pop-fresh</c> and <c>warrior-pop-cash</c> (0.25 s after the event). As the Bully (<c>-cw_perk Warrior_Bully</c>):
    /// <c>warrior-lock</c> and <c>warrior-pop-take</c>. As anyone with an Enforcer bot (<c>-cw_botperks Warrior_Enforcer,...</c>): <c>warrior-owe</c> (the
    /// victim's YOU OWE cue). Photographs only: nothing here plays for the player except the one hook below.
    /// </summary>
    /// <remarks>
    /// <b>Dev hook, said out loud:</b> a victim is only ever hit by a bot Enforcer by chance, so after 6 s of a round, with no <c>warrior-owe</c> yet, the harness
    /// has one bot Enforcer report a Stun hit on the local chicken through <see cref="EnforcerDebts.OnHit"/> (the public call its own Stun makes, authority
    /// only: in a solo match this peer owns every bot), every 4 s until the cue has been photographed. The debt is real, replicated and decays as any. Logged
    /// as <c>AutoTest warriorshots dev-hook</c>.
    /// </remarks>
    public sealed partial class AutoTestDriver
    {
        private bool _wsFresh, _wsPips, _wsHex, _wsLock, _wsOwe, _wsPopTake, _wsPopDebt, _wsPopFresh, _wsPopCash;
        private float _wsGoAt = -1f, _wsHookAt = -10f, _wsTakeAt = -1f, _wsDebtAt = -1f, _wsFreshAt = -1f, _wsCashAt = -1f;
        private ChickenController _wsBound;
        private const float WarriorPopShotDelay = 0.25f;

        private void TickWarriorShots(GameManager gm)
        {
            if (!_cfg.WarriorShots || !DevTools.Available || gm == null) return;
            var self = LocalChicken();
            if (self == null || self.Abilities == null) return;
            BindWarriorShots(self);
            EnsureMarkShotDir();

            float now = Time.realtimeSinceStartup;
            if (!gm.IsMatchRunning)
            {
                _wsGoAt = -1f;
                return;
            }
            if (_wsGoAt < 0f) _wsGoAt = now;

            // What the HUD really drew this frame, not what the state says: a fresh rival off the panel has no pill.
            if (MarkHudController.FreshPillShownNow && Settled("ws-fresh")) Capture(ref _wsFresh, "warrior-fresh");
            if (MarkHudController.MostDebtsShownNow >= 2 && Settled("ws-pips")) Capture(ref _wsPips, "warrior-pips");
            if (MarkHudController.LockShownNow && Settled("ws-lock")) Capture(ref _wsLock, "warrior-lock");
            // Short on purpose: the fighter presses a ready Steal the moment a carrier is in reach, so the number is on screen for a few frames.
            if (CluckWars.Input.TouchControlsController.StealAmountShownNow > 0 && Settled("ws-hex", 2)) Capture(ref _wsHex, "warrior-hex-amount");
            if (MarkHudController.OweCueShownNow && Settled("ws-owe")) Capture(ref _wsOwe, "warrior-owe");

            PopShot(ref _wsTakeAt, ref _wsPopTake, "warrior-pop-take", now);
            PopShot(ref _wsDebtAt, ref _wsPopDebt, "warrior-pop-debt", now);
            PopShot(ref _wsFreshAt, ref _wsPopFresh, "warrior-pop-fresh", now);
            PopShot(ref _wsCashAt, ref _wsPopCash, "warrior-pop-cash", now);

            if (!_wsOwe && now - _wsGoAt > 6f && now >= _wsHookAt)
            {
                _wsHookAt = now + 4f;
                ForceOneDebtOn(self);
            }
        }

        private void PopShot(ref float eventAt, ref bool taken, string name, float now)
        {
            if (eventAt < 0f || now - eventAt < WarriorPopShotDelay) return;
            if (_capturedFrame == Time.frameCount) return;   // one capture per frame: this one waits a frame
            eventAt = -1f;
            Capture(ref taken, name);
        }

        private void BindWarriorShots(ChickenController self)
        {
            if (_wsBound == self) return;
            _wsBound = self;
            var e = self.LocalEvents;
            e.HitStealTaken += _ => { if (!_wsPopTake) _wsTakeAt = Time.realtimeSinceStartup; };
            e.DebtGained += _ => { if (!_wsPopDebt) _wsDebtAt = Time.realtimeSinceStartup; };
            e.FreshHit += _ => { if (!_wsPopFresh) _wsFreshAt = Time.realtimeSinceStartup; };
            e.DebtCashed += _ => { if (!_wsPopCash) _wsCashAt = Time.realtimeSinceStartup; };
        }

        private void ForceOneDebtOn(ChickenController self)
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == self || c.IsDecoy || c.Debts == null || !c.Debts.Facts.IsEnforcer || !c.Debts.HasStateAuthority) continue;
                c.Debts.OnHit(self, AbilityJob.Stun, true);
                _log.Info(Source, $"AutoTest warriorshots dev-hook: {c.name} reports a Stun hit on the local chicken through EnforcerDebts.OnHit (a bot Enforcer rarely picks the local one).");
                return;
            }
        }
    }
}
