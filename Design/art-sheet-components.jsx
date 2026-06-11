// ============================================================
// CluckWars Concept Art Sheet — Illustration Components
// Detailed character, food pile, and map reference art
// ============================================================

// --- STYLING CONSTANTS --------------------------------------
const ART = {
  bg: '#0e0804', bg2: '#1a120a',
  annot: '#c4a060', annotDim: 'rgba(196,160,96,0.3)',
  grid: 'rgba(138,106,58,0.08)', text: '#fef5e0', dim: '#8a6a40',
};

// Chicken shape params (for viewBox 0 0 200 250)
const CSHAPES = {
  fatty:    { bx:48, by:38, hr:18, hoy:52, hox:0,  ls:22, bcy:148 },
  speedy:   { bx:28, by:34, hr:14, hoy:46, hox:5,  ls:13, bcy:150 },
  warrior:  { bx:36, by:33, hr:16, hoy:44, hox:1,  ls:16, bcy:148 },
  assassin: { bx:25, by:30, hr:13, hoy:36, hox:6,  ls:11, bcy:153 },
};

const CLASS_NOTES = {
  fatty:    ['Widest body — lowest center of gravity', 'Short stubby legs, wide wobbling stance', 'Soft, round proportions throughout'],
  speedy:   ['Lean, narrow body — angular posture', 'Forward-leaning head — alert, ready to bolt', 'Longer legs relative to body size'],
  warrior:  ['Broad-shouldered, upright confidence', 'Balanced proportions — no extreme', 'Strong, grounded, wider stance'],
  assassin: ['Sleekest, most compact silhouette', 'Hunched posture — coiled and ready', 'Smallest overall profile — hard to hit'],
};

