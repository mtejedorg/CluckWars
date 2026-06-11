// CluckWars UI — Design Tokens & Shared Components
// ==================================================

// Color-blind safe player identity colors (Okabe-Ito derived)
const CW_PLAYER_COLORS = ['#E8751A', '#1A7FC4', '#C4286F', '#0D9E7A'];
const CW_PLAYER_NAMES = ['P1', 'P2', 'P3', 'P4'];

// Class data from GDD
const CW_CLASSES = {
  fatty:    { name: 'Fatty Chicken',    short: 'Fatty',    role: 'Bulk Carrier', lore: '"Slow and steady wins the race — if it survives long enough."', color: '#F5D75A', dark: '#B89E20', stats: { cargo:5, rate:5, hp:4, resist:4, speed:2, attack:2 }, shape:'wide' },
  speedy:   { name: 'Speedy Chicken',   short: 'Speedy',   role: 'Hit & Run',    lore: '"If you can\'t catch me, you can\'t kill me."',                color: '#E85A2A', dark: '#B84418', stats: { cargo:3, rate:3, hp:2, resist:1, speed:5, attack:3 }, shape:'lean' },
  warrior:  { name: 'Warrior Chicken',  short: 'Warrior',  role: 'All-Rounder',  lore: '"The farm is a battlefield."',                                 color: '#C04030', dark: '#8A2A20', stats: { cargo:3, rate:3, hp:3, resist:3, speed:3, attack:4 }, shape:'broad' },
  assassin: { name: 'Assassin Chicken', short: 'Assassin', role: 'Disruptor',    lore: '"Blink and your food is gone."',                               color: '#7B68EE', dark: '#5A48C8', stats: { cargo:2, rate:2, hp:2, resist:2, speed:4, attack:5 }, shape:'sleek', twoSlots:true },
};
const CW_CLASS_KEYS = ['fatty','speedy','warrior','assassin'];

const CW_STAT_LABELS = { cargo:'Cargo', rate:'Rate', hp:'HP', resist:'Resist', speed:'Speed', attack:'Attack' };
const CW_STAT_ORDER = ['cargo','rate','hp','resist','speed','attack'];

// Abilities from GDD
const CW_ABILITIES = [
  { id:'speed',  name:'Speed Burst',    short:'SPD',  color:'#00BCD4', type:'Mobility' },
  { id:'egg',    name:'Egg Shell',      short:'EGG',  color:'#FFC107', type:'Defensive' },
  { id:'roll',   name:'Roll & Trample', short:'ROLL', color:'#FF5722', type:'Offensive' },
  { id:'doppel', name:'Doppelganger',   short:'DPLG', color:'#9C27B0', type:'Deceptive' },
  { id:'invis',  name:'Invisibility',   short:'INVIS',color:'#607D8B', type:'Deceptive' },
  { id:'spine',  name:'Spine Coat',     short:'SPNE', color:'#4CAF50', type:'Reactive' },
  { id:'turtle', name:'Turtle Mode',    short:'TRTL', color:'#795548', type:'Defensive' },
  { id:'sneak',  name:'Sneaky Steal',   short:'SNKY', color:'#E91E63', type:'Utility' },
];

// ─── Theme palettes ────────────────────────────────────────
const CW_THEMES = {
  barnyard: {
    name: 'Barnyard Bold',
    screenBg:  'radial-gradient(ellipse at 50% 30%, #3a2810 0%, #1e150d 70%)',
    mapBg:     'radial-gradient(ellipse at 50% 50%, #2a3018 0%, #1a1d10 55%, #121008 100%)',
    panel:     'linear-gradient(180deg, #4a3520, #3a2816)',
    panelBdr:  '2px solid #6b4f35',
    panelR:    8,
    panelShd:  '0 4px 16px rgba(0,0,0,0.5), inset 0 1px 0 rgba(255,200,100,0.08)',
    card:      'linear-gradient(180deg, #503a24, #3d2b1a)',
    cardBdr:   '2px solid #6b4f35',
    cardSelBdr:'3px solid #f5c842',
    cardR:     10,
    btn:       'linear-gradient(180deg, #8b6914, #6b4f0f)',
    btnBdr:    '2px solid #a07818',
    btnR:      8,
    btnShd:    '0 3px 6px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,220,100,0.2)',
    startBtn:  'linear-gradient(180deg, #c49b1a, #9a7a0f)',
    t1:        '#fef5e7',
    t2:        '#c4a66a',
    t3:        '#f5c842',
    barBg:     '#2a1d10',
    barBdr:    '1px solid #4a3520',
    hp:        '#c0392b',
    cargo:     '#f39c12',
    timerBg:   'rgba(30,21,13,0.88)',
    overlayBg: 'rgba(20,14,8,0.92)',
    hudBarBg:  'rgba(30,21,13,0.7)',
  },
  cluckpop: {
    name: 'Cluck Pop',
    screenBg:  'linear-gradient(180deg, #1a1a2e, #16213e)',
    mapBg:     'linear-gradient(180deg, #1a2a1a, #0f1a0f)',
    panel:     '#fef5e7',
    panelBdr:  '3px solid #2c2c2c',
    panelR:    16,
    panelShd:  '0 5px 0 #2c2c2c',
    card:      '#fff8ee',
    cardBdr:   '3px solid #2c2c2c',
    cardSelBdr:'3px solid #e67e22',
    cardR:     14,
    btn:       'linear-gradient(180deg, #f39c12, #e67e22)',
    btnBdr:    '3px solid #2c2c2c',
    btnR:      12,
    btnShd:    '0 3px 0 #2c2c2c',
    startBtn:  'linear-gradient(180deg, #2ecc71, #27ae60)',
    t1:        '#2c2c2c',
    t2:        '#6b6b6b',
    t3:        '#e67e22',
    barBg:     '#e8dcc8',
    barBdr:    '2px solid #2c2c2c',
    hp:        '#e74c3c',
    cargo:     '#f39c12',
    timerBg:   'rgba(26,26,46,0.92)',
    overlayBg: 'rgba(26,26,46,0.94)',
    hudBarBg:  'rgba(26,26,46,0.75)',
  },
};

