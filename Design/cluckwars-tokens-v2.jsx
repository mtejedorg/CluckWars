// CluckWars UI v2 — Rich Glossy Tokens (Clash Royale / Disney)
// =============================================================

const CW_PLAYER_COLORS = ['#E8751A', '#1A7FC4', '#C4286F', '#0D9E7A'];
const CW_PLAYER_NAMES = ['P1', 'P2', 'P3', 'P4'];

const CW_CLASSES = {
  fatty:    { name: 'Fatty Chicken',    short: 'Fatty',    role: 'Bulk Carrier', lore: '"Slow and steady wins the race — if it survives long enough."', color: '#F5D75A', dark: '#B89E20', light:'#FFF3B0', stats: { cargo:5, rate:5, hp:4, resist:4, speed:2, attack:2 }, shape:'wide' },
  speedy:   { name: 'Speedy Chicken',   short: 'Speedy',   role: 'Hit & Run',    lore: '"If you can\'t catch me, you can\'t kill me."',                color: '#E85A2A', dark: '#B84418', light:'#FFB088', stats: { cargo:3, rate:3, hp:2, resist:1, speed:5, attack:3 }, shape:'lean' },
  warrior:  { name: 'Warrior Chicken',  short: 'Warrior',  role: 'All-Rounder',  lore: '"The farm is a battlefield."',                                 color: '#C04030', dark: '#8A2A20', light:'#F09888', stats: { cargo:3, rate:3, hp:3, resist:3, speed:3, attack:4 }, shape:'broad' },
  assassin: { name: 'Assassin Chicken', short: 'Assassin', role: 'Disruptor',    lore: '"Blink and your food is gone."',                               color: '#7B68EE', dark: '#5A48C8', light:'#C4B8FF', stats: { cargo:2, rate:2, hp:2, resist:2, speed:4, attack:5 }, shape:'sleek', twoSlots:true },
};
const CW_CLASS_KEYS = ['fatty','speedy','warrior','assassin'];
const CW_STAT_LABELS = { cargo:'Cargo', rate:'Rate', hp:'HP', resist:'Resist', speed:'Speed', attack:'Attack' };
const CW_STAT_ORDER = ['cargo','rate','hp','resist','speed','attack'];

const CW_ABILITIES = [
  { id:'speed',  name:'Speed Burst',    short:'SPD',  color:'#00BCD4', type:'Mobility',  icon:'⚡' },
  { id:'egg',    name:'Egg Shell',      short:'EGG',  color:'#FFC107', type:'Defensive',  icon:'🥚' },
  { id:'roll',   name:'Roll & Trample', short:'ROLL', color:'#FF5722', type:'Offensive',  icon:'🔥' },
  { id:'doppel', name:'Doppelganger',   short:'DPLG', color:'#9C27B0', type:'Deceptive',  icon:'👥' },
  { id:'invis',  name:'Invisibility',   short:'INVIS',color:'#607D8B', type:'Deceptive',  icon:'👻' },
  { id:'spine',  name:'Spine Coat',     short:'SPNE', color:'#4CAF50', type:'Reactive',   icon:'🦔' },
  { id:'turtle', name:'Turtle Mode',    short:'TRTL', color:'#795548', type:'Defensive',  icon:'🐢' },
  { id:'sneak',  name:'Sneaky Steal',   short:'SNKY', color:'#E91E63', type:'Utility',    icon:'🤏' },
];

