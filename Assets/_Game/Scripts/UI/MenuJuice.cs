using System;
using System.Collections.Generic;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>The Fx sprite a burst scatters (Art/UI/Fx, wired in CluckWarsTheme.uss as .cw-fx-*).</summary>
    public enum ParticleKind { Sparkle, Dust, Feather }

    /// <summary>
    /// The menu's effect engine: squash-pop, countdown slam, staggered entries, fly-to-slot, the READY stamp and
    /// small particle bursts, all driven by ONE scheduled item that is paused whenever nothing is
    /// running. Effects write inline style values each frame (curves in <see cref="MenuJuicePolicy"/>)
    /// and clear them when done, so an element always ends exactly where the stylesheet puts it.
    /// While an effect owns an element it carries <see cref="LiveClass"/> (USS transitions off), so
    /// hover/selected transitions never fight the curve.
    /// </summary>
    /// <remarks>
    /// <b>Reduced Motion.</b> Every effect entry point (<see cref="Pop"/>, <see cref="Stagger"/>,
    /// <see cref="Fly"/>, <see cref="Stamp"/>, <see cref="Burst"/>, <see cref="BurstAt"/>) begins with
    /// <c>if (!Allowed) return false;</c> and does nothing visual: a caller that needs the
    /// consequence of the effect (a sound, a slot appearing) takes it from the false return.
    /// <see cref="MenuJuiceGuardTests"/> fails on a public entry point that skips the gate.
    /// <para>Effects are pooled (the <see cref="Effect"/> records and the particle / ghost elements),
    /// so a running effect allocates nothing per frame. The layer holds at most
    /// <see cref="MenuJuicePolicy.ParticlePoolSize"/> particles and
    /// <see cref="MenuJuicePolicy.GhostPoolSize"/> ghosts; past that a burst is skipped (cosmetic) and a
    /// flight lands the oldest one early (its landing still happens).</para>
    /// </remarks>
    public sealed class MenuJuice : IDisposable
    {
        /// <summary>The single Reduced Motion gate for menu juice.</summary>
        public static bool Allowed => !PlayerPreferences.ReducedMotionEnabled;

        /// <summary>On an element while an effect drives it: transitions off so inline values apply as written.</summary>
        public const string LiveClass = "cw-fx-live";

        internal enum Kind { Pop, Slam, Enter, Fly, Stamp, Particle }

        /// <summary>A running effect; also the handle <see cref="Fly"/> returns so a caller can land it early.</summary>
        public sealed class Effect
        {
            internal Kind Kind;
            internal VisualElement El;           // pop/enter/stamp target; fly source; particle element
            internal VisualElement Target;       // fly destination
            internal VisualElement Ghost;        // fly ghost
            internal float Start, Delay, Duration;
            internal bool Begun, Hit, Done;
            internal float Rest;                 // pop rest scale
            internal float Rise;                 // enter rise
            internal Vector2 Delta;              // fly centre-to-centre
            internal float EndScale;             // fly
            internal Vector2 Dir; internal float Speed, BaseScale, Spin; internal ParticleKind PKind; // particle
            internal Action OnDone, OnHit;
            internal Action<VisualElement> Paint;
        }

        private readonly VisualElement _layer;
        private readonly List<Effect> _active = new();
        private readonly Stack<Effect> _effectPool = new();
        private readonly Stack<VisualElement> _freeGhosts = new();
        private readonly Stack<VisualElement> _freeParticles = new();
        private int _ghostCount, _particleCount;
        private readonly IVisualElementScheduledItem _tick;
        private readonly System.Random _rng = new(12345);

        /// <param name="layer">Absolute, full-panel, non-picking layer above the pages: ghosts and particles live here.</param>
        public MenuJuice(VisualElement layer)
        {
            _layer = layer;
            _tick = layer.schedule.Execute(Tick).Every(16);
            _tick.Pause();
        }

        // ======================================================================
        //  Entry points (all gated on Allowed)
        // ======================================================================

        /// <summary>Squash-pop of <paramref name="el"/> around <paramref name="restScale"/> (its stylesheet scale).</summary>
        public bool Pop(VisualElement el, float restScale = 1f)
        {
            if (!Allowed) return false;
            if (el == null) return false;
            var e = FindRun(el, Kind.Pop) ?? Acquire(Kind.Pop, el);
            e.Start = Time.unscaledTime; e.Delay = 0f; e.Duration = MenuJuicePolicy.PopSeconds; e.Rest = restScale;
            Begin(e);
            return true;
        }

        /// <summary>A countdown numeral slams in (scale 1.7 -> 1 with a little overshoot, 220 ms).</summary>
        public bool Slam(VisualElement el)
        {
            if (!Allowed) return false;
            if (el == null) return false;
            var e = FindRun(el, Kind.Slam) ?? Acquire(Kind.Slam, el);
            e.Start = Time.unscaledTime; e.Delay = 0f; e.Duration = MenuJuicePolicy.CountdownPopSeconds;
            Begin(e);
            return true;
        }

        /// <summary>
        /// Items rise and fade in one after another (40 ms apart, whole entry under 400 ms). They are
        /// hidden immediately, so nothing flashes at rest before its turn.
        /// </summary>
        public bool Stagger(IReadOnlyList<VisualElement> items)
        {
            if (!Allowed) return false;
            if (items == null || items.Count == 0) return false;
            float now = Time.unscaledTime;
            for (int i = 0; i < items.Count; i++)
            {
                var el = items[i];
                if (el == null) continue;
                var e = FindRun(el, Kind.Enter) ?? Acquire(Kind.Enter, el);
                e.Start = now; e.Delay = MenuJuicePolicy.StaggerDelay(i, items.Count);
                e.Duration = MenuJuicePolicy.EntrySeconds; e.Rise = MenuJuicePolicy.EntryRisePx;
                Begin(e);
            }
            return true;
        }

        /// <summary>
        /// A ghost made by <paramref name="paint"/> flies from <paramref name="from"/> to
        /// <paramref name="to"/> (laid out when the flight starts, one frame from now);
        /// <paramref name="onLand"/> runs on the landing frame, or when the flight is landed early.
        /// Returns the handle, or null when motion is off (the caller then lands at once).
        /// </summary>
        public Effect Fly(VisualElement from, VisualElement to, Action<VisualElement> paint, Action onLand)
        {
            if (!Allowed) return null;
            if (from == null || to == null) return null;
            var e = Acquire(Kind.Fly, from);
            e.Target = to; e.Paint = paint; e.OnDone = onLand;
            e.Start = Time.unscaledTime; e.Delay = MenuJuicePolicy.FlightStartDelaySeconds; e.Duration = MenuJuicePolicy.FlightSeconds;
            Begin(e);
            return e;
        }

        /// <summary>
        /// Slams <paramref name="el"/> in (scale 1.6 -> 1, a slight rotation) after
        /// <paramref name="delaySeconds"/>; <paramref name="onHit"/> runs on the hit frame (or at once if cancelled first).
        /// </summary>
        public bool Stamp(VisualElement el, float delaySeconds, Action onHit)
        {
            if (!Allowed) return false;
            if (el == null) return false;
            var e = FindRun(el, Kind.Stamp) ?? Acquire(Kind.Stamp, el);
            e.Start = Time.unscaledTime; e.Delay = delaySeconds; e.Duration = MenuJuicePolicy.StampSeconds; e.Hit = false; e.OnHit = onHit;
            Begin(e);
            return true;
        }

        /// <summary>A small burst (<= <see cref="MenuJuicePolicy.MaxBurstParticles"/> particles) at the centre of <paramref name="el"/>.</summary>
        public bool BurstAt(VisualElement el, ParticleKind kind, int count, float radius = 70f)
        {
            if (!Allowed) return false;
            if (el == null) return false;
            var b = el.worldBound;
            if (float.IsNaN(b.x) || float.IsNaN(b.width) || b.width <= 0f) return false;   // not laid out: nothing to anchor to
            return BurstCore(b.center, kind, count, radius);
        }

        /// <summary>A burst at a panel-space point.</summary>
        public bool Burst(Vector2 centerInPanel, ParticleKind kind, int count, float radius = 70f)
        {
            if (!Allowed) return false;
            return BurstCore(centerInPanel, kind, count, radius);
        }

        // ======================================================================
        //  Housekeeping (not effects, no gate)
        // ======================================================================

        /// <summary>Lands the flight now (its <c>onLand</c> runs); no-op if it already finished.</summary>
        public void LandNow(Effect e)
        {
            if (e != null && !e.Done) Finish(e);
        }

        /// <summary>Ends every effect: styles cleared, ghosts and particles back in the pool, pending landings / stamp hits fired.</summary>
        public void CancelAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;
                Finish(_active[i]);
            }
        }

        public void Dispose()
        {
            CancelAll();
            _tick.Pause();
        }

        // ======================================================================
        //  Engine
        // ======================================================================

        private Effect Acquire(Kind kind, VisualElement el)
        {
            var e = _effectPool.Count > 0 ? _effectPool.Pop() : new Effect();
            e.Kind = kind; e.El = el; e.Target = null; e.Ghost = null;
            e.Begun = false; e.Hit = false; e.Done = false;
            e.OnDone = null; e.OnHit = null; e.Paint = null;
            return e;
        }

        private Effect FindRun(VisualElement el, Kind kind)
        {
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].El == el && _active[i].Kind == kind) return _active[i];
            return null;
        }

        /// <summary>Applies the effect's starting look right away (so a delayed one is already hidden) and schedules it.</summary>
        private void Begin(Effect e)
        {
            if (!_active.Contains(e)) _active.Add(e);
            switch (e.Kind)
            {
                case Kind.Pop:
                case Kind.Slam:
                    e.El.AddToClassList(LiveClass);
                    break;
                case Kind.Enter:
                    e.El.AddToClassList(LiveClass);
                    e.El.style.opacity = 0f;
                    e.El.style.translate = new Translate(0f, e.Rise);
                    break;
                case Kind.Stamp:
                    e.El.AddToClassList(LiveClass);
                    ApplyStamp(e.El, 0f);
                    break;
            }
            _tick.Resume();
        }

        private void Tick()
        {
            float now = Time.unscaledTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;   // a callback cancelled things
                var e = _active[i];
                float t = now - e.Start - e.Delay;
                if (t < 0f) continue;
                Step(e, t);
            }
            if (_active.Count == 0) _tick.Pause();
        }

        private void Step(Effect e, float t)
        {
            switch (e.Kind)
            {
                case Kind.Pop:
                {
                    float s = e.Rest * MenuJuicePolicy.PopFactor(t);
                    e.El.style.scale = new Scale(new Vector3(s, s, 1f));
                    break;
                }
                case Kind.Slam:
                {
                    float s = MenuJuicePolicy.CountdownScale(t);
                    e.El.style.scale = new Scale(new Vector3(s, s, 1f));
                    break;
                }
                case Kind.Enter:
                {
                    float u = MenuJuicePolicy.EaseOutCubic(t / e.Duration);
                    e.El.style.opacity = u;
                    e.El.style.translate = new Translate(0f, e.Rise * (1f - u));
                    break;
                }
                case Kind.Stamp:
                    ApplyStamp(e.El, t);
                    if (!e.Hit && t >= MenuJuicePolicy.StampHitSeconds) { e.Hit = true; e.OnHit?.Invoke(); }
                    break;
                case Kind.Fly:
                    if (!e.Begun && !BeginFlight(e)) { Finish(e); return; }
                    StepFlight(e, t);
                    break;
                case Kind.Particle:
                    StepParticle(e, t);
                    break;
            }
            if (t >= e.Duration && !e.Done) Finish(e);
        }

        private static void ApplyStamp(VisualElement el, float t)
        {
            float s = MenuJuicePolicy.StampScale(t);
            el.style.opacity = MenuJuicePolicy.StampOpacity(t);
            el.style.scale = new Scale(new Vector3(s, s, 1f));
            el.style.rotate = new Rotate(new Angle(MenuJuicePolicy.StampDegrees(t), AngleUnit.Degree));
        }

        /// <summary>Ends <paramref name="e"/>: restores the element, recycles it, runs its landing / pending hit.</summary>
        private void Finish(Effect e)
        {
            if (e.Done) return;
            e.Done = true;
            _active.Remove(e);
            switch (e.Kind)
            {
                case Kind.Pop:
                case Kind.Slam:
                    e.El.style.scale = StyleKeyword.Null;
                    ReleaseLive(e.El);
                    break;
                case Kind.Enter:
                    e.El.style.opacity = StyleKeyword.Null;
                    e.El.style.translate = StyleKeyword.Null;
                    ReleaseLive(e.El);
                    break;
                case Kind.Stamp:
                    e.El.style.opacity = StyleKeyword.Null;
                    e.El.style.scale = StyleKeyword.Null;
                    e.El.style.rotate = StyleKeyword.Null;
                    ReleaseLive(e.El);
                    if (!e.Hit) { e.Hit = true; e.OnHit?.Invoke(); }
                    break;
                case Kind.Fly:
                    if (e.Ghost != null) { e.Ghost.style.display = DisplayStyle.None; _freeGhosts.Push(e.Ghost); e.Ghost = null; }
                    break;
                case Kind.Particle:
                    e.El.style.display = DisplayStyle.None;
                    _freeParticles.Push(e.El);
                    break;
            }
            var land = e.OnDone;
            e.OnDone = null; e.OnHit = null; e.Paint = null; e.El = null; e.Target = null;
            _effectPool.Push(e);
            land?.Invoke();
        }

        /// <summary>Drops <see cref="LiveClass"/> unless another running effect still drives the element.</summary>
        private void ReleaseLive(VisualElement el)
        {
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].El == el) return;
            el.RemoveFromClassList(LiveClass);
        }

        // ---- Fly-to-slot ---------------------------------------------------------------------

        private bool BeginFlight(Effect e)
        {
            e.Begun = true;
            var a = e.El.worldBound;
            var b = e.Target.worldBound;
            if (!Valid(a) || !Valid(b)) return false;   // not laid out (page left?): land at once

            var ghost = AcquireGhost();
            if (ghost == null) return false;
            e.Ghost = ghost;
            var origin = _layer.worldBound.position;
            ghost.style.left = a.x - origin.x; ghost.style.top = a.y - origin.y;
            ghost.style.width = a.width; ghost.style.height = a.height;
            ghost.style.translate = StyleKeyword.Null;
            ghost.style.scale = StyleKeyword.Null;
            e.Paint?.Invoke(ghost);
            ghost.style.display = DisplayStyle.Flex;
            e.Delta = b.center - a.center;
            e.EndScale = b.width / a.width;
            return true;
        }

        private static bool Valid(Rect r) =>
            !float.IsNaN(r.x) && !float.IsNaN(r.y) && !float.IsNaN(r.width) && r.width > 1f && r.height > 1f;

        private static void StepFlight(Effect e, float t)
        {
            float u = MenuJuicePolicy.EaseInOut(t / e.Duration);
            float s = Mathf.Lerp(1f, e.EndScale, u);
            e.Ghost.style.translate = new Translate(e.Delta.x * u, e.Delta.y * u - MenuJuicePolicy.FlightArc(Mathf.Clamp01(t / e.Duration)));
            e.Ghost.style.scale = new Scale(new Vector3(s, s, 1f));
        }

        private VisualElement AcquireGhost()
        {
            if (_freeGhosts.Count > 0) return _freeGhosts.Pop();
            if (_ghostCount >= MenuJuicePolicy.GhostPoolSize)
            {
                // Both ghosts are in flight: land the oldest one early (its landing still happens).
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].Kind != Kind.Fly || _active[i].Ghost == null) continue;
                    Finish(_active[i]);
                    return _freeGhosts.Count > 0 ? _freeGhosts.Pop() : null;
                }
                return null;
            }
            var g = new VisualElement { pickingMode = PickingMode.Ignore };
            g.AddToClassList("cw-fly-ghost");
            g.style.display = DisplayStyle.None;
            _layer.Add(g);
            _ghostCount++;
            return g;
        }

        // ---- Particles -------------------------------------------------------------------------

        private bool BurstCore(Vector2 centerInPanel, ParticleKind kind, int count, float radius)
        {
            count = Mathf.Clamp(count, 0, MenuJuicePolicy.MaxBurstParticles);
            if (count == 0) return false;
            var origin = _layer.worldBound.position;
            var local = centerInPanel - origin;
            float now = Time.unscaledTime;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                var p = AcquireParticle(kind);
                if (p == null) break;   // pool exhausted: a cosmetic burst is simply smaller
                var e = Acquire(Kind.Particle, p);
                float ang = (i + (float)_rng.NextDouble() * 0.6f) / count * Mathf.PI * 2f;
                e.PKind = kind;
                e.Dir = new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang));
                e.Speed = radius * (0.7f + (float)_rng.NextDouble() * 0.6f);
                e.BaseScale = kind == ParticleKind.Dust ? 1f : 0.8f + (float)_rng.NextDouble() * 0.5f;
                e.Spin = ((float)_rng.NextDouble() - 0.5f) * (kind == ParticleKind.Feather ? 260f : 90f);
                e.Start = now; e.Delay = 0f; e.Duration = MenuJuicePolicy.BurstSeconds * (kind == ParticleKind.Dust ? 1.1f : 1f);
                p.style.left = local.x; p.style.top = local.y;       // the .cw-fx-* negative margins centre the sprite on this point
                p.style.opacity = 0f;
                p.style.display = DisplayStyle.Flex;
                _active.Add(e);
                any = true;
            }
            if (any) _tick.Resume();
            return any;
        }

        private VisualElement AcquireParticle(ParticleKind kind)
        {
            VisualElement p;
            if (_freeParticles.Count > 0) p = _freeParticles.Pop();
            else if (_particleCount < MenuJuicePolicy.ParticlePoolSize)
            {
                p = new VisualElement { pickingMode = PickingMode.Ignore };
                p.AddToClassList("cw-fx-particle");
                p.style.display = DisplayStyle.None;
                _layer.Add(p);
                _particleCount++;
            }
            else return null;

            if (p.userData is string old) p.RemoveFromClassList(old);
            string cls = kind switch
            {
                ParticleKind.Sparkle => "cw-fx-sparkle",
                ParticleKind.Dust => "cw-fx-dust",
                _ => "cw-fx-feather-" + (1 + _rng.Next(3)),
            };
            p.AddToClassList(cls);
            p.userData = cls;
            return p;
        }

        private static void StepParticle(Effect e, float t)
        {
            float u = Mathf.Clamp01(t / e.Duration);
            float d = MenuJuicePolicy.BurstDistance(u, e.Speed);
            float gravity = e.PKind == ParticleKind.Feather ? 40f * u * u : e.PKind == ParticleKind.Dust ? -10f * u : 0f;
            float scale = e.PKind switch
            {
                ParticleKind.Sparkle => e.BaseScale * MenuJuicePolicy.SparkleScale(u),
                ParticleKind.Dust => e.BaseScale * Mathf.Lerp(0.5f, 1.3f, u),
                _ => e.BaseScale * Mathf.Lerp(0.6f, 1f, Mathf.Min(1f, u * 3f)),
            };
            float alpha = MenuJuicePolicy.BurstOpacity(u) * (e.PKind == ParticleKind.Dust ? 0.6f : 1f);
            e.El.style.translate = new Translate(e.Dir.x * d, e.Dir.y * d + gravity);
            e.El.style.scale = new Scale(new Vector3(scale, scale, 1f));
            e.El.style.rotate = new Rotate(new Angle(e.Spin * u, AngleUnit.Degree));
            e.El.style.opacity = alpha;
        }
    }
}