// --- DETAILED CHICKEN SVG -----------------------------------
const DetailedChicken = ({ classKey, size = 220, showRing, ringColor }) => {
  const cls = CW_CLASSES[classKey]; if (!cls) return null;
  const sh = CSHAPES[classKey];
  const { color: c, dark: d, light: l } = cls;
  const uid = `dc-${classKey}-${(Math.random()*1e6|0)}`;
  const cx = 100, hx = cx + sh.hox, hy = sh.bcy - sh.hoy;
  const bb = sh.bcy + sh.by, legY1 = bb - 5, legY2 = 222;
  const lx = cx - sh.ls / 2, rx = cx + sh.ls / 2;

  const foot = (fx, fy) => (
    <React.Fragment>
      <line x1={fx} y1={fy} x2={fx-9} y2={fy+5} stroke="#D48810" strokeWidth="2.5" strokeLinecap="round" />
      <line x1={fx} y1={fy} x2={fx+1} y2={fy+6} stroke="#D48810" strokeWidth="2.5" strokeLinecap="round" />
      <line x1={fx} y1={fy} x2={fx+8} y2={fy+4} stroke="#D48810" strokeWidth="2.5" strokeLinecap="round" />
    </React.Fragment>
  );

  return (
    <svg viewBox="0 0 200 250" width={size} height={size*1.25} style={{ overflow:'visible' }}>
      <defs>
        <radialGradient id={`${uid}-bg`} cx="38%" cy="32%" r="62%">
          <stop offset="0%" stopColor={l}/><stop offset="45%" stopColor={c}/><stop offset="100%" stopColor={d}/>
        </radialGradient>
        <radialGradient id={`${uid}-hg`} cx="35%" cy="28%" r="65%">
          <stop offset="0%" stopColor={l}/><stop offset="50%" stopColor={c}/><stop offset="100%" stopColor={d}/>
        </radialGradient>
        <filter id={`${uid}-ds`} x="-15%" y="-10%" width="130%" height="125%">
          <feDropShadow dx="0" dy="3" stdDeviation="4" floodColor="#000" floodOpacity="0.3"/>
        </filter>
      </defs>

      {/* Ground shadow */}
      <ellipse cx={cx} cy={232} rx={sh.bx*0.6} ry={6} fill="rgba(0,0,0,0.25)"/>
      {showRing && <ellipse cx={cx} cy={229} rx={sh.bx*0.48} ry={7} fill={ringColor||'#E8751A'} opacity="0.7" stroke={`${ringColor||'#E8751A'}88`} strokeWidth="2"/>}

      <g filter={`url(#${uid}-ds)`}>
        {/* Legs */}
        <line x1={lx} y1={legY1} x2={lx-3} y2={legY2} stroke="#E8A020" strokeWidth="4.5" strokeLinecap="round"/>
        <line x1={rx} y1={legY1} x2={rx+3} y2={legY2} stroke="#E8A020" strokeWidth="4.5" strokeLinecap="round"/>
        {foot(lx-3, legY2)}{foot(rx+3, legY2)}

        {/* Tail tuft */}
        <ellipse cx={cx-sh.bx+6} cy={sh.bcy-8} rx={9} ry={13} fill={d} opacity="0.4" transform={`rotate(20,${cx-sh.bx+6},${sh.bcy-8})`}/>

        {/* Body */}
        <ellipse cx={cx} cy={sh.bcy} rx={sh.bx} ry={sh.by} fill={`url(#${uid}-bg)`} stroke={d} strokeWidth="0.8"/>
        <ellipse cx={cx-6} cy={sh.bcy-sh.by*0.25} rx={sh.bx*0.4} ry={sh.by*0.35} fill="rgba(255,255,255,0.1)"/>

        {/* Wing */}
        <ellipse cx={cx-sh.bx+15} cy={sh.bcy+3} rx={sh.bx*0.28} ry={sh.by*0.5} fill={d} opacity="0.4"/>
        <line x1={cx-sh.bx+10} y1={sh.bcy-2} x2={cx-sh.bx+22} y2={sh.bcy+16} stroke={d} strokeWidth="0.6" opacity="0.25"/>
        <line x1={cx-sh.bx+14} y1={sh.bcy-5} x2={cx-sh.bx+24} y2={sh.bcy+12} stroke={d} strokeWidth="0.6" opacity="0.25"/>

        {/* Head */}
        <circle cx={hx} cy={hy} r={sh.hr} fill={`url(#${uid}-hg)`} stroke={d} strokeWidth="0.6"/>
        <circle cx={hx-3} cy={hy-sh.hr*0.35} r={sh.hr*0.3} fill="rgba(255,255,255,0.12)"/>

        {/* Comb */}
        <path d={`M${hx-8},${hy-sh.hr+1}Q${hx-4},${hy-sh.hr-14}${hx},${hy-sh.hr}Q${hx+4},${hy-sh.hr-16}${hx+6},${hy-sh.hr+1}Q${hx+10},${hy-sh.hr-10}${hx+10},${hy-sh.hr+4}`} fill="#e74c3c" stroke="#c0392b" strokeWidth="1"/>

        {/* Beak */}
        <polygon points={`${hx+sh.hr-2},${hy-2} ${hx+sh.hr+sh.hr*0.8},${hy+2} ${hx+sh.hr-2},${hy+6}`} fill="#F0A020" stroke="#D08810" strokeWidth="0.8"/>
        <line x1={hx+sh.hr-1} y1={hy+1.5} x2={hx+sh.hr+sh.hr*0.7} y2={hy+2} stroke="#D08810" strokeWidth="0.6"/>

        {/* Wattle */}
        <ellipse cx={hx+sh.hr*0.3} cy={hy+sh.hr-2} rx={3} ry={5} fill="#e74c3c" stroke="#c0392b" strokeWidth="0.5"/>

        {/* Eye */}
        <circle cx={hx+sh.hr*0.35} cy={hy-sh.hr*0.1} r={4} fill="#fff"/>
        <circle cx={hx+sh.hr*0.42} cy={hy-sh.hr*0.1} r={2.5} fill="#1a1a1a"/>
        <circle cx={hx+sh.hr*0.48} cy={hy-sh.hr*0.2} r={1} fill="#fff"/>
      </g>
    </svg>
  );
};