// ─── GLOSSY THEME ──────────────────────────────────────────
// Rich, dimensional, Clash Royale / Supercell energy
const CW_THEME = {
  // Panels — layered depth with inner glow
  panelOuter: 'linear-gradient(180deg, #5a3a1a 0%, #3a2210 100%)',
  panelInner: 'linear-gradient(180deg, #4a3018 0%, #2a1a0c 100%)',
  panelBorder: '#8a6a3a',
  panelGlow: 'inset 0 1px 0 rgba(255,220,140,0.3), inset 0 -1px 0 rgba(0,0,0,0.4), 0 6px 20px rgba(0,0,0,0.6)',
  panelR: 14,

  // Cards — embossed, thick
  cardBg: 'linear-gradient(180deg, #3a2816 0%, #2a1c0e 100%)',
  cardBorder: '#6a4a28',
  cardGlow: 'inset 0 1px 0 rgba(255,200,100,0.15), 0 3px 8px rgba(0,0,0,0.4)',
  cardSelGlow: '0 0 16px rgba(245,200,66,0.5), inset 0 1px 0 rgba(255,220,140,0.4), 0 4px 12px rgba(0,0,0,0.5)',
  cardR: 12,

  // Buttons — beveled with glossy highlight
  btnBg: 'linear-gradient(180deg, #f5c842 0%, #d4a020 50%, #b88a14 100%)',
  btnBorder: '#8a6a10',
  btnGlow: 'inset 0 2px 0 rgba(255,240,180,0.6), inset 0 -2px 0 rgba(0,0,0,0.25), 0 3px 6px rgba(0,0,0,0.4)',
  btnR: 10,
  btnHover: 'linear-gradient(180deg, #ffe066 0%, #e8b830 50%, #c89818 100%)',

  startBg: 'linear-gradient(180deg, #5ac54f 0%, #33a332 50%, #228b22 100%)',
  startBorder: '#1a6a1a',
  startGlow: 'inset 0 2px 0 rgba(180,255,180,0.5), inset 0 -2px 0 rgba(0,0,0,0.25), 0 3px 6px rgba(0,0,0,0.4)',

  dangerBg: 'linear-gradient(180deg, #e85a4a 0%, #c83030 50%, #a82020 100%)',
  dangerBorder: '#7a1a1a',
  dangerGlow: 'inset 0 2px 0 rgba(255,180,160,0.5), inset 0 -2px 0 rgba(0,0,0,0.25), 0 3px 6px rgba(0,0,0,0.4)',

  // Text
  t1: '#fef5e0',
  t2: '#c4a060',
  t3: '#f5c842',
  tShadow: '0 2px 4px rgba(0,0,0,0.6)',
  tGlow: (c) => `0 0 8px ${c}88, 0 2px 4px rgba(0,0,0,0.5)`,

  // Bars
  barBg: '#1a1008',
  barBorder: '2px solid #4a3018',
  barGlow: 'inset 0 1px 2px rgba(0,0,0,0.6)',
  hp: 'linear-gradient(180deg, #e84040 0%, #c03030 100%)',
  hpFlat: '#d43030',
  cargo: 'linear-gradient(180deg, #f5c842 0%, #d4a020 100%)',
  cargoFlat: '#e8b020',

  // Screen backgrounds
  screenBg: 'radial-gradient(ellipse at 50% 20%, #2a1a0c 0%, #0e0804 80%)',
  mapBg: 'radial-gradient(ellipse at 50% 40%, #1a2a10 0%, #0a1208 60%, #050804 100%)',
  overlayBg: 'rgba(8,4,2,0.88)',
  hudGrad: 'linear-gradient(180deg, rgba(10,6,2,0.7) 0%, transparent 100%)',

  // Ribbon / banner
  ribbonBg: 'linear-gradient(180deg, #d4a020 0%, #b08018 100%)',
  ribbonShadow: '0 3px 8px rgba(0,0,0,0.5)',
};