// ─── Hex-button SVG component ──────────────────────────────
const CWHex = ({ size=72, color='#3388cc', label, glow, style={} }) => {
  const h = size * 1.155;
  return (
    <div style={{ width:size, height:h, position:'relative', flexShrink:0, ...style }}>
      <svg viewBox="0 0 100 115.5" width="100%" height="100%">
        {glow && <polygon points="50,0 100,28.87 100,86.6 50,115.5 0,86.6 0,28.87" fill={color} opacity="0.35" filter="url(#hexglow)" />}
        <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87" fill={color} stroke="rgba(255,255,255,0.12)" strokeWidth="1.5" />
        <polygon points="50,10 90,32 90,83.5 50,105.5 10,83.5 10,32" fill="none" stroke="rgba(255,255,255,0.06)" strokeWidth="1" />
      </svg>
      {label && <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center', color:'#fff', fontWeight:700, fontSize:size*0.2, fontFamily:"'Lilita One',cursive", textShadow:'0 1px 3px rgba(0,0,0,0.6)', letterSpacing:0.5, pointerEvents:'none' }}>{label}</div>}
    </div>
  );
};

// ─── Stat bar ──────────────────────────────────────────────
const CWStatBar = ({ value, max=5, label, color, theme='barnyard', compact }) => {
  const t = CW_THEMES[theme];
  const pct = (value/max)*100;
  const dots = [];
  for (let i = 0; i < max; i++) dots.push(i < value);
  if (compact) {
    return (
      <div style={{ display:'flex', alignItems:'center', gap:3 }}>
        <span style={{ fontFamily:t.bodyFont||"'Nunito',sans-serif", fontSize:9, color:t.t2, width:36, textAlign:'right', flexShrink:0 }}>{label}</span>
        <div style={{ display:'flex', gap:2 }}>
          {dots.map((on,i) => <div key={i} style={{ width:8, height:8, borderRadius:2, background: on ? (color||t.t3) : t.barBg, border: `1px solid ${on ? 'transparent' : t.t2}44`, transition:'all .2s' }} />)}
        </div>
      </div>
    );
  }
  return (
    <div style={{ display:'flex', alignItems:'center', gap:6 }}>
      <span style={{ fontFamily:"'Nunito',sans-serif", fontSize:11, color:t.t2, width:44, textAlign:'right', flexShrink:0 }}>{label}</span>
      <div style={{ flex:1, height:10, background:t.barBg, borderRadius:5, overflow:'hidden', border:t.barBdr }}>
        <div style={{ width:`${pct}%`, height:'100%', background: color||t.t3, borderRadius:5, boxShadow:'inset 0 1px 0 rgba(255,255,255,0.25)' }} />
      </div>
    </div>
  );
};

// ─── Player badge ──────────────────────────────────────────
const CWPlayerBadge = ({ index, score, small }) => {
  const c = CW_PLAYER_COLORS[index];
  return (
    <div style={{ display:'flex', alignItems:'center', gap: small?3:5, padding: small?'2px 5px':'3px 8px', borderRadius:6, background:`${c}18` }}>
      <div style={{ width: small?7:10, height: small?7:10, borderRadius:'50%', background:c, boxShadow:`0 0 5px ${c}66` }} />
      <span style={{ fontFamily:"'Lilita One',cursive", fontSize: small?12:15, color:c }}>{CW_PLAYER_NAMES[index]}:{score}</span>
    </div>
  );
};

// ─── Chicken placeholder ───────────────────────────────────
const CWChicken = ({ classKey, size=100 }) => {
  const cls = CW_CLASSES[classKey]; if (!cls) return null;
  const c = cls.color;
  const dims = { wide:{bx:38,by:30,hr:14}, lean:{bx:24,by:28,hr:12}, broad:{bx:30,by:28,hr:13}, sleek:{bx:22,by:25,hr:11} }[cls.shape];
  return (
    <svg viewBox="0 0 100 105" width={size} height={size*1.05}>
      <ellipse cx="50" cy="58" rx={dims.bx} ry={dims.by} fill={c} />
      <circle cx="50" cy={58-dims.by-dims.hr*0.3} r={dims.hr} fill={c} />
      <polygon points={`${50+dims.hr},${58-dims.by-dims.hr*0.35} ${50+dims.hr+9},${58-dims.by-dims.hr*0.15} ${50+dims.hr},${58-dims.by+2}`} fill="#E8A020" />
      <circle cx={50+dims.hr*0.35} cy={58-dims.by-dims.hr*0.5} r={2.5} fill="#1a1a1a" />
      <ellipse cx="50" cy={58-dims.by-dims.hr*1.1} rx={5} ry={7} fill="#e74c3c" />
      <line x1="43" y1={58+dims.by-4} x2="40" y2={58+dims.by+12} stroke="#E8A020" strokeWidth="2.5" strokeLinecap="round" />
      <line x1="57" y1={58+dims.by-4} x2="60" y2={58+dims.by+12} stroke="#E8A020" strokeWidth="2.5" strokeLinecap="round" />
      <ellipse cx={50-dims.bx+10} cy="60" rx={dims.bx*0.22} ry={dims.by*0.28} fill={cls.dark} opacity="0.5" />
    </svg>
  );
};

// ─── Cooldown ring (for HUD hex buttons) ───────────────────
const CWCooldownHex = ({ size=72, color='#3388cc', label, pct=0, style={} }) => {
  const h = size * 1.155;
  return (
    <div style={{ width:size, height:h, position:'relative', flexShrink:0, ...style }}>
      <svg viewBox="0 0 100 115.5" width="100%" height="100%">
        <defs>
          <clipPath id={`cd-clip-${label}`}><rect x="0" y={115.5*(1-pct)} width="100" height={115.5*pct} /></clipPath>
        </defs>
        <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87" fill={color} stroke="rgba(255,255,255,0.12)" strokeWidth="1.5" />
        {pct > 0 && <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87" fill="rgba(0,0,0,0.55)" clipPath={`url(#cd-clip-${label})`} />}
      </svg>
      {label && <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center', color:'#fff', fontWeight:700, fontSize:size*0.2, fontFamily:"'Lilita One',cursive", textShadow:'0 1px 3px rgba(0,0,0,0.6)', pointerEvents:'none' }}>{label}</div>}
    </div>
  );
};

// ─── Timer display ─────────────────────────────────────────
const CWTimer = ({ time = '02:34', theme = 'barnyard' }) => {
  const t = CW_THEMES[theme];
  return (
    <div style={{
      background: t.timerBg, padding: '4px 14px', borderRadius: 8,
      border: theme === 'cluckpop' ? '2px solid #2c2c2c' : '1px solid rgba(255,200,100,0.2)',
      fontFamily: "'Lilita One',cursive", fontSize: 20, color: '#fef5e7',
      letterSpacing: 2, textShadow: '0 1px 2px rgba(0,0,0,0.5)', flexShrink: 0,
    }}>
      {time}
    </div>
  );
};

// ─── Food icon ─────────────────────────────────────────────
const CWFoodIcon = ({ size = 14, color = '#f5c842' }) => (
  <svg viewBox="0 0 20 20" width={size} height={size} style={{ verticalAlign: 'middle' }}>
    <circle cx="10" cy="12" r="7" fill={color} />
    <ellipse cx="10" cy="7" rx="3" ry="4" fill="#4CAF50" />
  </svg>
);

// ─── Shared SVG filter defs (inject once) ──────────────────
const CWSvgDefs = () => (
  <svg width="0" height="0" style={{ position:'absolute' }}>
    <defs>
      <filter id="hexglow" x="-50%" y="-50%" width="200%" height="200%">
        <feGaussianBlur stdDeviation="6" />
      </filter>
    </defs>
  </svg>
);

// Export all
Object.assign(window, {
  CW_PLAYER_COLORS, CW_PLAYER_NAMES, CW_CLASSES, CW_CLASS_KEYS,
  CW_STAT_LABELS, CW_STAT_ORDER, CW_ABILITIES, CW_THEMES,
  CWHex, CWStatBar, CWPlayerBadge, CWChicken, CWCooldownHex, CWSvgDefs,
  CWTimer, CWFoodIcon,
});
