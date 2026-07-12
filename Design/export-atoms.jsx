// CluckWars — export-only atom wrappers (Stage 0 asset pipeline)
// ==============================================================
// Thin components composed from CW_THEME / tokens-v2 atoms, exported onto
// window for use by export.html ONLY. Not loaded by the main design canvas.
//
// Conventions:
// • Every wrapper fills its parent (width/height 100%); export.html provides
//   the sized container. Wrappers that bake a drop shadow reserve internal
//   padding so the shadow isn't clipped — 9-slice borders must account for it.
// • "Grayscale" atoms use a near-white → gray ramp so Unity's multiplicative
//   -unity-background-image-tint-color lands close to the target color.

// Neutral 3-stop ramp for tintable gradients (multiply-tint friendly)
const X_GRAY_TOP = '#ffffff';
const X_GRAY_MID = '#cfcfcf';
const X_GRAY_BOT = '#9a9a9a';

// ─── PanelFrame — CWPanel double-layer, empty (9-slice) ────
// Padding 26 reserves room for the 0 6px 20px drop shadow in panelGlow.
const XPanel = () => {
  const t = CW_THEME;
  return (
    <div style={{ width:'100%', height:'100%', padding:26, boxSizing:'border-box' }}>
      <div style={{
        width:'100%', height:'100%', boxSizing:'border-box',
        background:t.panelOuter, border:`3px solid ${t.panelBorder}`,
        borderRadius:t.panelR, boxShadow:t.panelGlow, padding:4,
      }}>
        <div style={{
          width:'100%', height:'100%', boxSizing:'border-box',
          background:t.panelInner, borderRadius:t.panelR - 4,
          border:'1px solid rgba(255,200,100,0.08)',
        }} />
      </div>
    </div>
  );
};

// ─── CardBg — card gradient + border (9-slice) ─────────────
const XCard = () => {
  const t = CW_THEME;
  return (
    <div style={{ width:'100%', height:'100%', padding:12, boxSizing:'border-box' }}>
      <div style={{
        width:'100%', height:'100%', boxSizing:'border-box',
        background:t.cardBg, border:`2px solid ${t.cardBorder}`,
        borderRadius:t.cardR, boxShadow:t.cardGlow,
      }} />
    </div>
  );
};

// ─── CardGlowFrame — gold selection ring, transparent center ─
const XCardGlow = () => {
  const t = CW_THEME;
  return (
    <div style={{ width:'100%', height:'100%', padding:20, boxSizing:'border-box' }}>
      <div style={{
        width:'100%', height:'100%', boxSizing:'border-box',
        border:`2px solid ${t.t3}`, borderRadius:t.cardR,
        boxShadow:t.cardSelGlow,
      }} />
    </div>
  );
};

// ─── Ribbon — CWRibbon with empty content (9-slice, tintable) ─
// Default neutral gray so in-engine tint produces gold / player colors.
const XRibbon = ({ color = '#c8c8c8' }) => (
  <div style={{
    width:'100%', height:'100%', display:'flex', alignItems:'center',
    justifyContent:'center', padding:'8px 26px', boxSizing:'border-box',
  }}>
    <div style={{ width:'100%' }}>
      <CWRibbon color={color} width={'100%'}>{' '}</CWRibbon>
    </div>
  </div>
);

// ─── ButtonGrayscale — CWButton 3-stop gradient, neutral (9-slice) ─
const XButton = () => {
  const t = CW_THEME;
  return (
    <div style={{ width:'100%', height:'100%', padding:12, boxSizing:'border-box' }}>
      <div style={{
        width:'100%', height:'100%', boxSizing:'border-box',
        borderRadius:t.btnR,
        background:`linear-gradient(180deg, ${X_GRAY_TOP} 0%, ${X_GRAY_MID} 50%, ${X_GRAY_BOT} 100%)`,
        border:'2px solid #7a7a7a',
        boxShadow:'inset 0 2px 0 rgba(255,255,255,0.6), inset 0 -2px 0 rgba(0,0,0,0.25), 0 3px 6px rgba(0,0,0,0.4)',
      }} />
    </div>
  );
};

// ─── HexGlossy — CWHex neutral gray, no icon/label/cooldown ─
// #d0d0d0 + adjustColor(+50/-30) yields a white→gray→dark-gray glossy ramp.
const XHex = ({ size = 150, color = '#d0d0d0' }) => (
  <div style={{ width:'100%', height:'100%', display:'flex', alignItems:'flex-start', justifyContent:'center' }}>
    <CWHex size={size} color={color} />
  </div>
);

// ─── BarTrough — dark inset trough (9-slice, final color) ──
const XBarTrough = () => {
  const t = CW_THEME;
  return (
    <div style={{
      width:'100%', height:'100%', boxSizing:'border-box',
      background:t.barBg, border:t.barBorder, borderRadius:6,
      boxShadow:t.barGlow,
    }} />
  );
};

