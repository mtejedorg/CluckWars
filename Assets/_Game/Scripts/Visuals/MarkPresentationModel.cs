using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>The local player's part in the Death Mark the HUD is showing.</summary>
    public enum MarkRole : byte
    {
        /// <summary>No mark involves this view (or none is alive).</summary>
        None = 0,
        /// <summary>The local player placed the mark.</summary>
        Assassin = 1,
        /// <summary>The local player is the mark.</summary>
        Victim = 2,
        /// <summary>A mark between two other players.</summary>
        ThirdParty = 3,
    }

    /// <summary>What the Assassin's hunt marker says.</summary>
    public enum MarkMarkerMode : byte
    {
        /// <summary>Find it (target's player colour). Stun and close-in look the same.</summary>
        Hunt = 0,
        /// <summary>A press kills now (yellow, exactly while <c>AssassinExecute.CanKillNow</c>).</summary>
        Kill = 1,
        /// <summary>A base wall stands between them (grey + lock badge); the ring keeps draining.</summary>
        Walled = 2,
        /// <summary>The target is shelled (steal-proof) and cannot be executed (cream + shell badge); the ring keeps draining.</summary>
        Shelled = 3,
    }

    /// <summary>One-shot cues of a frame, edge-triggered (several can fire on one frame).</summary>
    [System.Flags]
    public enum MarkEvents : byte
    {
        None = 0,
        /// <summary>The draw highlight stepped (<see cref="MarkOutputs.DrawStep"/> says which step).</summary>
        DrawTick = 1,
        /// <summary>The draw landed on the target.</summary>
        Lock = 2,
        /// <summary>The local player was marked (fires with the toast, after intro / GO).</summary>
        Marked = 4,
        /// <summary><c>CanKillNow</c> went false to true (rate-limited).</summary>
        KillReady = 8,
    }

    /// <summary>Everything the model needs for one frame, gathered by <see cref="MarkFeedback"/> from replicated state.
    /// No UnityEngine object: EditMode tests build it by hand.</summary>
    public struct MarkInputs
    {
        /// <summary>The round is being played (not intro, not ended, not the waiting room). False ends any mark.</summary>
        public bool MatchRunning;
        public bool ReducedMotion;
        /// <summary>The intro and the GO flourish are over (<c>EventBannerTiming.CanShow</c>): the victim's toast may show.</summary>
        public bool BannerMayShow;

        /// <summary>None = no live mark involves this view (expired, killed, target or hunter gone, local despawned...).</summary>
        public MarkRole Role;
        /// <summary>Identity of the live mark (hunter, target, window end tick): the same mark found again (host migration)
        /// never replays its draw, toast or stings; a different key is a new mark.</summary>
        public long MarkKey;
        public int TargetCorner;
        public int HunterCorner;
        /// <summary>Seconds left in the mark's window, and the window's length.</summary>
        public float Remaining;
        public float Window;
        /// <summary>The mark's arm time (<c>MarkKill.asset</c>): the draw never outlasts it.</summary>
        public float ArmSeconds;

        // ---- Assassin --------------------------------------------------------------------------------------------
        /// <summary>Planar metres from the local Assassin to its target.</summary>
        public float Distance;
        public bool CanKillNow;
        /// <summary>The target stands inside the equipped Ambush's own shape (its <c>WouldAffect</c>), and the slot is ready.</summary>
        public bool AmbushReadyOnTarget;
        /// <summary>A base wall between the Assassin and the target (<c>PlayerBase.WallBetween</c>).</summary>
        public bool Walled;
        /// <summary>The target is shelled: steal-proof, so <c>ExecuteRules.CanBeExecuted</c> refuses it.</summary>
        public bool TargetShelled;

        // ---- Victim ----------------------------------------------------------------------------------------------
        /// <summary>The hunter is invisible to the victim (Invisibility / Smoke Roost fade).</summary>
        public bool HunterInvisible;
        /// <summary>The hunter is outside the victim's clear viewport (the rival indicator's own hidden decision).</summary>
        public bool HunterHiddenFromView;
        /// <summary>Planar metres from the hunter to the victim, and the inner-circle radius (the floor-arrow ring's).</summary>
        public float HunterDistance;
        public float InnerRadius;
    }

    /// <summary>What every Death Mark presentation layer draws this frame. Already faded: a layer applies it as given.</summary>
    public struct MarkOutputs
    {
        /// <summary>The role being shown (kept through the end fade; None once clean).</summary>
        public MarkRole Role;
        public int TargetCorner;
        public int HunterCorner;
        /// <summary>A live mark the local player placed (false from the first frame of the end fade): the range guides show
        /// only Ambush's while it holds.</summary>
        public bool AssassinMarkActive;

        // Hunt marker (Assassin).
        public float MarkerAlpha;
        /// <summary>Scale of the appear pop (0.85 to 1 over 0.15 s at lock), 1 otherwise.</summary>
        public float MarkerPop;
        public MarkMarkerMode MarkerMode;
        /// <summary>Remaining / window: the ring drains clockwise from 12 o'clock.</summary>
        public float MarkerFill01;
        public int MarkerMetres;

        // Standings.
        public int SkullCorner;
        public float SkullAlpha;

        // Draw highlight (Assassin): step <see cref="DrawStep"/> of <see cref="DrawSteps"/>, the last one is the target's row.
        public float DrawAlpha;
        public int DrawStep;
        public int DrawSteps;
        public bool DrawLocked;
        /// <summary>The lock pop's scale (1 to 1.06 and back over 0.15 s), 1 otherwise.</summary>
        public float DrawScale;
        /// <summary>The draw is running or holding its lock: the compact phone board shows every row.</summary>
        public bool DrawShowsAllRows;

        // Hexes (Assassin).
        public bool AmbushLit;
        public bool KillHex;

        // Victim.
        public float ToastAlpha;
        public float ToastScale;
        public float ClockAlpha;
        public int ClockSeconds;
        public float Dim, Cold, Red;
        /// <summary>The hunter's rival arrow carries the skull badge, and shows even while the hunter is invisible.</summary>
        public float HunterBadgeAlpha;
        public bool ForceShowHunter;

        public MarkEvents Events;

        /// <summary>Nothing shown at all.</summary>
        public bool IsClean =>
            Role == MarkRole.None && !AssassinMarkActive && MarkerAlpha == 0f && SkullAlpha == 0f && DrawAlpha == 0f &&
            !AmbushLit && !KillHex && ToastAlpha == 0f && ClockAlpha == 0f && Dim == 0f && Cold == 0f && Red == 0f &&
            HunterBadgeAlpha == 0f && !ForceShowHunter && Events == MarkEvents.None;
    }

    /// <summary>One living chicken as the mark resolver sees it, copied from replicated state each frame (decoys left out).</summary>
    public struct MarkChicken
    {
        /// <summary>Its behaviour id (never 0 for a real chicken).</summary>
        public long Id;
        public int Corner;
        /// <summary>The local player's own chicken.</summary>
        public bool IsLocal;
        /// <summary>Dead, eliminated or being executed (<c>ChickenCombat.IsRemoved</c>).</summary>
        public bool Removed;
        /// <summary>The id its <c>AssassinExecute.MarkedTarget</c> names, 0 for none.</summary>
        public long MarkedTargetId;
        /// <summary>Its mark's window end tick (part of the mark's identity).</summary>
        public int WindowEndTick;
        /// <summary>Planar position (x, z).</summary>
        public Vector2 Position;
    }

    /// <summary>Which mark the local HUD shows and in what role, pure: the Assassin's own first, then a mark on the local
    /// player (the hunter already shown, else the nearest), then any other (the one already shown, else the lowest id).</summary>
    public static class MarkRoleRules
    {
        public struct Resolved
        {
            public MarkRole Role;
            public int Hunter;   // index into the list, -1 for none
            public int Target;
            public int Local;
            public long Key;
        }

        public static Resolved Resolve(System.Collections.Generic.IReadOnlyList<MarkChicken> all, long shownHunterId)
        {
            var r = new Resolved { Role = MarkRole.None, Hunter = -1, Target = -1, Local = -1 };
            if (all == null) return r;
            for (int i = 0; i < all.Count; i++) if (all[i].IsLocal) { r.Local = i; break; }
            if (r.Local < 0) return r;                                   // local player not spawned / despawned
            var local = all[r.Local];

            // The local player's own mark.
            if (local.MarkedTargetId != 0)
            {
                int t = IndexOf(all, local.MarkedTargetId);
                if (t >= 0 && !all[t].Removed && !local.Removed) return With(r, MarkRole.Assassin, r.Local, t, all);
            }

            // Marked: the hunter already shown, else the nearest.
            if (!local.Removed)
            {
                int best = -1;
                float bestSqr = float.MaxValue;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (i == r.Local || c.Removed || c.MarkedTargetId != local.Id) continue;
                    if (c.Id == shownHunterId) { best = i; break; }
                    float d = (c.Position - local.Position).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = i; }
                }
                if (best >= 0) return With(r, MarkRole.Victim, best, r.Local, all);
            }

            // Somebody else's mark.
            int hunter = -1;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (i == r.Local || c.Removed || c.MarkedTargetId == 0) continue;
                int t = IndexOf(all, c.MarkedTargetId);
                if (t < 0 || all[t].Removed || t == r.Local) continue;
                if (c.Id == shownHunterId) { hunter = i; break; }
                if (hunter < 0 || c.Id < all[hunter].Id) hunter = i;
            }
            if (hunter >= 0) return With(r, MarkRole.ThirdParty, hunter, IndexOf(all, all[hunter].MarkedTargetId), all);
            return r;
        }

        /// <summary>A mark's identity: hunter, target and window end. The same mark found again keeps it; a new one never has it.</summary>
        public static long KeyOf(long hunterId, long targetId, int windowEndTick)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                h = (h ^ hunterId) * 1099511628211L;
                h = (h ^ targetId) * 1099511628211L;
                h = (h ^ windowEndTick) * 1099511628211L;
                return h == 0 ? 1 : h;
            }
        }

        private static Resolved With(Resolved r, MarkRole role, int hunter, int target, System.Collections.Generic.IReadOnlyList<MarkChicken> all)
        {
            r.Role = role;
            r.Hunter = hunter;
            r.Target = target;
            r.Key = KeyOf(all[hunter].Id, all[target].Id, all[hunter].WindowEndTick);
            return r;
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<MarkChicken> all, long id)
        {
            for (int i = 0; i < all.Count; i++) if (all[i].Id == id) return i;
            return -1;
        }
    }

    /// <summary>
    /// The Death Mark's presentation as a pure state machine (Maestro's 2026-11 redesign): one <see cref="Step"/> per frame
    /// from <see cref="MarkInputs"/> to <see cref="MarkOutputs"/>. No Unity object, no networking: everything it reads was
    /// gathered from replicated state by <see cref="MarkFeedback"/>, and everything it decides is local presentation.
    /// </summary>
    /// <remarks>
    /// <para>Endings are one rule: the moment the inputs stop describing a live mark, for any reason, every output fades to 0
    /// over <see cref="FeedbackTuning.MarkEndFadeSeconds"/> (overriding the cold-exit delay), no event fires, and then the model
    /// is exactly clean. A different mark arriving during the fade starts clean; the same mark found again (host migration)
    /// resumes without replaying its draw, toast or stings.</para>
    /// <para>Cold hysteresis: enters at distance &lt;= R, leaves only after &gt; <see cref="FeedbackTuning.MarkColdExitFactor"/> R
    /// continuously for <see cref="FeedbackTuning.MarkColdExitDelaySeconds"/>, so nobody walking the edge makes it flicker.</para>
    /// </remarks>
    public sealed class MarkPresentationModel
    {
        // The mark being shown.
        private bool _showing;            // a mark is shown (live, or in its end fade)
        private bool _ending;             // in the end fade
        private float _endT;              // seconds into the end fade
        private long _key;
        private MarkRole _role;
        private int _targetCorner, _hunterCorner;
        private float _age;               // seconds since this mark was first shown

        // Assassin.
        private float _drawSeconds;
        private int _drawSteps;
        private int _lastStep;
        private bool _locked;
        private float _sinceLock;
        private bool _prevCanKill;
        private float _sinceKillSting = float.PositiveInfinity;
        private MarkMarkerMode _mode;
        private float _fill;
        private int _metres;

        // Victim.
        private bool _toastDone;
        private float _toastT = -1f;      // < 0: not started
        private float _dim, _cold, _red;
        private bool _coldOn;
        private float _beyondExit;        // seconds continuously beyond the exit radius
        private int _clockSeconds;
        private float _sinceSkull;

        // Memory of the last marks announced (several Death Mark carriers can take turns on this view), so the same mark
        // found again never replays its draw, toast or stings.
        public const int AnnouncedMemory = 4;
        private readonly long[] _announced = new long[AnnouncedMemory];
        private int _announcedCount, _announcedNext;

        private MarkOutputs _out;
        private MarkOutputs _snapshot;

        /// <summary>The last outputs <see cref="Step"/> produced.</summary>
        public MarkOutputs Current => _out;

        /// <summary>Drop whatever is shown, at once (scene unload, disable). The announced-mark memory survives: only
        /// <see cref="ForgetAnnounced"/> (a new match: mark keys restart with the ticks) clears it.</summary>
        public void Reset()
        {
            ClearShown();
            _out = default;
        }

        /// <summary>A new match: its mark keys can repeat the last match's, so nothing counts as announced any more.</summary>
        public void ForgetAnnounced()
        {
            _announcedCount = _announcedNext = 0;
        }

        private bool WasAnnounced(long key)
        {
            for (int i = 0; i < _announcedCount; i++) if (_announced[i] == key) return true;
            return false;
        }

        private void RememberAnnounced(long key)
        {
            if (WasAnnounced(key)) return;
            _announced[_announcedNext] = key;
            _announcedNext = (_announcedNext + 1) % AnnouncedMemory;
            if (_announcedCount < AnnouncedMemory) _announcedCount++;
        }

        public MarkOutputs Step(in MarkInputs input, float dt)
        {
            dt = Mathf.Max(0f, dt);
            bool live = input.MatchRunning && input.Role != MarkRole.None;

            bool switching = live && _showing && input.MarkKey != _key;
            if (switching && _ending) { ClearShown(); switching = false; }   // the old one was already fading: start the new one clean
            if (live && !_showing) Begin(input);
            else if (live && _ending) _ending = false;                       // the same mark came back mid-fade
            // A different live mark (a role switch, another hunter): the old one fades out first, then the new one begins.

            var o = new MarkOutputs { MarkerPop = 1f, DrawScale = 1f, ToastScale = 1f, SkullCorner = -1, TargetCorner = -1, HunterCorner = -1 };
            if (!_showing) { _out = o; return o; }

            if ((!live || switching) && !_ending)
            {
                _ending = true;
                _endT = 0f;
                _snapshot = _out;   // the last live frame: the fade freezes it
            }

            if (_ending)
            {
                _endT += dt;
                float f = 1f - Mathf.Clamp01(_endT / Mathf.Max(1e-4f, FeedbackTuning.MarkEndFadeSeconds));
                if (f <= 0f)
                {
                    ClearShown();
                    _out = o;
                    return o;
                }
                FillFrozen(ref o, f);
                _out = o;
                return o;
            }

            _age += dt;
            if (_role == MarkRole.Assassin) StepAssassin(input, dt, ref o);
            else if (_role == MarkRole.Victim) StepVictim(input, dt, ref o);
            StepSkull(dt, ref o);
            o.Role = _role;
            o.TargetCorner = _targetCorner;
            o.HunterCorner = _hunterCorner;
            _out = o;
            return o;
        }

        private void Begin(in MarkInputs input)
        {
            ClearShown();
            _showing = true;
            _key = input.MarkKey;
            _role = input.Role;
            _targetCorner = input.TargetCorner;
            _hunterCorner = input.HunterCorner;
            bool replay = WasAnnounced(_key);
            RememberAnnounced(_key);

            float arm = input.ArmSeconds > 0f ? input.ArmSeconds : FeedbackTuning.MarkDrawSeconds;
            _drawSeconds = DrawDuration(input.ReducedMotion, arm);
            _drawSteps = DrawStepCount(input.ReducedMotion, _drawSeconds);
            _lastStep = -1;
            _prevCanKill = input.CanKillNow;      // a re-found mark already kill-ready is not a new edge
            _sinceKillSting = float.PositiveInfinity;
            _sinceSkull = 0f;

            if (replay)
            {
                // The same mark again (host migration): straight to its settled state, nothing announced twice.
                _locked = true;
                _sinceLock = float.PositiveInfinity;
                _toastDone = true;
                _sinceSkull = float.PositiveInfinity;
                _age = _drawSeconds;
            }
        }

        private void ClearShown()
        {
            _showing = _ending = false;
            _endT = 0f;
            _key = 0;
            _role = MarkRole.None;
            _targetCorner = _hunterCorner = -1;
            _age = 0f;
            _drawSeconds = 0f;
            _drawSteps = 0;
            _lastStep = -1;
            _locked = false;
            _sinceLock = 0f;
            _prevCanKill = false;
            _sinceKillSting = float.PositiveInfinity;
            _mode = MarkMarkerMode.Hunt;
            _fill = 0f;
            _metres = 0;
            _toastDone = false;
            _toastT = -1f;
            _dim = _cold = _red = 0f;
            _coldOn = false;
            _beyondExit = 0f;
            _clockSeconds = 0;
            _sinceSkull = 0f;
        }

        /// <summary>The draw's length: the tuned 0.6 s sweep (0.3 s fade-in under reduced motion), never longer than the arm.</summary>
        public static float DrawDuration(bool reducedMotion, float armSeconds)
        {
            float want = reducedMotion ? FeedbackTuning.MarkDrawReducedFadeSeconds : FeedbackTuning.MarkDrawSeconds;
            return Mathf.Max(0f, Mathf.Min(want, armSeconds));
        }

        /// <summary>Highlight steps in a draw of <paramref name="drawSeconds"/>: one per <see cref="FeedbackTuning.MarkDrawStepSeconds"/>,
        /// at least one (the target's row); one under reduced motion (no sweep).</summary>
        public static int DrawStepCount(bool reducedMotion, float drawSeconds)
        {
            if (reducedMotion) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(drawSeconds / Mathf.Max(1e-3f, FeedbackTuning.MarkDrawStepSeconds)));
        }

        /// <summary>The marker's look, in the order the Kill press judges it: KILL, then a shell (nothing executes it), then a
        /// base wall, else hunting.</summary>
        public static MarkMarkerMode MarkerModeFor(bool canKill, bool shelled, bool walled) =>
            canKill ? MarkMarkerMode.Kill : shelled ? MarkMarkerMode.Shelled : walled ? MarkMarkerMode.Walled : MarkMarkerMode.Hunt;

        /// <summary>Seconds shown on the clock: rounds up, so "0" never shows while the mark is alive.</summary>
        public static int ShownSeconds(float remaining) => Mathf.CeilToInt(Mathf.Max(0f, remaining));

        // ---- Assassin ----------------------------------------------------------------------------------------------

        private void StepAssassin(in MarkInputs input, float dt, ref MarkOutputs o)
        {
            o.AssassinMarkActive = true;

            // The draw: a highlight stepping down the standings, landing on the target at the end.
            if (!_locked)
            {
                int step = _drawSteps <= 1 ? 0 : Mathf.Min(_drawSteps - 1, Mathf.FloorToInt(_age / Mathf.Max(1e-4f, _drawSeconds / _drawSteps)));
                if (!input.ReducedMotion && step != _lastStep) o.Events |= MarkEvents.DrawTick;
                _lastStep = step;
                if (_age >= _drawSeconds)
                {
                    _locked = true;
                    _sinceLock = 0f;
                    _sinceSkull = 0f;
                    o.Events |= MarkEvents.Lock;
                }
            }
            else
            {
                _sinceLock += dt;
            }

            o.DrawSteps = Mathf.Max(1, _drawSteps);
            o.DrawStep = _locked ? o.DrawSteps - 1 : Mathf.Max(0, _lastStep);
            o.DrawLocked = _locked;
            if (!_locked)
            {
                o.DrawAlpha = input.ReducedMotion ? Mathf.Clamp01(_age / Mathf.Max(1e-4f, _drawSeconds)) : 1f;
                o.DrawShowsAllRows = true;
            }
            else
            {
                float pop = FeedbackTuning.MarkDrawLockPopSeconds, fade = FeedbackTuning.MarkDrawFadeSeconds;
                if (_sinceLock < pop)
                {
                    o.DrawAlpha = 1f;
                    o.DrawScale = input.ReducedMotion ? 1f
                        : 1f + (FeedbackTuning.MarkDrawLockPopScale - 1f) * Mathf.Sin(Mathf.PI * _sinceLock / Mathf.Max(1e-4f, pop));
                }
                else
                {
                    o.DrawAlpha = 1f - Mathf.Clamp01((_sinceLock - pop) / Mathf.Max(1e-4f, fade));
                }
                o.DrawShowsAllRows = o.DrawAlpha > 0f;
            }

            // The marker, from the lock on.
            _mode = MarkerModeFor(input.CanKillNow, input.TargetShelled, input.Walled);
            _fill = input.Window > 0f ? Mathf.Clamp01(input.Remaining / input.Window) : 0f;
            _metres = Mathf.RoundToInt(Mathf.Max(0f, input.Distance));
            if (_locked)
            {
                o.MarkerAlpha = 1f;
                float popT = FeedbackTuning.MarkMarkerPopSeconds;
                o.MarkerPop = input.ReducedMotion || _sinceLock >= popT ? 1f
                    : Mathf.Lerp(FeedbackTuning.MarkMarkerPopFrom, 1f, _sinceLock / Mathf.Max(1e-4f, popT));
            }
            o.MarkerMode = _mode;
            o.MarkerFill01 = _fill;
            o.MarkerMetres = _metres;

            // Hexes: KILL exactly while a press would kill; Ambush lit only once the draw has told who the target is.
            o.KillHex = input.CanKillNow;
            o.AmbushLit = _locked && input.AmbushReadyOnTarget && !input.CanKillNow;

            _sinceKillSting += dt;
            if (input.CanKillNow && !_prevCanKill && _sinceKillSting >= FeedbackTuning.MarkKillReadyMinInterval)
            {
                o.Events |= MarkEvents.KillReady;
                _sinceKillSting = 0f;
            }
            _prevCanKill = input.CanKillNow;
        }

        // ---- Victim ------------------------------------------------------------------------------------------------

        private void StepVictim(in MarkInputs input, float dt, ref MarkOutputs o)
        {
            // Toast + sting, once per mark, after the intro / GO flourish.
            if (!_toastDone && _toastT < 0f && input.BannerMayShow)
            {
                _toastT = 0f;
                o.Events |= MarkEvents.Marked;
            }
            if (_toastT >= 0f && !_toastDone)
            {
                _toastT += dt;
                float tin = FeedbackTuning.MarkToastInSeconds, hold = FeedbackTuning.MarkToastHoldSeconds, tout = FeedbackTuning.MarkToastOutSeconds;
                if (_toastT >= tin + hold + tout) _toastDone = true;
                else if (_toastT < tin)
                {
                    float k = _toastT / Mathf.Max(1e-4f, tin);
                    o.ToastAlpha = k;
                    o.ToastScale = input.ReducedMotion ? 1f : Mathf.Lerp(FeedbackTuning.MarkToastScaleFrom, 1f, k);
                }
                else if (_toastT < tin + hold) o.ToastAlpha = 1f;
                else o.ToastAlpha = 1f - (_toastT - tin - hold) / Mathf.Max(1e-4f, tout);
            }

            // Dim for the mark's whole life.
            _dim = Approach(_dim, 1f, dt, FeedbackTuning.MarkDimFadeInSeconds);

            // Cold, with hysteresis whoever moves.
            float r = Mathf.Max(0f, input.InnerRadius);
            if (!_coldOn)
            {
                if (input.HunterDistance <= r) { _coldOn = true; _beyondExit = 0f; }
            }
            else if (input.HunterDistance > r * FeedbackTuning.MarkColdExitFactor)
            {
                _beyondExit += dt;
                if (_beyondExit >= FeedbackTuning.MarkColdExitDelaySeconds) _coldOn = false;
            }
            else _beyondExit = 0f;

            _cold = _coldOn ? Approach(_cold, 1f, dt, FeedbackTuning.MarkColdFadeInSeconds)
                            : Approach(_cold, 0f, dt, FeedbackTuning.MarkColdFadeOutSeconds);
            bool redOn = _coldOn && input.HunterInvisible;
            _red = redOn ? Approach(_red, 1f, dt, FeedbackTuning.MarkColdFadeInSeconds)
                         : Approach(_red, 0f, dt, FeedbackTuning.MarkColdFadeOutSeconds);
            o.Dim = _dim;
            o.Cold = _cold;
            o.Red = _red;

            _clockSeconds = ShownSeconds(input.Remaining);
            o.ClockAlpha = 1f;
            o.ClockSeconds = _clockSeconds;

            // The hunter's arrow carries the skull while it is beyond the inner circle and the victim cannot just look at it.
            bool beyond = input.HunterDistance > r;
            bool badge = beyond && (input.HunterHiddenFromView || input.HunterInvisible);
            o.HunterBadgeAlpha = badge ? 1f : 0f;
            o.ForceShowHunter = beyond && input.HunterInvisible;
        }

        private void StepSkull(float dt, ref MarkOutputs o)
        {
            bool shown = _role != MarkRole.Assassin || _locked;
            if (!shown) return;
            _sinceSkull += dt;
            o.SkullCorner = _targetCorner;
            o.SkullAlpha = Mathf.Clamp01(_sinceSkull / Mathf.Max(1e-4f, FeedbackTuning.MarkSkullFadeInSeconds));
        }

        /// <summary>The end fade: the last live frame's values, frozen, times <paramref name="f"/>; nothing boolean survives it.</summary>
        private void FillFrozen(ref MarkOutputs o, float f)
        {
            var last = _snapshot;
            o.Role = _role;
            o.TargetCorner = _targetCorner;
            o.HunterCorner = _hunterCorner;
            o.MarkerAlpha = last.MarkerAlpha * f;
            o.MarkerMode = last.MarkerMode == MarkMarkerMode.Kill ? MarkMarkerMode.Hunt : last.MarkerMode;   // never KILL on a dead mark
            o.MarkerFill01 = last.MarkerFill01;
            o.MarkerMetres = last.MarkerMetres;
            o.SkullCorner = last.SkullCorner;
            o.SkullAlpha = last.SkullAlpha * f;
            o.DrawAlpha = last.DrawAlpha * f;
            o.DrawStep = last.DrawStep;
            o.DrawSteps = last.DrawSteps;
            o.DrawLocked = last.DrawLocked;
            o.ToastAlpha = last.ToastAlpha * f;
            o.ToastScale = last.ToastScale;
            o.ClockAlpha = last.ClockAlpha * f;
            o.ClockSeconds = last.ClockSeconds;
            o.Dim = last.Dim * f;
            o.Cold = last.Cold * f;
            o.Red = last.Red * f;
            o.HunterBadgeAlpha = last.HunterBadgeAlpha * f;
        }

        private static float Approach(float v, float target, float dt, float seconds)
        {
            if (seconds <= 0f) return target;
            return Mathf.MoveTowards(v, target, dt / seconds);
        }
    }

    /// <summary>The Death Mark hex's KILL size, pure: a pop from <see cref="FeedbackTuning.MarkHexKillPopFrom"/> settling to
    /// <see cref="FeedbackTuning.MarkHexKillScale"/>, then an idle pulse that only ever dips BELOW it (1.15 is the cap the hex
    /// gaps allow); static at 1.15 under reduced motion.</summary>
    public static class MarkHexRules
    {
        public static float KillScale(float secondsSinceKill, bool reducedMotion)
        {
            float cap = FeedbackTuning.MarkHexKillScale;
            if (reducedMotion) return cap;
            float pop = FeedbackTuning.MarkHexKillPopSeconds;
            float t = Mathf.Max(0f, secondsSinceKill);
            if (t < pop) return Mathf.Lerp(FeedbackTuning.MarkHexKillPopFrom, cap, t / Mathf.Max(1e-4f, pop));
            float wave = 0.5f + 0.5f * Mathf.Sin((t - pop) * FeedbackTuning.MarkHexKillPulseHz * 2f * Mathf.PI);
            return cap - FeedbackTuning.MarkHexKillPulse * wave;
        }
    }
}