// ─── GLOSSY HEX BUTTON ────────────────────────────────────
const CWHex = ({ size=72, color='#3388cc', label, icon, glow, cooldownPct=0, style={}, onClick }) => {
  const h = size * 1.155;
  const darkerColor = adjustColor(color, -30);
  const lighterColor = adjustColor(color, 50);
  const uid = `hex-${label}-${Math.random().toString(36).substr(2,4)}`;
  return (
    <div style={{ width:size, height:h, position:'relative', flexShrink:0, cursor: onClick?'pointer':'default', ...style }} onClick={onClick}>
      <svg viewBox="0 0 100 115.5" width="100%" height="100%">
        <defs>
          <linearGradient id={`${uid}-grad`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={lighterColor} />
            <stop offset="40%" stopColor={color} />
            <stop offset="100%" stopColor={darkerColor} />
          </linearGradient>
          <linearGradient id={`${uid}-shine`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="rgba(255,255,255,0.45)" />
            <stop offset="50%" stopColor="rgba(255,255,255,0.05)" />
            <stop offset="100%" stopColor="rgba(0,0,0,0.2)" />
          </linearGradient>
          {cooldownPct > 0 && <clipPath id={`${uid}-cd`}><rect x="0" y={115.5*(1-cooldownPct)} width="100" height={115.5*cooldownPct} /></clipPath>}
          <filter id={`${uid}-glow`} x="-40%" y="-40%" width="180%" height="180%">
            <feGaussianBlur stdDeviation="4" result="blur" />
            <feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge>
          </filter>
        </defs>
        {/* Outer shadow hex */}
        <polygon points="50,2 98,28.87 98,86.6 50,113.5 2,86.6 2,28.87" fill="#1a0e04" opacity="0.6" />
        {/* Border hex */}
        <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87" fill={darkerColor} stroke={darkerColor} strokeWidth="2" />
        {/* Main fill */}
        <polygon points="50,8 92,31 92,84 50,107 8,84 8,31" fill={`url(#${uid}-grad)`} />
        {/* Gloss overlay */}
        <polygon points="50,8 92,31 92,84 50,107 8,84 8,31" fill={`url(#${uid}-shine)`} />
        {/* Inner highlight line */}
        <polygon points="50,14 86,34 86,81 50,101 14,81 14,34" fill="none" stroke="rgba(255,255,255,0.15)" strokeWidth="1" />
        {/* Cooldown */}
        {cooldownPct > 0 && <polygon points="50,8 92,31 92,84 50,107 8,84 8,31" fill="rgba(0,0,0,0.6)" clipPath={`url(#${uid}-cd)`} />}
      </svg>
      <div style={{ position:'absolute', inset:0, display:'flex', flexDirection:'column', alignItems:'center', justifyContent:'center', pointerEvents:'none', gap: 0 }}>
        {icon && <span style={{ fontSize: size*0.28, lineHeight:1, filter:'drop-shadow(0 1px 2px rgba(0,0,0,0.5))' }}>{icon}</span>}
        {label && <span style={{ color:'#fff', fontWeight:700, fontSize: icon ? size*0.14 : size*0.2, fontFamily:"'Lilita One',cursive", textShadow:'0 1px 3px rgba(0,0,0,0.7), 0 0 6px rgba(0,0,0,0.3)', letterSpacing:0.5 }}>{label}</span>}
      </div>
    </div>
  );
};

function adjustColor(hex, amount) {
  let r = parseInt(hex.slice(1,3),16), g = parseInt(hex.slice(3,5),16), b = parseInt(hex.slice(5,7),16);
  r = Math.max(0,Math.min(255,r+amount)); g = Math.max(0,Math.min(255,g+amount)); b = Math.max(0,Math.min(255,b+amount));
  return `#${r.toString(16).padStart(2,'0')}${g.toString(16).padStart(2,'0')}${b.toString(16).padStart(2,'0')}`;
}

// ─── GLOSSY STAT BAR ───────────────────────────────────────
const CWStatBar = ({ value, max=5, label, color }) => {
  const t = CW_THEME;
  const stars = [];
  for (let i = 0; i < max; i++) stars.push(i < value);
  return (
    <div style={{ display:'flex', alignItems:'center', gap:6 }}>
      <span style={{ fontFamily:"'Nunito',sans-serif", fontSize:11, color:t.t2, width:44, textAlign:'right', flexShrink:0, fontWeight:700, textShadow:t.tShadow }}>{label}</span>
      <div style={{ display:'flex', gap:3 }}>
        {stars.map((on,i) => (
          <div key={i} style={{
            width:14, height:14, borderRadius:3,
            background: on ? `linear-gradient(180deg, ${adjustColor(color,40)}, ${color}, ${adjustColor(color,-30)})` : '#1a1008',
            border: on ? `1px solid ${adjustColor(color,20)}` : '1px solid #3a2816',
            boxShadow: on ? `inset 0 1px 0 rgba(255,255,255,0.35), 0 1px 3px rgba(0,0,0,0.4)` : 'inset 0 1px 2px rgba(0,0,0,0.5)',
          }} />
        ))}
      </div>
    </div>
  );
};

// ─── PLAYER BADGE (glossy pill) ────────────────────────────
const CWPlayerBadge = ({ index, score, small }) => {
  const c = CW_PLAYER_COLORS[index];
  const darker = adjustColor(c, -40);
  return (
    <div style={{
      display:'flex', alignItems:'center', gap: small?4:6,
      padding: small?'3px 8px':'4px 12px', borderRadius:20,
      background: `linear-gradient(180deg, ${c}44 0%, ${c}22 100%)`,
      border: `1.5px solid ${c}66`,
      boxShadow: `inset 0 1px 0 rgba(255,255,255,0.15), 0 2px 4px rgba(0,0,0,0.3)`,
    }}>
      <div style={{ width: small?8:12, height: small?8:12, borderRadius:'50%', background:`radial-gradient(circle at 35% 35%, ${adjustColor(c,60)}, ${c}, ${darker})`, boxShadow:`0 0 6px ${c}88, inset 0 1px 0 rgba(255,255,255,0.4)` }} />
      <span style={{ fontFamily:"'Lilita One',cursive", fontSize: small?12:15, color:'#fef5e0', textShadow:'0 1px 3px rgba(0,0,0,0.6)' }}>{CW_PLAYER_NAMES[index]}: {score}</span>
    </div>
  );
};

// ─── TIMER (embossed badge) ────────────────────────────────
const CWTimer = ({ time = '02:34' }) => (
  <div style={{
    padding:'5px 18px', borderRadius:10,
    background:'linear-gradient(180deg, #3a2816 0%, #2a1a0c 100%)',
    border:'2px solid #6a4a28',
    boxShadow:'inset 0 1px 0 rgba(255,200,100,0.2), inset 0 -1px 0 rgba(0,0,0,0.3), 0 3px 8px rgba(0,0,0,0.5)',
    fontFamily:"'Lilita One',cursive", fontSize:22, color:'#fef5e0',
    letterSpacing:3, textShadow:'0 2px 4px rgba(0,0,0,0.6), 0 0 8px rgba(245,200,66,0.3)',
    flexShrink:0,
  }}>
    {time}
  </div>
);

// ─── CHICKEN (richer, dimensional) ─────────────────────────
const CWChicken = ({ classKey, size=100, showRing, ringColor }) => {
  const cls = CW_CLASSES[classKey]; if (!cls) return null;
  const c = cls.color, d = cls.dark, l = cls.light;
  const dims = { wide:{bx:38,by:30,hr:14}, lean:{bx:24,by:28,hr:12}, broad:{bx:30,by:28,hr:13}, sleek:{bx:22,by:25,hr:11} }[cls.shape];
  const uid = `chk-${classKey}-${Math.random().toString(36).substr(2,4)}`;
  return (
    <svg viewBox="0 0 100 110" width={size} height={size*1.1}>
      <defs>
        <radialGradient id={`${uid}-body`} cx="40%" cy="35%" r="60%">
          <stop offset="0%" stopColor={l} />
          <stop offset="50%" stopColor={c} />
          <stop offset="100%" stopColor={d} />
        </radialGradient>
        <radialGradient id={`${uid}-head`} cx="40%" cy="30%" r="65%">
          <stop offset="0%" stopColor={l} />
          <stop offset="60%" stopColor={c} />
          <stop offset="100%" stopColor={d} />
        </radialGradient>
        <filter id={`${uid}-shadow`} x="-20%" y="-10%" width="140%" height="130%">
          <feDropShadow dx="0" dy="3" stdDeviation="3" floodColor="#000" floodOpacity="0.35" />
        </filter>
      </defs>
      {/* Ground shadow */}
      <ellipse cx="50" cy="100" rx={dims.bx*0.7} ry="5" fill="rgba(0,0,0,0.25)" />
      {/* Player ring */}
      {showRing && <ellipse cx="50" cy="96" rx={dims.bx*0.55} ry="6" fill={ringColor||CW_PLAYER_COLORS[0]} opacity="0.8" stroke={adjustColor(ringColor||CW_PLAYER_COLORS[0],30)} strokeWidth="1.5" />}
      <g filter={`url(#${uid}-shadow)`}>
        {/* Body */}
        <ellipse cx="50" cy="62" rx={dims.bx} ry={dims.by} fill={`url(#${uid}-body)`} />
        {/* Body highlight */}
        <ellipse cx="42" cy="52" rx={dims.bx*0.4} ry={dims.by*0.35} fill="rgba(255,255,255,0.12)" />
        {/* Wing */}
        <ellipse cx={50-dims.bx+12} cy="64" rx={dims.bx*0.25} ry={dims.by*0.32} fill={d} opacity="0.5" />
        {/* Legs */}
        <line x1="43" y1={62+dims.by-4} x2="39" y2={62+dims.by+14} stroke="#E8A020" strokeWidth="3" strokeLinecap="round" />
        <line x1="57" y1={62+dims.by-4} x2="61" y2={62+dims.by+14} stroke="#E8A020" strokeWidth="3" strokeLinecap="round" />
        {/* Feet */}
        <line x1="39" y1={62+dims.by+14} x2="34" y2={62+dims.by+16} stroke="#D48810" strokeWidth="2" strokeLinecap="round" />
        <line x1="39" y1={62+dims.by+14} x2="42" y2={62+dims.by+17} stroke="#D48810" strokeWidth="2" strokeLinecap="round" />
        <line x1="61" y1={62+dims.by+14} x2="56" y2={62+dims.by+16} stroke="#D48810" strokeWidth="2" strokeLinecap="round" />
        <line x1="61" y1={62+dims.by+14} x2="64" y2={62+dims.by+17} stroke="#D48810" strokeWidth="2" strokeLinecap="round" />
        {/* Head */}
        <circle cx="50" cy={62-dims.by-dims.hr*0.2} r={dims.hr} fill={`url(#${uid}-head)`} />
        {/* Head highlight */}
        <circle cx={48} cy={62-dims.by-dims.hr*0.5} r={dims.hr*0.3} fill="rgba(255,255,255,0.15)" />
        {/* Comb */}
        <path d={`M${45},${62-dims.by-dims.hr*1.3} Q${47},${62-dims.by-dims.hr*1.8} ${50},${62-dims.by-dims.hr*1.3} Q${53},${62-dims.by-dims.hr*1.8} ${55},${62-dims.by-dims.hr*1.3}`} fill="#e74c3c" stroke="#c0392b" strokeWidth="0.8" />
        {/* Beak */}
        <polygon points={`${50+dims.hr},${62-dims.by-dims.hr*0.25} ${50+dims.hr+10},${62-dims.by-dims.hr*0.05} ${50+dims.hr},${62-dims.by+3}`} fill="#F0A020" stroke="#D08810" strokeWidth="0.5" />
        <line x1={50+dims.hr} y1={62-dims.by-dims.hr*0.1} x2={50+dims.hr+8} y2={62-dims.by-dims.hr*0.05} stroke="#D08810" strokeWidth="0.5" />
        {/* Eye */}
        <circle cx={50+dims.hr*0.3} cy={62-dims.by-dims.hr*0.45} r={3} fill="#fff" />
        <circle cx={50+dims.hr*0.4} cy={62-dims.by-dims.hr*0.45} r={1.8} fill="#1a1a1a" />
        <circle cx={50+dims.hr*0.5} cy={62-dims.by-dims.hr*0.55} r={0.8} fill="#fff" />
      </g>
    </svg>
  );
};

// ─── RIBBON BANNER ─────────────────────────────────────────
const CWRibbon = ({ children, color, width=280 }) => {
  const c = color || '#d4a020';
  const dk = adjustColor(c, -30);
  return (
    <div style={{ position:'relative', width, textAlign:'center', margin:'0 auto' }}>
      {/* Ribbon tails */}
      <svg style={{ position:'absolute', left:-18, top:'50%', transform:'translateY(-50%)', width:20, height:40 }} viewBox="0 0 20 40">
        <polygon points="20,0 4,0 0,20 4,40 20,40" fill={dk} />
      </svg>
      <svg style={{ position:'absolute', right:-18, top:'50%', transform:'translateY(-50%)', width:20, height:40 }} viewBox="0 0 20 40">
        <polygon points="0,0 16,0 20,20 16,40 0,40" fill={dk} />
      </svg>
      <div style={{
        padding:'6px 28px', background:`linear-gradient(180deg, ${adjustColor(c,20)}, ${c}, ${dk})`,
        border:`2px solid ${adjustColor(c,30)}`,
        boxShadow:`inset 0 2px 0 rgba(255,255,255,0.3), inset 0 -2px 0 rgba(0,0,0,0.2), 0 3px 8px rgba(0,0,0,0.4)`,
        borderRadius:4, position:'relative', zIndex:1,
        fontFamily:"'Lilita One',cursive", fontSize:18, color:'#fef5e0',
        textShadow:'0 2px 4px rgba(0,0,0,0.5)', letterSpacing:2,
      }}>
        {children}
      </div>
    </div>
  );
};

// ─── GLOSSY BUTTON ─────────────────────────────────────────
const CWButton = ({ children, bg, border, glow, style={}, onClick }) => {
  const t = CW_THEME;
  return (
    <div onClick={onClick} style={{
      padding:'10px 24px', borderRadius: t.btnR, cursor:'pointer',
      background: bg || t.btnBg, border: `2px solid ${border || t.btnBorder}`,
      boxShadow: glow || t.btnGlow,
      fontFamily:"'Lilita One',cursive", fontSize:16, color:'#fef5e0',
      textShadow: t.tShadow, textAlign:'center', letterSpacing:1,
      ...style,
    }}>
      {children}
    </div>
  );
};

// ─── GLOSSY PANEL ──────────────────────────────────────────
const CWPanel = ({ children, width, style={} }) => {
  const t = CW_THEME;
  return (
    <div style={{
      width, background: t.panelOuter, border: `3px solid ${t.panelBorder}`,
      borderRadius: t.panelR, boxShadow: t.panelGlow,
      ...style,
    }}>
      <div style={{
        background: t.panelInner, borderRadius: t.panelR - 2,
        border: '1px solid rgba(255,200,100,0.08)',
        ...style,
      }}>
        {children}
      </div>
    </div>
  );
};

// ─── FOOD ICON ─────────────────────────────────────────────
const CWFoodIcon = ({ size=14 }) => (
  <svg viewBox="0 0 20 20" width={size} height={size} style={{ verticalAlign:'middle' }}>
    <defs><radialGradient id="food-g" cx="40%" cy="35%"><stop offset="0%" stopColor="#ffe066" /><stop offset="100%" stopColor="#d4a020" /></radialGradient></defs>
    <circle cx="10" cy="12" r="7" fill="url(#food-g)" stroke="#b08018" strokeWidth="0.5" />
    <ellipse cx="10" cy="7" rx="3.5" ry="4.5" fill="#5ac54f" stroke="#33a332" strokeWidth="0.5" />
  </svg>
);

// ─── SVG DEFS ──────────────────────────────────────────────
const CWSvgDefs = () => (<svg width="0" height="0" style={{ position:'absolute' }}></svg>);

Object.assign(window, {
  CW_PLAYER_COLORS, CW_PLAYER_NAMES, CW_CLASSES, CW_CLASS_KEYS,
  CW_STAT_LABELS, CW_STAT_ORDER, CW_ABILITIES, CW_THEME,
  CWHex, CWStatBar, CWPlayerBadge, CWTimer, CWChicken, CWRibbon,
  CWButton, CWPanel, CWFoodIcon, CWSvgDefs, adjustColor,
});