// ─── BarFill — grayscale fill w/ bevel highlight (9-slice, tintable) ─
const XBarFill = () => (
  <div style={{
    width:'100%', height:'100%', boxSizing:'border-box', borderRadius:4,
    background:`linear-gradient(180deg, ${X_GRAY_TOP} 0%, ${X_GRAY_MID} 100%)`,
    boxShadow:'inset 0 1px 0 rgba(255,255,255,0.55), inset 0 -1px 2px rgba(0,0,0,0.35)',
  }} />
);

// ─── Joystick base + knob (white/alpha — inherently tintable) ─
const XJoystickBase = () => (
  <div style={{ width:'100%', height:'100%', padding:4, boxSizing:'border-box' }}>
    <div style={{
      width:'100%', height:'100%', boxSizing:'border-box', borderRadius:'50%',
      background:'radial-gradient(circle at 50% 45%, rgba(255,255,255,0.12) 0%, rgba(255,255,255,0.06) 55%, rgba(255,255,255,0.02) 100%)',
      border:'2.5px solid rgba(255,255,255,0.12)',
      boxShadow:'inset 0 2px 10px rgba(0,0,0,0.35)',
    }} />
  </div>
);

const XJoystickKnob = () => (
  <div style={{ width:'100%', height:'100%', padding:4, boxSizing:'border-box' }}>
    <div style={{
      width:'100%', height:'100%', boxSizing:'border-box', borderRadius:'50%',
      background:'radial-gradient(circle at 35% 30%, rgba(255,255,255,0.5) 0%, rgba(255,255,255,0.18) 55%, rgba(255,255,255,0.07) 100%)',
      border:'2px solid rgba(255,255,255,0.2)',
      boxShadow:'inset 0 -2px 4px rgba(0,0,0,0.25), 0 2px 6px rgba(0,0,0,0.3)',
    }} />
  </div>
);

// ─── PlayerDotGlossy — grayscale glossy sphere (tinted per player) ─
const XPlayerDot = ({ color = '#c8c8c8' }) => (
  <div style={{ width:'100%', height:'100%', padding:4, boxSizing:'border-box' }}>
    <div style={{
      width:'100%', height:'100%', boxSizing:'border-box', borderRadius:'50%',
      background:`radial-gradient(circle at 35% 35%, ${adjustColor(color,60)}, ${color}, ${adjustColor(color,-40)})`,
      boxShadow:'inset 0 1px 0 rgba(255,255,255,0.4), 0 2px 4px rgba(0,0,0,0.35)',
    }} />
  </div>
);

// ─── GlossOverlay — white sheen strip (stretched over any surface) ─
const XGlossOverlay = () => (
  <div style={{
    width:'100%', height:'100%',
    background:'linear-gradient(180deg, rgba(255,255,255,0.45) 0%, rgba(255,255,255,0.05) 55%, rgba(255,255,255,0) 100%)',
    borderRadius:'10px 10px 45% 45%',
  }} />
);

// ─── RadialGlow — white radial (countdown / platform glow) ─
const XRadialGlow = () => (
  <div style={{
    width:'100%', height:'100%',
    background:'radial-gradient(circle, rgba(255,255,255,0.95) 0%, rgba(255,255,255,0.35) 42%, rgba(255,255,255,0) 70%)',
  }} />
);

// ─── CodeTile — lobby invite-code letter tile (9-slice) ────
// Green badge treatment per ART 6.5 (join code, #4ae66a family).
const XCodeTile = () => (
  <div style={{ width:'100%', height:'100%', padding:8, boxSizing:'border-box' }}>
    <div style={{
      width:'100%', height:'100%', boxSizing:'border-box', borderRadius:8,
      background:'linear-gradient(180deg, #16381e 0%, #0c2412 100%)',
      border:'2px solid #2f7a44',
      boxShadow:'inset 0 1px 0 rgba(120,255,150,0.25), 0 0 10px rgba(74,230,106,0.35), 0 2px 4px rgba(0,0,0,0.4)',
    }} />
  </div>
);

// ─── XEmoji — single emoji glyph → color icon PNG ──────────
const XEmoji = ({ glyph = '🍎', px = 96, glow = null }) => (
  <div style={{ width:'100%', height:'100%', display:'flex', alignItems:'center', justifyContent:'center' }}>
    <span style={{
      fontSize:px, lineHeight:1,
      fontFamily:"'Segoe UI Emoji','Noto Color Emoji',sans-serif",
      filter: glow
        ? `drop-shadow(0 0 8px ${glow}) drop-shadow(0 2px 3px rgba(0,0,0,0.35))`
        : 'drop-shadow(0 2px 3px rgba(0,0,0,0.35))',
    }}>{glyph}</span>
  </div>
);

// ─── Backgrounds ───────────────────────────────────────────
const XScreenBg = () => (
  <div style={{ width:'100%', height:'100%', background:CW_THEME.screenBg }} />
);

const XHudTopGradient = () => (
  <div style={{ width:'100%', height:'100%', background:CW_THEME.hudGrad }} />
);

Object.assign(window, {
  XPanel, XCard, XCardGlow, XRibbon, XButton, XHex,
  XBarTrough, XBarFill, XJoystickBase, XJoystickKnob, XPlayerDot,
  XGlossOverlay, XRadialGlow, XCodeTile, XEmoji,
  XScreenBg, XHudTopGradient,
});