// --- CHARACTER DETAIL CARD ----------------------------------
const ArtCharacterCard = ({ classKey }) => {
  const cls = CW_CLASSES[classKey];
  const notes = CLASS_NOTES[classKey];
  return (
    <div style={{ width:'100%',height:'100%',background:ART.bg,fontFamily:"'Nunito',sans-serif",display:'flex',padding:28,gap:24,position:'relative',overflow:'hidden' }}>
      {/* Background grid */}
      <svg style={{ position:'absolute',inset:0,width:'100%',height:'100%',opacity:0.5 }}>
        {Array.from({length:18}).map((_,i) => <React.Fragment key={i}>
          <line x1={i*40} y1="0" x2={i*40} y2="500" stroke={ART.grid} strokeWidth="0.5"/>
          <line x1="0" y1={i*40} x2="700" y2={i*40} stroke={ART.grid} strokeWidth="0.5"/>
        </React.Fragment>)}
      </svg>

      {/* Left: chicken */}
      <div style={{ display:'flex',alignItems:'center',justifyContent:'center',width:'42%',position:'relative' }}>
        <DetailedChicken classKey={classKey} size={210} showRing ringColor={CW_PLAYER_COLORS[0]}/>
        {/* Silhouette outline behind */}
        <div style={{ position:'absolute',inset:0,display:'flex',alignItems:'center',justifyContent:'center',opacity:0.04 }}>
          <DetailedChicken classKey={classKey} size={230}/>
        </div>
      </div>

      {/* Right: info */}
      <div style={{ flex:1,display:'flex',flexDirection:'column',gap:16,justifyContent:'center',zIndex:1 }}>
        <div>
          <div style={{ fontFamily:"'Lilita One',cursive",fontSize:28,color:cls.color,textShadow:`0 0 12px ${cls.color}44, 0 2px 4px rgba(0,0,0,0.5)`,letterSpacing:1 }}>{cls.name}</div>
          <div style={{ fontSize:13,color:ART.annot,fontWeight:700,letterSpacing:2,textTransform:'uppercase',marginTop:2 }}>{cls.role}</div>
        </div>
        <div style={{ fontSize:12,color:ART.dim,fontStyle:'italic',maxWidth:280 }}>{cls.lore}</div>

        {/* Color palette */}
        <div>
          <div style={{ fontSize:10,color:ART.annot,fontWeight:700,letterSpacing:1.5,textTransform:'uppercase',marginBottom:6 }}>Color Palette</div>
          <div style={{ display:'flex',gap:10 }}>
            {[{label:'Primary',hex:cls.color},{label:'Dark',hex:cls.dark},{label:'Light',hex:cls.light}].map((sw,i) => (
              <div key={i} style={{ display:'flex',flexDirection:'column',alignItems:'center',gap:3 }}>
                <div style={{ width:48,height:30,borderRadius:6,background:sw.hex,border:'1px solid rgba(255,255,255,0.08)',boxShadow:'0 2px 4px rgba(0,0,0,0.3)' }}/>
                <span style={{ fontSize:8,color:ART.dim }}>{sw.label}</span>
                <span style={{ fontSize:8,color:ART.annot,fontFamily:'monospace' }}>{sw.hex}</span>
              </div>
            ))}
          </div>
        </div>

        {/* Silhouette notes */}
        <div>
          <div style={{ fontSize:10,color:ART.annot,fontWeight:700,letterSpacing:1.5,textTransform:'uppercase',marginBottom:6 }}>Silhouette Notes</div>
          <div style={{ display:'flex',flexDirection:'column',gap:5 }}>
            {notes.map((n,i) => (
              <div key={i} style={{ display:'flex',alignItems:'center',gap:6 }}>
                <div style={{ width:4,height:4,borderRadius:'50%',background:cls.color,flexShrink:0 }}/>
                <span style={{ fontSize:11,color:ART.text }}>{n}</span>
              </div>
            ))}
          </div>
        </div>

        {/* Stats */}
        <div>
          <div style={{ fontSize:10,color:ART.annot,fontWeight:700,letterSpacing:1.5,textTransform:'uppercase',marginBottom:6 }}>Stat Spread</div>
          <div style={{ display:'grid',gridTemplateColumns:'1fr 1fr',gap:'4px 16px' }}>
            {CW_STAT_ORDER.map(stat => (
              <div key={stat} style={{ display:'flex',alignItems:'center',gap:4 }}>
                <span style={{ fontSize:9,color:ART.dim,width:40 }}>{CW_STAT_LABELS[stat]}</span>
                <div style={{ display:'flex',gap:2 }}>
                  {Array.from({length:5}).map((_,i) => (
                    <div key={i} style={{ width:8,height:8,borderRadius:2,background:i<cls.stats[stat]?cls.color:'rgba(255,255,255,0.06)',opacity:i<cls.stats[stat]?1:0.4 }}/>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};

// --- CHARACTER LINEUP ----------------------------------------
const ArtLineup = () => (
  <div style={{ width:'100%',height:'100%',background:ART.bg,fontFamily:"'Nunito',sans-serif",display:'flex',flexDirection:'column',position:'relative',overflow:'hidden' }}>
    {/* Proportion grid */}
    <svg style={{ position:'absolute',inset:0,width:'100%',height:'100%' }}>
      {[80,140,200,260,340].map(y => <line key={y} x1="40" y1={y} x2="1060" y2={y} stroke={ART.grid} strokeWidth="0.5" strokeDasharray="4,4"/>)}
      <text x="24" y="344" fill={ART.dim} fontSize="8" fontFamily="Nunito" textAnchor="middle">base</text>
      <text x="24" y="84" fill={ART.dim} fontSize="8" fontFamily="Nunito" textAnchor="middle">top</text>
    </svg>

    {/* Title */}
    <div style={{ padding:'14px 0 0',textAlign:'center' }}>
      <span style={{ fontFamily:"'Lilita One',cursive",fontSize:16,color:ART.annot,letterSpacing:3,textTransform:'uppercase',textShadow:'0 1px 3px rgba(0,0,0,0.4)' }}>Relative Scale &amp; Silhouette Comparison</span>
    </div>

    {/* Characters row */}
    <div style={{ flex:1,display:'flex',alignItems:'flex-end',justifyContent:'center',gap:60,paddingBottom:46 }}>
      {CW_CLASS_KEYS.map((key, i) => {
        const cls = CW_CLASSES[key];
        return (
          <div key={key} style={{ display:'flex',flexDirection:'column',alignItems:'center',gap:6 }}>
            <DetailedChicken classKey={key} size={180} showRing ringColor={CW_PLAYER_COLORS[i]}/>
            <div style={{ textAlign:'center' }}>
              <div style={{ fontFamily:"'Lilita One',cursive",fontSize:16,color:cls.color,textShadow:`0 0 8px ${cls.color}33` }}>{cls.short}</div>
              <div style={{ fontSize:10,color:ART.dim }}>{cls.role}</div>
            </div>
          </div>
        );
      })}
    </div>

    {/* Player identity strip */}
    <div style={{ display:'flex',justifyContent:'center',gap:24,padding:'0 0 14px' }}>
      {CW_PLAYER_COLORS.map((pc,i) => (
        <div key={i} style={{ display:'flex',alignItems:'center',gap:5 }}>
          <div style={{ width:12,height:12,borderRadius:'50%',background:pc,boxShadow:`0 0 6px ${pc}66` }}/>
          <span style={{ fontSize:10,color:ART.text }}>P{i+1}</span>
          <span style={{ fontSize:8,color:ART.dim,fontFamily:'monospace' }}>{pc}</span>
        </div>
      ))}
    </div>
  </div>
);

// --- FOOD PILE STATES ----------------------------------------
const ArtFoodPiles = () => {
  const states = [
    { pct:100, label:'Full (100%)', color:'#F5C842', sz:1.0,  glow:true },
    { pct:75,  label:'75%',         color:'#E8B430', sz:0.82, glow:true },
    { pct:50,  label:'50%',         color:'#D48C20', sz:0.62, glow:false },
    { pct:25,  label:'Last Scraps', color:'#7A6040', sz:0.35, glow:false },
    { pct:0,   label:'Empty (0%)',  color:'#4A3828', sz:0.0,  glow:false },
  ];
  // Mound path: dome/arch shape
  const mound = (mcx, by, mrx, h) => `M${mcx-mrx},${by} Q${mcx},${by-h*2} ${mcx+mrx},${by} Z`;

  return (
    <div style={{ width:'100%',height:'100%',background:ART.bg,fontFamily:"'Nunito',sans-serif",display:'flex',flexDirection:'column',overflow:'hidden',position:'relative' }}>
      {/* Title */}
      <div style={{ padding:'16px 0 4px',textAlign:'center' }}>
        <span style={{ fontFamily:"'Lilita One',cursive",fontSize:16,color:ART.annot,letterSpacing:3,textTransform:'uppercase' }}>Food Pile Depletion — Visual States</span>
      </div>

      {/* Piles row */}
      <div style={{ flex:1,display:'flex',alignItems:'center',justifyContent:'space-around',padding:'0 50px' }}>
        {states.map((st, i) => {
          const pileCx=65, pileBy=90, maxRx=40, maxH=32;
          const prx = maxRx * st.sz, ph = maxH * st.sz;
          const gid = `fp-${i}-${(Math.random()*1e6|0)}`;
          return (
            <div key={i} style={{ display:'flex',flexDirection:'column',alignItems:'center',gap:10,position:'relative' }}>
              {/* Arrow between states */}
              {i > 0 && <div style={{ position:'absolute',left:-36,top:'42%',color:ART.annotDim,fontSize:18 }}>→</div>}

              <svg viewBox="0 0 130 120" width={130} height={120}>
                <defs>
                  <radialGradient id={`${gid}-g`} cx="40%" cy="30%">
                    <stop offset="0%" stopColor={adjustColor(st.color,40)}/><stop offset="60%" stopColor={st.color}/><stop offset="100%" stopColor={adjustColor(st.color,-30)}/>
                  </radialGradient>
                </defs>
                {/* Glow */}
                {st.glow && <ellipse cx={pileCx} cy={pileBy} rx={prx+15} ry={12} fill={st.color} opacity="0.1"/>}
                {/* Ground shadow */}
                <ellipse cx={pileCx} cy={pileBy+6} rx={prx+6} ry={5} fill="rgba(0,0,0,0.2)"/>
                {/* Mound */}
                {st.pct > 0 && <path d={mound(pileCx,pileBy,prx,ph)} fill={`url(#${gid}-g)`} stroke={adjustColor(st.color,-20)} strokeWidth="0.6"/>}
                {/* Highlight on mound */}
                {st.pct >= 50 && <ellipse cx={pileCx-prx*0.15} cy={pileBy-ph*0.7} rx={prx*0.3} ry={ph*0.3} fill="rgba(255,255,255,0.12)"/>}
                {/* Scattered grains for depleted */}
                {st.pct <= 25 && st.pct > 0 && [[-14,4],[12,6],[18,-2]].map(([ox,oy],j) => (
                  <circle key={j} cx={pileCx+ox} cy={pileBy+oy} r={1.5} fill={st.color} opacity="0.5"/>
                ))}
                {/* Ground mark for empty */}
                {st.pct === 0 && <>
                  <ellipse cx={pileCx} cy={pileBy} rx={18} ry={4} fill="rgba(74,56,40,0.25)" stroke="rgba(74,56,40,0.15)" strokeWidth="0.5" strokeDasharray="2,2"/>
                  <text x={pileCx} y={pileBy+2} fill={ART.dim} fontSize="6" fontFamily="Nunito" textAnchor="middle">remnant</text>
                </>}
              </svg>

              <div style={{ textAlign:'center' }}>
                <div style={{ fontSize:12,color:ART.text,fontWeight:700 }}>{st.label}</div>
                <div style={{ display:'flex',alignItems:'center',gap:4,justifyContent:'center',marginTop:3 }}>
                  <div style={{ width:14,height:14,borderRadius:3,background:st.color,border:'1px solid rgba(255,255,255,0.08)' }}/>
                  <span style={{ fontSize:9,color:ART.annot,fontFamily:'monospace' }}>{st.color}</span>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Continuous gradient bar */}
      <div style={{ padding:'0 80px 18px',display:'flex',flexDirection:'column',alignItems:'center',gap:4 }}>
        <div style={{ width:'100%',maxWidth:700,height:14,borderRadius:7,background:'linear-gradient(90deg, #F5C842, #E8B430, #D48C20, #7A6040, #4A3828)',border:'1px solid rgba(255,255,255,0.06)' }}/>
        <div style={{ display:'flex',justifyContent:'space-between',width:'100%',maxWidth:700 }}>
          <span style={{ fontSize:8,color:ART.dim }}>Full — vibrant gold, glow aura</span>
          <span style={{ fontSize:8,color:ART.dim }}>Depleted — dull brown, no glow</span>
        </div>
      </div>
    </div>
  );
};

// --- ARENA MAP CONCEPT ---------------------------------------
const ArtArenaMap = () => {
  const mcx=440, mcy=310, mw=280, mh=230;
  const top=[mcx,mcy-mh], rt=[mcx+mw,mcy], bot=[mcx,mcy+mh], lt=[mcx-mw,mcy];
  const inset=0.22;
  const bases = [
    {pos:[mcx, mcy-mh*(1-inset)], p:0},       // top
    {pos:[mcx+mw*(1-inset), mcy], p:1},        // right
    {pos:[mcx, mcy+mh*(1-inset)], p:2},        // bottom
    {pos:[mcx-mw*(1-inset), mcy], p:3},        // left
  ];
  const pIslands = [
    [mcx+mw*0.28, mcy-mh*0.45], [mcx+mw*0.45, mcy+mh*0.28],
    [mcx-mw*0.28, mcy+mh*0.45], [mcx-mw*0.45, mcy-mh*0.28],
  ];
  const cIslands = [
    [mcx+mw*0.48, mcy-mh*0.22], [mcx+mw*0.22, mcy+mh*0.48],
    [mcx-mw*0.48, mcy+mh*0.22], [mcx-mw*0.22, mcy-mh*0.48],
  ];

  return (
    <div style={{ width:'100%',height:'100%',background:ART.bg,position:'relative' }}>
      <svg viewBox="0 0 880 650" width="100%" height="100%" style={{ fontFamily:"'Nunito',sans-serif" }}>
        <defs>
          <radialGradient id="map-gnd" cx="50%" cy="50%"><stop offset="0%" stopColor="#2a3a1a"/><stop offset="60%" stopColor="#1a2a10"/><stop offset="100%" stopColor="#0e1208"/></radialGradient>
          <radialGradient id="map-glow" cx="50%" cy="50%"><stop offset="0%" stopColor="rgba(245,200,66,0.25)"/><stop offset="100%" stopColor="transparent"/></radialGradient>
        </defs>

        {/* Title */}
        <text x={mcx} y={mcy-mh-18} fill={ART.annot} fontSize="14" fontFamily="'Lilita One',cursive" textAnchor="middle" letterSpacing="3">ARENA LAYOUT — ISOMETRIC VIEW</text>

        {/* Map diamond */}
        <polygon points={`${top[0]},${top[1]} ${rt[0]},${rt[1]} ${bot[0]},${bot[1]} ${lt[0]},${lt[1]}`} fill="url(#map-gnd)" stroke="#4a5c2a" strokeWidth="2"/>

        {/* Iso grid lines */}
        {Array.from({length:7}).map((_,i)=>{
          const t=(i+1)/8;
          return <React.Fragment key={i}>
            <line x1={lt[0]+(top[0]-lt[0])*t} y1={lt[1]+(top[1]-lt[1])*t} x2={bot[0]+(rt[0]-bot[0])*t} y2={bot[1]+(rt[1]-bot[1])*t} stroke="rgba(80,120,40,0.06)" strokeWidth="0.5"/>
            <line x1={top[0]+(rt[0]-top[0])*t} y1={top[1]+(rt[1]-top[1])*t} x2={lt[0]+(bot[0]-lt[0])*t} y2={lt[1]+(bot[1]-lt[1])*t} stroke="rgba(80,120,40,0.06)" strokeWidth="0.5"/>
          </React.Fragment>;
        })}

        {/* Grass patches */}
        {[[-80,-40,50,22],[100,30,38,16],[-30,65,44,18],[65,-75,32,14]].map(([ox,oy,prx,pry],i) =>
          <ellipse key={i} cx={mcx+ox} cy={mcy+oy} rx={prx} ry={pry} fill="#3a5a28" opacity="0.12"/>
        )}

        {/* Dirt paths from bases to center */}
        {bases.map((b,i) => <line key={i} x1={b.pos[0]} y1={b.pos[1]} x2={mcx} y2={mcy} stroke="#6b5540" strokeWidth="10" opacity="0.12" strokeLinecap="round"/>)}

        {/* Center pile glow */}
        <circle cx={mcx} cy={mcy} r={55} fill="url(#map-glow)"/>

        {/* Personal islands */}
        {pIslands.map(([px,py],i) => <g key={`p${i}`}>
          <circle cx={px} cy={py} r={16} fill="#1e2e14" stroke="#4a6a2a" strokeWidth="1" strokeDasharray="3,2"/>
          <ellipse cx={px} cy={py} rx={7} ry={4.5} fill="#D48C20" stroke="#B07018" strokeWidth="0.5"/>
          <text x={px} y={py+26} fill={ART.dim} fontSize="9" textAnchor="middle" fontWeight="600">Personal</text>
          <text x={px} y={py+36} fill={ART.dim} fontSize="7" textAnchor="middle">15 food</text>
        </g>)}

        {/* Contested islands */}
        {cIslands.map(([px,py],i) => <g key={`c${i}`}>
          <circle cx={px} cy={py} r={18} fill="#1a2510" stroke="#8a4a2a" strokeWidth="1.5" strokeDasharray="4,3"/>
          <ellipse cx={px} cy={py} rx={9} ry={5.5} fill="#D4A020" stroke="#B08018" strokeWidth="0.5"/>
          <text x={px} y={py+28} fill="#c45a30" fontSize="9" textAnchor="middle" fontWeight="700">Contested</text>
          <text x={px} y={py+38} fill={ART.dim} fontSize="7" textAnchor="middle">25 food</text>
        </g>)}

        {/* Central pile */}
        <ellipse cx={mcx} cy={mcy} rx={22} ry={15} fill="#F5C842" stroke="#D4A020" strokeWidth="1.5"/>
        <ellipse cx={mcx-5} cy={mcy-4} rx={9} ry={5} fill="rgba(255,255,255,0.15)"/>
        <text x={mcx} y={mcy+30} fill="#F5C842" fontSize="12" fontFamily="'Lilita One',cursive" textAnchor="middle">Central Pile</text>
        <text x={mcx} y={mcy+42} fill={ART.dim} fontSize="9" textAnchor="middle">60 food — highest risk/reward</text>

        {/* Player bases */}
        {bases.map((b,i) => {
          const pc = CW_PLAYER_COLORS[i];
          return <g key={`b${i}`}>
            <rect x={b.pos[0]-18} y={b.pos[1]-13} width={36} height={26} rx={5} fill={`${pc}33`} stroke={pc} strokeWidth="2"/>
            <text x={b.pos[0]} y={b.pos[1]+5} fill={pc} fontSize="13" fontFamily="'Lilita One',cursive" textAnchor="middle">P{i+1}</text>
            <text x={b.pos[0]} y={b.pos[1]+ (i===0?-20:i===2?36:0)} dx={i===1?46:i===3?-46:0} fill={ART.annot} fontSize="9" textAnchor="middle" fontWeight="600">Base</text>
          </g>;
        })}

        {/* Legend */}
        <g transform="translate(24,36)">
          <text x="0" y="0" fill={ART.annot} fontSize="12" fontFamily="'Lilita One',cursive">MAP LEGEND</text>
          {[
            {c:CW_PLAYER_COLORS[0],l:'Player Base (safe zone)',shape:'r'},
            {c:'#F5C842',l:'Central Food Pile (60)',shape:'c'},
            {c:'#D48C20',l:'Personal Island (15 ea.)',shape:'c'},
            {c:'#D4A020',l:'Contested Island (25 ea.)',shape:'c'},
          ].map((it,i) => <g key={i} transform={`translate(0,${18+i*18})`}>
            {it.shape==='r'
              ? <rect x="0" y="-7" width="12" height="10" rx="2" fill={`${it.c}55`} stroke={it.c} strokeWidth="1"/>
              : <circle cx="6" cy="-2" r="5" fill={it.c} opacity="0.7"/>}
            <text x="18" y="2" fill={ART.text} fontSize="10">{it.l}</text>
          </g>)}
        </g>

        {/* Props callout */}
        <g transform="translate(24,550)">
          <text x="0" y="0" fill={ART.annot} fontSize="10" fontWeight="700" letterSpacing="1">DECORATIVE PROPS (low-profile)</text>
          <text x="0" y="16" fill={ART.dim} fontSize="9">Hay bales · Wooden fences · Flower patches · Mud puddles</text>
          <text x="0" y="30" fill={ART.dim} fontSize="9">Must not obscure characters or food piles</text>
        </g>

        {/* Camera note */}
        <g transform={`translate(${mcx+mw+10},${mcy-mh+10})`}>
          <text x="0" y="0" fill={ART.annot} fontSize="10" fontWeight="700" letterSpacing="1">CAMERA</text>
          <text x="0" y="14" fill={ART.dim} fontSize="9">Orthographic iso</text>
          <text x="0" y="26" fill={ART.dim} fontSize="9">45° rot, ~30° elev</text>
          <text x="0" y="38" fill={ART.dim} fontSize="9">Fixed — no zoom/pan</text>
          <text x="0" y="52" fill={ART.dim} fontSize="9">Full map always visible</text>
        </g>
      </svg>
    </div>
  );
};

Object.assign(window, { DetailedChicken, ArtCharacterCard, ArtLineup, ArtFoodPiles, ArtArenaMap });
