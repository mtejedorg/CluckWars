using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The post-match winner celebration: a handful of feathers and sparkles (Art/UI/Fx) in the
    /// menus' cream and gold, falling slowly down the podium column in a loop. It replaces the
    /// retired static <c>.cw-bg-particle</c> dots. Phase 4: no longer tinted toward the winner's
    /// player colour (player colour lives only on the dots, bars and pedestal rings). Round 2,
    /// finding 17: every feather is two-tone, a cream body over a slightly larger warm-gold copy of
    /// the same sprite, so a thin warm rim shows at its edges and tips (subtle, no new art).
    /// </summary>
    /// <remarks>
    /// Nine pooled elements, one 30 Hz scheduled item that runs only between <see cref="Start"/> and
    /// <see cref="Stop"/> (the overlay being open), translate / rotate / opacity only (no layout).
    /// Nothing is started under Reduced Motion (<see cref="MenuJuice.Allowed"/>).
    /// </remarks>
    public sealed class MatchCelebration
    {
        private sealed class Piece
        {
            public VisualElement El;
            public VisualElement Body;     // feathers: the cream body drawn over El (the warm rim); null for sparkles
            public bool Sparkle;
            public float Left01, Cycle, Phase, SwayAmp, SwayHz, SwayPhase, Spin, MaxAlpha, Scale;
        }

        private readonly VisualElement _layer;
        private readonly List<Piece> _pieces = new();
        private readonly IVisualElementScheduledItem _tick;
        private float _t0;

        /// <param name="layer">Absolute, non-picking layer drawn first in the podium column, so under all of its text.</param>
        public MatchCelebration(VisualElement layer)
        {
            _layer = layer;
            var rng = new System.Random(7);
            for (int i = 0; i < MenuJuicePolicy.CelebrationParticles; i++)
            {
                bool sparkle = i % 3 == 2;                                  // 6 feathers, 3 sparkles
                var el = new VisualElement { pickingMode = PickingMode.Ignore };
                el.AddToClassList("cw-fx-particle");
                el.AddToClassList(sparkle ? "cw-fx-sparkle" : "cw-fx-feather-" + (1 + i % 3));
                el.style.display = DisplayStyle.None;
                VisualElement body = null;
                if (!sparkle)
                {
                    body = new VisualElement { pickingMode = PickingMode.Ignore };
                    body.AddToClassList("cw-fx-feather-" + (1 + i % 3));
                    body.AddToClassList("cw-fx-feather__body");
                    el.Add(body);
                }
                layer.Add(el);
                _pieces.Add(new Piece
                {
                    El = el, Body = body, Sparkle = sparkle,
                    Left01 = (i + 0.5f) / MenuJuicePolicy.CelebrationParticles,
                    Cycle = 8f + (float)rng.NextDouble() * 5f,
                    Phase = (float)rng.NextDouble(),
                    SwayAmp = 30f + (float)rng.NextDouble() * 40f,
                    SwayHz = 0.12f + (float)rng.NextDouble() * 0.1f,
                    SwayPhase = (float)rng.NextDouble(),
                    Spin = (sparkle ? 40f : 90f) * ((float)rng.NextDouble() < 0.5f ? -1f : 1f),
                    MaxAlpha = sparkle ? 0.7f : 0.5f,
                    Scale = sparkle ? 0.9f + (float)rng.NextDouble() * 0.5f : 1f,
                });
            }
            _tick = layer.schedule.Execute(Tick).Every(33);
            _tick.Pause();
        }

        // Feathers: cream #fef5e0 body over a warm amber rim (#e89a3c); sparkles in the gold #f5c842 family.
        public static readonly Color FeatherCream = new Color(0.996f, 0.961f, 0.878f, 1f);
        public static readonly Color FeatherRim   = new Color(0.910f, 0.604f, 0.235f, 1f);
        private static readonly Color SparkleGold = new Color(1f, 0.84f, 0.31f, 1f);

        /// <summary>Starts the loop. No-op (returns false) under Reduced Motion.</summary>
        public bool Start()
        {
            if (!MenuJuice.Allowed) return false;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                p.El.style.unityBackgroundImageTintColor = p.Sparkle ? SparkleGold : FeatherRim;
                if (p.Body != null) p.Body.style.unityBackgroundImageTintColor = FeatherCream;
                p.El.style.display = DisplayStyle.Flex;
                p.El.style.opacity = 0f;
            }
            _t0 = Time.unscaledTime;
            _tick.Resume();
            return true;
        }

        /// <summary>Stops and hides everything (overlay closed). Safe to call when not running.</summary>
        public void Stop()
        {
            _tick.Pause();
            foreach (var p in _pieces)
            {
                p.El.style.display = DisplayStyle.None;
                p.El.style.translate = StyleKeyword.Null;
                p.El.style.rotate = StyleKeyword.Null;
                p.El.style.scale = StyleKeyword.Null;
                p.El.style.opacity = StyleKeyword.Null;
            }
        }

        private void Tick()
        {
            float w = _layer.resolvedStyle.width, h = _layer.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 1f || h <= 1f) return;
            float t = Time.unscaledTime - _t0;
            foreach (var p in _pieces)
            {
                float fall = MenuJuicePolicy.DriftFall(t, p.Cycle, p.Phase);
                float x = p.Left01 * w + MenuJuicePolicy.DriftSway(t, p.SwayAmp, p.SwayHz, p.SwayPhase);
                float y = Mathf.Lerp(-80f, h + 80f, fall);
                var s = p.Sparkle ? p.Scale * (0.75f + 0.25f * Mathf.Sin(t * 3f + p.Phase * 6.28f)) : p.Scale;
                p.El.style.translate = new Translate(x, y);
                p.El.style.rotate = new Rotate(new Angle(p.Spin * t, AngleUnit.Degree));
                p.El.style.scale = new Scale(new Vector3(s, s, 1f));
                p.El.style.opacity = MenuJuicePolicy.DriftOpacity(fall, p.MaxAlpha);
            }
        }
    }
}
