using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The §2.2 threat overlay: while the local player is aiming an ability, every
    /// chicken inside its shape gets a bracket reticle at its feet telling the caster
    /// whether it will actually be hit.
    ///
    /// <list type="bullet">
    ///   <item><b>Valid target</b> — the ability's accent colour, <i>solid</i> bracket,
    ///   <i>pulsing</i> at <see cref="FeedbackTuning.ValidTargetPulseHz"/>. Will be hit.</item>
    ///   <item><b>Immune / no-effect</b> — <see cref="FeedbackTuning.NeutralNoEffectColor"/>,
    ///   <i>dashed</i> bracket, <i>static</i>, plus a small ⃠ glyph. Inside the area, but
    ///   the ability will do nothing (Spine Coat reflect, Turtle Mode, already-stunned for
    ///   a stun, a <c>requireCargo</c> ability against an empty-handed rival).</item>
    ///   <item><b>Not marked</b> — hidden.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Per §1.3 every state is coded by at least <b>two</b> channels, never colour alone:
    /// accent + solid + motion versus grey + dashed + static + glyph. Both survive a
    /// greyscale/colour-blind read.
    ///
    /// <b>Strictly additive</b>, exactly like <see cref="ChickenStateOverlays"/>: this adds
    /// its own sprites and never touches the chicken's body material or animator, so it
    /// cannot fight the hit-flash (Stage 4) which <i>does</i> drive body colour. The two
    /// must never both own the same channel.
    ///
    /// Everything is derived from <see cref="AbilityTelegraph.Active"/>, which exists only
    /// on the caster's own peer (§1.6) — so on a remote peer this component correctly
    /// draws nothing at all, and no RPC or networked state is involved.
    ///
    /// <b>Maestro:</b> add to the Chicken prefab, on <i>every</i> chicken (not just the
    /// local player — the local caster needs brackets drawn on their rivals). No Inspector
    /// wiring needed.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class TargetHighlight : MonoBehaviour
    {
        /// <summary>World diameter of the bracket sprite. The corner marks sit at ~0.46 of
        /// this (see <see cref="BracketSprite"/>), i.e. ~0.69 world units out — just clear
        /// of the 0.62 status-ring footprint (<see cref="FeedbackTuning.SelfRingRadius"/>)
        /// so a bracketed chicken that is also stunned shows both, unstacked.</summary>
        private const float BracketDiameter = 1.50f;

        /// <summary>World diameter of the ⃠ glyph — small, per §2.2 ("small ⃠ glyph").</summary>
        private const float GlyphDiameter = 0.34f;

        /// <summary>Draw height for both sprites. Above <c>ChickenStateOverlays</c>'s ground
        /// blobs (0.03) and the arena plane, below the telegraph outline's 0.06 line ribbon
        /// so the two never z-fight.</summary>
        private const float OverlayY = 0.045f;

        /// <summary>How far in front of the chicken (local -Z, toward the iso camera) the ⃠
        /// glyph sits, so it isn't hidden underneath the chicken's own mesh.</summary>
        private const float GlyphForwardOffset = -0.62f;

        /// <summary>Depth of the valid-target alpha pulse: the bracket breathes between 60%
        /// and 100% of <see cref="FeedbackTuning.ValidTargetBracketAlpha"/>. Same shaping
        /// idea as <c>ControlStateVFX.UpdateStatusRing</c>'s 0.55 ± 0.25 ring pulse —
        /// clearly alive, never strobing.</summary>
        private const float PulseFloor = 0.6f;

        // Generated once per process, shared by every chicken — same static-cache discipline
        // as ChickenStateOverlays.DiscSprite() / StarSprite().
        /// <summary>Lies a sprite flat on the ground plane, facing up.</summary>
        private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        private static Sprite _solidBracket;
        private static Sprite _dashedBracket;
        private static Sprite _noEffectGlyph;

        private ChickenController _controller;
        private SpriteRenderer    _bracket;
        private SpriteRenderer    _glyph;
        private TargetMark        _shownMark = TargetMark.None;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();

            _bracket = BuildFlatSprite("TargetBracket", BracketDiameter, Vector3.zero, sortingOrder: 4);
            _glyph   = BuildFlatSprite("TargetNoEffectGlyph", GlyphDiameter,
                                       new Vector3(0f, 0f, GlyphForwardOffset), sortingOrder: 5);
        }

        private void LateUpdate()
        {
            var telegraph = AbilityTelegraph.Active;
            if (_controller == null || telegraph == null || !telegraph.isActiveAndEnabled)
            {
                Apply(TargetMark.None, default);
                return;
            }

            if (!telegraph.TryClassify(_controller, out var mark))
            {
                Apply(TargetMark.None, default);
                return;
            }

            if (mark == TargetMark.Valid)
            {
                // Channel 1: the charging ability's accent (already washed toward the
                // illegal tint by the telegraph if the cast has gone illegal — §2.5).
                // Channel 2: motion. Channel 3: a solid, unbroken bracket.
                Color c = telegraph.PreviewColor;
                float phase = Mathf.Sin(Time.time * FeedbackTuning.ValidTargetPulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
                c.a = FeedbackTuning.ValidTargetBracketAlpha * Mathf.Lerp(PulseFloor, 1f, phase);
                Apply(TargetMark.Valid, c);
            }
            else
            {
                // Channels: neutral grey + dashed outline + no motion + the ⃠ glyph. Four,
                // so "nothing will happen to this one" is unmistakable even in greyscale.
                Apply(TargetMark.NoEffect, FeedbackTuning.NeutralNoEffectColor);
            }
        }

        private void Apply(TargetMark mark, Color color)
        {
            if (mark != TargetMark.None)
            {
                // Both sprites are children of a chicken that yaws as it moves, but a
                // reticle that spins with its target reads as an effect *on* the chicken
                // rather than a mark *about* it. Pin them flat and world-aligned — the iso
                // camera never rotates, so this is also what keeps the ⃠ glyph reliably on
                // the camera-facing side instead of swinging behind the body.
                if (_bracket != null) _bracket.transform.rotation = FlatRotation;
                if (_glyph != null)
                {
                    _glyph.transform.rotation = FlatRotation;
                    _glyph.transform.position = transform.position
                                              + new Vector3(0f, OverlayY, GlyphForwardOffset);
                }
            }

            if (mark != _shownMark)
            {
                _shownMark = mark;

                bool visible = mark != TargetMark.None;
                if (visible && _bracket != null)
                    _bracket.sprite = BracketSprite(dashed: mark == TargetMark.NoEffect);

                SetActive(_bracket, visible);
                SetActive(_glyph, mark == TargetMark.NoEffect);
                if (mark == TargetMark.NoEffect && _glyph != null)
                {
                    _glyph.sprite = GlyphSprite();
                    _glyph.color  = FeedbackTuning.NeutralNoEffectColor;
                }
            }

            if (mark != TargetMark.None && _bracket != null) _bracket.color = color;
        }

        private static void SetActive(SpriteRenderer sr, bool on)
        {
            if (sr != null && sr.gameObject.activeSelf != on) sr.gameObject.SetActive(on);
        }

        /// <summary>Sprite lying flat on the ground at the chicken's feet — the same
        /// construction <c>ChickenStateOverlays.BuildBlob</c> uses for its state blobs.</summary>
        private SpriteRenderer BuildFlatSprite(string goName, float diameter, Vector3 localOffset, int sortingOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(localOffset.x, OverlayY, localOffset.z);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale    = Vector3.one * diameter;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            go.SetActive(false);
            return sr;
        }

        // ── Procedural sprites (generated once per process) ─────────────────────

        /// <summary>
        /// Four corner "L" marks forming a reticle around the target's feet. The dashed
        /// variant breaks each arm into ticks — the shape channel that lets the immune
        /// state read without relying on its grey (§1.3).
        /// </summary>
        private static Sprite BracketSprite(bool dashed)
        {
            if (dashed && _dashedBracket != null) return _dashedBracket;
            if (!dashed && _solidBracket != null) return _solidBracket;

            const int   S       = 128;
            const float extent  = 0.46f;  // distance from centre to the outer edge of a corner
            const float thick   = 0.055f; // stroke width
            const float arm     = 0.20f;  // how far each corner's arm runs along its edge
            const float dashLen = 0.045f; // one dash + one gap

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f;
                float v = (y + 0.5f) / S - 0.5f;
                float au = Mathf.Abs(u), av = Mathf.Abs(v);

                bool inSquare = au <= extent && av <= extent;
                // Vertical arm of a corner: hugging the left/right edge, running down from
                // the corner by `arm`. Horizontal arm: the mirror of that.
                bool onVertical   = inSquare && au >= extent - thick && av >= extent - arm;
                bool onHorizontal = inSquare && av >= extent - thick && au >= extent - arm;

                bool on = onVertical || onHorizontal;
                if (on && dashed)
                {
                    // Run the dash pattern along whichever arm this pixel belongs to.
                    float along = onVertical ? av : au;
                    on = Mathf.Repeat(along, dashLen) < dashLen * 0.5f;
                }

                px[y * S + x] = new Color(1f, 1f, 1f, on ? 1f : 0f);
            }

            var sprite = MakeSprite(px, S);
            if (dashed) _dashedBracket = sprite; else _solidBracket = sprite;
            return sprite;
        }

        /// <summary>The ⃠ "no effect" glyph: a ring with a diagonal bar through it.</summary>
        private static Sprite GlyphSprite()
        {
            if (_noEffectGlyph != null) return _noEffectGlyph;

            const int   S     = 64;
            const float ringR = 0.36f;
            const float thick = 0.070f;
            float invSqrt2 = 1f / Mathf.Sqrt(2f);

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f;
                float v = (y + 0.5f) / S - 0.5f;
                float d = Mathf.Sqrt(u * u + v * v);

                bool onRing  = Mathf.Abs(d - ringR) <= thick * 0.5f;
                // Perpendicular distance to the u = v diagonal, clipped to the ring.
                bool onSlash = d <= ringR && Mathf.Abs(u - v) * invSqrt2 <= thick * 0.5f;

                px[y * S + x] = new Color(1f, 1f, 1f, (onRing || onSlash) ? 1f : 0f);
            }

            _noEffectGlyph = MakeSprite(px, S);
            return _noEffectGlyph;
        }

        private static Sprite MakeSprite(Color[] px, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
