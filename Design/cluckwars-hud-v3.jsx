// CluckWars UI v3 — Match HUD, Control States, Ability Button Sheet
// ===================================================================
// 390×844 portrait. v0.3: attack button removed, ability-only combat.

// ─── MATCH HUD v3 ─────────────────────────────────────────
const CWMatchHUDv3 = ({
  abilityIds = ['fly_peck','shell'],         // length 2 or 3
  hpPct = 0.72,
  cargoPct = 0.45,
  cargo = '9/20',
  cooldowns = [0, 0.4, 0],                   // 0..1 per slot
  showStateAnnotations = false,
}) => {
  const t = CW_THEME;
  const abilities = abilityIds.map(id => CW_ABILITY_BY_ID[id]).filter(Boolean);
  const isAssassin = abilities.length === 3;

  // Cargo color shifts: gold → orange → red
  const cargoColor = cargoPct >= 1.0
    ? '#e84040'
    : cargoPct >= 0.7
      ? '#ff7a30'
      : null; // null = use default gradient
  const cargoBg = cargoColor
    ? `linear-gradient(180deg, ${adjustColor(cargoColor,30)}, ${cargoColor})`
    : t.cargo;
  const cargoFull = cargoPct >= 1.0;

  // Leaderboard (P1 is "you", sorted desc by score)
  const players = [
    { p:0, cls:'warrior',  score:42, me:true },
    { p:1, cls:'speedy',   score:17, me:false },
    { p:2, cls:'fatty',    score:28, me:false },
    { p:3, cls:'assassin', score:31, me:false },
  ];
  const ranked = [...players].sort((a,b) => b.score - a.score);
  const targetFood = 150;
  const posLabels = ['1st','2nd','3rd','4th'];

  // Iso grid bg
  const grid = [];
  for (let i = 0; i < 12; i++) {
    grid.push(
      <line key={`a${i}`} x1={i*60-100} y1="0" x2={i*60+200} y2="844" stroke="rgba(80,120,40,0.05)" strokeWidth="1" />,
      <line key={`b${i}`} x1={i*60+200} y1="0" x2={i*60-100} y2="844" stroke="rgba(80,120,40,0.05)" strokeWidth="1" />
    );
  }

  // Slot positions for ability buttons (right-thumb cluster)
  // 2-ability: vertical stack
  // 3-ability: triangle — primary (1) bottom-right, 2 above-left of 1, 3 left of 1
  const btnSize = 76;
  const slotPositions = isAssassin
    ? [
        { bottom: 30,  right: 18 },                 // Slot 1 — primary thumb
        { bottom: 102, right: 78 },                 // Slot 2 — upper-left
        { bottom: 38,  right: 110 },                // Slot 3 — far-left
      ]
    : [
        { bottom: 30,  right: 18 },
        { bottom: 110, right: 32 },
      ];

  const AbilityBtn = ({ ab, cd, pos, slotNum }) => (
    <div style={{ position:'absolute', ...pos }}>
      <div style={{
        position:'relative',
        filter: cd > 0 ? 'grayscale(0.4)' : `drop-shadow(0 0 6px ${ab.color}55)`,
        opacity: cd > 0 ? 0.85 : 1,
      }}>
        <CWHex size={btnSize} color={ab.color} icon={ab.icon} cooldownPct={cd} />
        {/* Cooldown number */}
        {cd > 0 && (
          <div style={{
            position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:24, color:'#fef5e0', fontFamily:"'Lilita One',cursive",
            textShadow:'0 2px 4px rgba(0,0,0,0.8)', pointerEvents:'none',
          }}>{Math.ceil(ab.cdSec * cd)}</div>
        )}
        {/* Slot index badge */}
        <div style={{
          position:'absolute', top:-2, left:-2,
          width:18, height:18, borderRadius:'50%',
          background: slotNum === 3
            ? `linear-gradient(180deg, ${t.t3}, ${adjustColor(t.t3,-30)})`
            : 'linear-gradient(180deg, #4a3018, #2a1a0c)',
          border: `1.5px solid ${slotNum === 3 ? '#fef5e0' : t.panelBorder}`,
          fontSize:10, fontFamily:"'Lilita One',cursive",
          color: slotNum === 3 ? '#1a0e04' : '#fef5e0',
          display:'flex', alignItems:'center', justifyContent:'center',
          textShadow: slotNum === 3 ? 'none' : '0 1px 2px rgba(0,0,0,0.6)',
          boxShadow:'0 2px 4px rgba(0,0,0,0.4)',
        }}>{slotNum === 3 ? '★' : slotNum}</div>
      </div>
    </div>
  );

  return (
    <div style={{
      width:'100%', height:'100%', background:t.mapBg,
      fontFamily:"'Lilita One',cursive", position:'relative', overflow:'hidden',
    }}>
      <svg style={{ position:'absolute', inset:0, width:'100%', height:'100%' }}>{grid}</svg>

      {/* Decorative food piles */}
      <div style={{ position:'absolute', left:'50%', top:'35%', transform:'translate(-50%,-50%)' }}>
        <div style={{ width:54, height:38, borderRadius:'50%',
          background:'radial-gradient(circle at 40% 35%, #ffe066, #f5c842, #d4a020)',
          boxShadow:'0 0 30px rgba(245,200,66,0.3), 0 4px 8px rgba(0,0,0,0.3)' }} />
      </div>
      {[{x:'18%',y:'46%',s:22},{x:'78%',y:'38%',s:24},{x:'25%',y:'22%',s:18},{x:'72%',y:'58%',s:20}].map((p,i) => (
        <div key={i} style={{ position:'absolute', left:p.x, top:p.y,
          width:p.s, height:p.s*0.65, borderRadius:'50%',
          background:'radial-gradient(circle, #e8b830, #b08018)',
          boxShadow:'0 0 10px rgba(200,160,40,0.15)' }} />
      ))}

      {/* Player chicken (center-ish, above HUD bottom) */}
      <div style={{ position:'absolute', left:'50%', top:'52%', transform:'translate(-50%,-50%)',
        display:'flex', flexDirection:'column', alignItems:'center' }}>
        <CWChicken classKey={isAssassin ? 'assassin' : 'warrior'} size={88}
          showRing ringColor={CW_PLAYERS_V3[0].color} />
        {/* HP bar */}
        <div style={{ width:68, height:8, background:'#0a0604', borderRadius:4, marginTop:-6,
          overflow:'hidden', border:'1.5px solid #4a2818',
          boxShadow:'inset 0 1px 2px rgba(0,0,0,0.6), 0 1px 2px rgba(0,0,0,0.3)' }}>
          <div style={{ width:`${hpPct*100}%`, height:'100%', background:t.hp, borderRadius:3,
            boxShadow:'inset 0 1px 0 rgba(255,200,200,0.3)' }} />
        </div>
        {/* Cargo bar */}
        <div style={{ width:68, height:6, background:'#0a0604', borderRadius:3, marginTop:2,
          overflow:'hidden', border:'1px solid #4a3018',
          boxShadow: cargoFull
            ? `inset 0 1px 2px rgba(0,0,0,0.6), 0 0 8px ${cargoColor}88`
            : 'inset 0 1px 2px rgba(0,0,0,0.6)' }}>
          <div style={{ width:`${cargoPct*100}%`, height:'100%', background:cargoBg, borderRadius:2,
            boxShadow:'inset 0 1px 0 rgba(255,240,180,0.3)' }} />
        </div>
        <div style={{ display:'flex', alignItems:'center', gap:3, marginTop:3 }}>
          <CWFoodIcon size={11} />
          <span style={{ fontSize:11, color:'#fef5e0', fontFamily:"'Nunito',sans-serif",
            fontWeight:800, textShadow:'0 1px 3px rgba(0,0,0,0.8)' }}>{cargo}</span>
        </div>
        {/* FULL banner */}
        {cargoFull && (
          <div style={{
            marginTop:4, padding:'3px 10px', borderRadius:6,
            background:`linear-gradient(180deg, ${adjustColor(cargoColor,30)}, ${cargoColor})`,
            border:`1.5px solid ${adjustColor(cargoColor,-30)}`,
            boxShadow:`0 0 10px ${cargoColor}88, inset 0 1px 0 rgba(255,255,255,0.3)`,
            fontSize:10, color:'#fff', fontFamily:"'Lilita One',cursive",
            textShadow:'0 1px 2px rgba(0,0,0,0.6)', letterSpacing:1, whiteSpace:'nowrap',
          }}>FULL — RETURN TO BASE!</div>
        )}
      </div>

      {/* Enemy chickens */}
      {[{x:'24%',y:'30%',cls:'speedy',p:1,hp:0.5},
        {x:'74%',y:'26%',cls:'fatty',p:2,hp:0.8},
        {x:'68%',y:'66%',cls:'assassin',p:3,hp:0.35}].map((e,i) => (
        <div key={i} style={{ position:'absolute', left:e.x, top:e.y,
          display:'flex', flexDirection:'column', alignItems:'center',
          transform:'scale(0.7)', opacity:0.9 }}>
          <CWChicken classKey={e.cls} size={56} showRing ringColor={CW_PLAYERS_V3[e.p].color} />
          <div style={{ width:44, height:5, background:'#0a0604', borderRadius:3, marginTop:-4,
            overflow:'hidden', border:'1px solid #4a2818' }}>
            <div style={{ width:`${e.hp*100}%`, height:'100%', background:t.hp, borderRadius:2 }} />
          </div>
        </div>
      ))}

      {/* ═══ TOP: leaderboard + timer ═══ */}
      <div style={{
        position:'absolute', top:8, left:8, zIndex:10,
        width:200,
        background:'linear-gradient(180deg, rgba(30,20,10,0.88), rgba(20,14,8,0.78))',
        border:`2px solid ${t.panelBorder}88`,
        borderRadius:10,
        boxShadow:'inset 0 1px 0 rgba(255,200,100,0.12), 0 3px 10px rgba(0,0,0,0.5)',
        overflow:'hidden',
      }}>
        {ranked.map((r, i) => {
          const pc = CW_PLAYERS_V3[r.p].color;
          const pct = Math.min(100, (r.score / targetFood) * 100);
          return (
            <div key={r.p} style={{
              display:'flex', alignItems:'center', gap:5,
              height:26, padding:'0 8px',
              background: r.me
                ? `linear-gradient(90deg, ${pc}30, ${pc}08)`
                : i === 0 ? 'rgba(245,200,66,0.07)' : 'transparent',
              borderBottom: i < 3 ? '1px solid rgba(138,106,58,0.22)' : 'none',
              borderLeft: r.me ? `3px solid ${pc}` : '3px solid transparent',
            }}>
              <span style={{ fontSize:9, color: i===0 ? t.t3 : t.t2, fontWeight:700,
                width:18, textAlign:'center', textShadow: i===0?`0 0 6px ${t.t3}66`:t.tShadow,
                fontFamily:"'Nunito',sans-serif" }}>{posLabels[i]}</span>
              <div style={{ width:8, height:8, borderRadius:'50%', flexShrink:0,
                background:`radial-gradient(circle at 35% 35%, ${adjustColor(pc,50)}, ${pc})`,
                boxShadow:`0 0 4px ${pc}88` }} />
              <span style={{ fontSize:10, color: r.me ? '#fef5e0' : pc, fontWeight: r.me?800:600,
                width:18, fontFamily:"'Nunito',sans-serif",
                textShadow: r.me ? `0 0 6px ${pc}66` : 'none' }}>P{r.p+1}</span>
              <div style={{ flex:1, display:'flex', alignItems:'center', gap:4 }}>
                <div style={{ flex:1, height:6, background:'#0a0604', borderRadius:3,
                  overflow:'hidden', border:'1px solid #3a2816' }}>
                  <div style={{ width:`${pct}%`, height:'100%',
                    background:`linear-gradient(90deg, ${adjustColor(pc,20)}, ${pc})`,
                    boxShadow:`inset 0 1px 0 rgba(255,255,255,0.2)` }} />
                </div>
                <span style={{ fontSize:10, color:t.t1, width:22, textAlign:'right',
                  fontFamily:"'Nunito',sans-serif", fontWeight:700, textShadow:t.tShadow }}>{r.score}</span>
              </div>
            </div>
          );
        })}
      </div>

      {/* Target badge under leaderboard */}
      <div style={{
        position:'absolute', top:118, left:8, zIndex:10,
        padding:'2px 8px', borderRadius:6,
        background:'linear-gradient(180deg, rgba(245,200,66,0.18), rgba(245,200,66,0.06))',
        border:'1px solid rgba(245,200,66,0.4)',
        fontSize:9, color:t.t3, fontFamily:"'Nunito',sans-serif", fontWeight:800,
        textShadow:t.tShadow, letterSpacing:0.5,
        display:'flex', alignItems:'center', gap:4,
      }}>
        <CWFoodIcon size={10} />
        <span>FIRST TO 150</span>
      </div>

      {/* ═══ TOP-RIGHT: timer ═══ */}
      <div style={{ position:'absolute', top:8, right:8, zIndex:10 }}>
        <CWTimer time="02:34" />
      </div>

      {/* ═══ BOTTOM: joystick + ability buttons ═══ */}

      {/* Joystick */}
      <div style={{ position:'absolute', bottom:24, left:18 }}>
        <div style={{ width:120, height:120, borderRadius:'50%',
          background:'radial-gradient(circle, rgba(255,255,255,0.06), rgba(255,255,255,0.02))',
          border:'2.5px solid rgba(255,255,255,0.12)',
          boxShadow:'inset 0 0 10px rgba(0,0,0,0.3), 0 2px 8px rgba(0,0,0,0.3)',
          display:'flex', alignItems:'center', justifyContent:'center' }}>
          <div style={{ width:50, height:50, borderRadius:'50%',
            background:'radial-gradient(circle at 40% 35%, rgba(255,255,255,0.25), rgba(255,255,255,0.08))',
            border:'2px solid rgba(255,255,255,0.2)',
            boxShadow:'inset 0 1px 0 rgba(255,255,255,0.3), 0 2px 4px rgba(0,0,0,0.3)',
            transform:'translate(6px,-3px)' }} />
        </div>
      </div>

      {/* Ability cluster (right) */}
      <div style={{ position:'absolute', bottom:0, right:0, width:200, height:220 }}>
        {abilities.map((ab, i) => (
          <AbilityBtn key={i} ab={ab} cd={cooldowns[i] || 0}
            pos={slotPositions[i]} slotNum={i+1} />
        ))}
      </div>

      {/* Annotations (optional) */}
      {showStateAnnotations && (
        <>
          <Annotation x={196} y={620} label="Joystick — MOVE" arrow="left" />
          <Annotation x={196} y={680}
            label={isAssassin ? "3-button TRIANGLE — primary at thumb base, combo finisher (★3) furthest" : "2 ability buttons — vertical stack in thumb arc"}
            arrow="right" />
          {cargoFull && <Annotation x={196} y={500} label="Cargo FULL → return to base" arrow="up" />}
        </>
      )}
    </div>
  );
};

// Tiny annotation pip used in the wireframe variants
const Annotation = ({ x, y, label, arrow }) => (
  <div style={{
    position:'absolute', left:x, top:y, zIndex:20,
    background:'rgba(254,245,224,0.94)', color:'#1a0e04',
    padding:'3px 8px', borderRadius:6,
    fontSize:9, fontFamily:"'Nunito',sans-serif", fontWeight:700,
    boxShadow:'0 2px 6px rgba(0,0,0,0.5)',
    maxWidth: 220, lineHeight:1.25, letterSpacing:0.2,
    transform:'translate(-50%,-50%)',
    pointerEvents:'none',
  }}>
    {label}
  </div>
);

// ─── CONTROL STATE — character overlay component ─────────
const CWStateChicken = ({ state, classKey = 'warrior', size = 100, playerIdx = 0 }) => {
  const c = CW_PLAYERS_V3[playerIdx].color;
  const isStunned = state === 'stunned';
  const isSlowed = state === 'slowed';
  const isRooted = state === 'rooted';
  const isKnocked = state === 'knockback';

  return (
    <div style={{ position:'relative', width: size+40, height: size+50,
      display:'flex', alignItems:'center', justifyContent:'center' }}>

      {/* Slowed: blue tint zone */}
      {isSlowed && (
        <div style={{
          position:'absolute', inset:'10% 5%',
          background:'radial-gradient(circle, rgba(80,180,240,0.28), transparent 70%)',
          borderRadius:'50%', pointerEvents:'none', zIndex:0,
        }} />
      )}

      {/* Slowed: speed trail behind chicken */}
      {isSlowed && (
        <svg style={{ position:'absolute', inset:0, pointerEvents:'none', zIndex:0 }} viewBox="0 0 140 150">
          {[0,1,2].map(i => (
            <path key={i} d={`M ${20-i*4} ${100-i*4} Q ${40-i*4} ${110-i*4} ${60-i*4} ${100-i*4}`}
              stroke="rgba(80,180,240,0.5)" strokeWidth={2-i*0.4} fill="none" strokeLinecap="round" />
          ))}
        </svg>
      )}

      {/* Knockback: motion arrow + ghost trail */}
      {isKnocked && (
        <svg style={{ position:'absolute', inset:0, pointerEvents:'none', zIndex:0 }} viewBox="0 0 140 150">
          {/* Ghost trail */}
          <g opacity="0.35" transform="translate(-22, 0)">
            <ellipse cx="70" cy="100" rx="22" ry="20" fill="rgba(254,245,224,0.4)" />
          </g>
          <g opacity="0.55" transform="translate(-10, 0)">
            <ellipse cx="70" cy="100" rx="22" ry="20" fill="rgba(254,245,224,0.5)" />
          </g>
          {/* Motion lines */}
          {[0,1,2].map(i => (
            <line key={i} x1={12} y1={75+i*8} x2={42} y2={75+i*8}
              stroke="#fef5e0" strokeWidth="2.5" strokeLinecap="round" opacity={0.7 - i*0.15} />
          ))}
        </svg>
      )}

      {/* Chicken */}
      <div style={{
        position:'relative', zIndex:1,
        filter: isStunned ? 'saturate(0.35) brightness(0.7)' : isSlowed ? 'saturate(0.7) hue-rotate(-10deg)' : 'none',
        opacity: isStunned ? 0.85 : 1,
        transform: isStunned ? 'rotate(-12deg)' : 'rotate(0)',
        transition: 'all .2s',
      }}>
        <CWChicken classKey={classKey} size={size} showRing ringColor={c} />
      </div>

      {/* Stunned: stars circling overhead */}
      {isStunned && (
        <div style={{ position:'absolute', top:-2, left:'50%', transform:'translateX(-50%)',
          width:90, height:38, zIndex:2, pointerEvents:'none' }}>
          {['⭐','✨','⭐','✨'].map((s,i) => {
            const angle = i * 90;
            const r = 32;
            const x = 45 + Math.cos(angle * Math.PI/180) * r;
            const y = 18 + Math.sin(angle * Math.PI/180) * 10;
            return (
              <span key={i} style={{
                position:'absolute', left:x, top:y, fontSize: i%2===0 ? 16 : 12,
                transform:'translate(-50%,-50%)',
                filter:'drop-shadow(0 1px 2px rgba(0,0,0,0.5))',
              }}>{s}</span>
            );
          })}
        </div>
      )}

      {/* Rooted: vines/roots at feet */}
      {isRooted && (
        <svg style={{ position:'absolute', bottom:0, left:'50%', transform:'translateX(-50%)',
          width:size+20, height:42, pointerEvents:'none', zIndex:2 }} viewBox="0 0 120 50">
          {/* Vines wrapping */}
          {[
            'M 30 10 Q 22 22 30 36 Q 38 42 32 50',
            'M 90 10 Q 98 22 90 36 Q 82 42 88 50',
            'M 60 6 Q 56 22 62 38 Q 60 46 60 50',
            'M 45 14 Q 40 28 50 42 Q 48 48 46 50',
            'M 75 14 Q 80 28 70 42 Q 72 48 74 50',
          ].map((d,i) => (
            <path key={i} d={d} stroke="#5d3b18" strokeWidth="4" fill="none" strokeLinecap="round" />
          ))}
          {[
            'M 30 10 Q 22 22 30 36 Q 38 42 32 50',
            'M 90 10 Q 98 22 90 36 Q 82 42 88 50',
          ].map((d,i) => (
            <path key={`hi${i}`} d={d} stroke="#8d6e3b" strokeWidth="1.5" fill="none" strokeLinecap="round" opacity="0.6" />
          ))}
          {/* Leaves */}
          {[[28,18],[92,18],[62,12],[42,26],[78,26]].map(([cx,cy],i) => (
            <ellipse key={i} cx={cx} cy={cy} rx="4" ry="2.5"
              fill="#3a7a2e" transform={`rotate(${i*20-40} ${cx} ${cy})`} />
          ))}
        </svg>
      )}

      {/* Nameplate badge */}
      <div style={{
        position:'absolute', bottom:-2, left:'50%', transform:'translateX(-50%)',
        display:'flex', alignItems:'center', gap:4,
        padding:'2px 8px', borderRadius:10,
        background: isStunned
          ? 'linear-gradient(180deg, #6a2020, #3a0e0e)'
          : `linear-gradient(180deg, ${c}cc, ${adjustColor(c,-30)}cc)`,
        border: `1.5px solid ${isStunned ? '#c83030' : adjustColor(c,30)}`,
        boxShadow: '0 2px 4px rgba(0,0,0,0.5), inset 0 1px 0 rgba(255,255,255,0.2)',
        fontFamily:"'Nunito',sans-serif", fontWeight:800, fontSize:9, color:'#fef5e0',
        textShadow:'0 1px 2px rgba(0,0,0,0.6)', letterSpacing:0.5, zIndex:3,
      }}>
        {isStunned && <span style={{ fontSize:11 }}>💀</span>}
        {isSlowed && <span style={{ fontSize:10 }}>🐌</span>}
        {isRooted && <span style={{ fontSize:10 }}>🌱</span>}
        {isKnocked && <span style={{ fontSize:10 }}>💨</span>}
        <span>P{playerIdx+1}</span>
      </div>
    </div>
  );
};

// ─── CONTROL STATES REFERENCE SHEET ──────────────────────
const CWControlStatesSheet = () => {
  const t = CW_THEME;
  const states = [
    { key:'stunned',   label:'STUNNED',      trigger:'HP hits 0',                   duration:'5 seconds',  effect:'Full incapacitation. Drops all cargo on the ground at feet.',
      visual:'Cartoon stars circle overhead, body tilted/desaturated, red skull on nameplate.' },
    { key:'slowed',    label:'SLOWED',       trigger:'Collision / pile / slow ability', duration:'1–4s (ability-dependent)',
      effect:'Movement speed reduced. Speedy class resists via Slippery passive.',
      visual:'Subtle blue tint overlay + fading speed-trail behind chicken. Snail icon on nameplate.' },
    { key:'knockback', label:'KNOCKED BACK', trigger:'Push abilities (Roll & Push, Spine Coat, Peck)', duration:'Instant',
      effect:'Involuntary displacement. Fatty class resists via Immovable passive.',
      visual:'Motion lines + ghost trail behind chicken in direction of travel. No persistent overlay.' },
    { key:'rooted',    label:'ROOTED',       trigger:'Root Egg trap', duration:'2–3s',
      effect:'Cannot move, BUT can still cast abilities & attack.',
      visual:'Vines/roots wrap legs. Green leaf icon on nameplate.' },
  ];

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", padding:'14px 16px', overflow:'auto' }}>

      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:12 }}>
        <CWRibbon width={280}>CONTROL STATE OVERLAYS</CWRibbon>
        <span style={{ fontSize:10, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>v0.3 — new in this spec</span>
      </div>

      <div style={{ display:'grid', gridTemplateColumns:'1fr 1fr', gap:14 }}>
        {states.map(s => (
          <div key={s.key} style={{
            background:t.cardBg, border:`2px solid ${t.cardBorder}`,
            borderRadius:t.cardR, boxShadow:t.cardGlow,
            padding:'12px 14px', display:'flex', flexDirection:'column', gap:8,
          }}>
            <div style={{ display:'flex', alignItems:'center', gap:8 }}>
              <span style={{ fontSize:16, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>{s.label}</span>
              <div style={{ flex:1, height:1,
                background:`linear-gradient(90deg, ${t.t3}66, transparent)` }} />
            </div>

            {/* Visual */}
            <div style={{
              height: 160, background: t.mapBg, borderRadius:8,
              border:'1px solid rgba(138,106,58,0.4)',
              display:'flex', alignItems:'center', justifyContent:'center',
              boxShadow:'inset 0 1px 4px rgba(0,0,0,0.5)',
              position:'relative', overflow:'hidden',
            }}>
              {/* faint grid */}
              <svg style={{ position:'absolute', inset:0, width:'100%', height:'100%', opacity:0.35 }} viewBox="0 0 200 160">
                {[0,1,2,3,4,5].map(i => <line key={i} x1={i*40} y1="0" x2={i*40+50} y2="160" stroke="rgba(80,120,40,0.2)" strokeWidth="1" />)}
              </svg>
              <CWStateChicken state={s.key} classKey="warrior" size={96} playerIdx={0} />
            </div>

            {/* Specs */}
            <div style={{ display:'grid', gridTemplateColumns:'auto 1fr', gap:'3px 8px',
              fontFamily:"'Nunito',sans-serif", fontSize:10 }}>
              <span style={{ color:t.t3, fontWeight:800 }}>Trigger</span>
              <span style={{ color:t.t1 }}>{s.trigger}</span>
              <span style={{ color:t.t3, fontWeight:800 }}>Duration</span>
              <span style={{ color:t.t1 }}>{s.duration}</span>
              <span style={{ color:t.t3, fontWeight:800 }}>Effect</span>
              <span style={{ color:t.t1, lineHeight:1.3 }}>{s.effect}</span>
              <span style={{ color:t.t3, fontWeight:800 }}>Visual</span>
              <span style={{ color:t.t2, lineHeight:1.3, fontStyle:'italic' }}>{s.visual}</span>
            </div>
          </div>
        ))}
      </div>

      <div style={{
        marginTop:14, padding:'10px 14px',
        background:'rgba(245,200,66,0.08)', border:'1px solid rgba(245,200,66,0.3)',
        borderRadius:8,
        fontFamily:"'Nunito',sans-serif", fontSize:10, color:t.t2, lineHeight:1.4,
      }}>
        <span style={{ color:t.t3, fontWeight:800 }}>Design note · </span>
        Overlays attach to the affected chicken in world space — not to a HUD panel. Player identity ring colour stays
        visible underneath so "who is what" reads instantly even mid-stun. Stunned cargo drop is part of the gameplay
        consequence (see GDD §7.1), not the visual state — show it once in the prototype via crumbled food sprites at
        the chicken's feet during the stun.
      </div>
    </div>
  );
};

// ─── ABILITY BUTTON COMPONENT SHEET ──────────────────────
const CWAbilityButtonSheet = () => {
  const t = CW_THEME;
  const sample = CW_ABILITY_BY_ID['fly_peck'];
  const cellW = 168;

  const Cell = ({ label, sub, children, accent }) => (
    <div style={{
      width: cellW, background:t.cardBg, border:`2px solid ${t.cardBorder}`,
      borderRadius:t.cardR, boxShadow:t.cardGlow,
      padding:'14px 12px 12px', display:'flex', flexDirection:'column', alignItems:'center', gap:10,
    }}>
      <div style={{ display:'flex', alignItems:'center', gap:6, alignSelf:'stretch' }}>
        <div style={{ width:8, height:8, borderRadius:2, background: accent || t.t3,
          boxShadow:`0 0 6px ${accent || t.t3}aa` }} />
        <span style={{ fontSize:11, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>{label}</span>
      </div>
      <div style={{
        flex:1, width:'100%', minHeight:130, background: t.mapBg, borderRadius:8,
        border:'1px solid rgba(138,106,58,0.4)',
        display:'flex', alignItems:'center', justifyContent:'center',
        boxShadow:'inset 0 1px 4px rgba(0,0,0,0.5)', position:'relative',
      }}>
        {children}
      </div>
      <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600,
        textAlign:'center', lineHeight:1.3, letterSpacing:0.2 }}>{sub}</span>
    </div>
  );

  // Active state: pulse glow + name visible
  const PulseGlow = ({ color }) => (
    <div style={{
      position:'absolute', inset:'18% 12%', borderRadius:'50%',
      background:`radial-gradient(circle, ${color}66, transparent 70%)`,
      animation: 'cwPulse 1.4s ease-in-out infinite',
    }} />
  );

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", padding:'14px 16px', overflow:'auto' }}>
      <style>{`@keyframes cwPulse { 0%,100%{opacity:0.5} 50%{opacity:1} }`}</style>

      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:12 }}>
        <CWRibbon width={260}>ABILITY BUTTON · STATES</CWRibbon>
        <span style={{ fontSize:10, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>Sample: Flying Peck</span>
      </div>

      <div style={{ display:'flex', gap:12, flexWrap:'wrap', justifyContent:'center' }}>

        {/* READY */}
        <Cell label="READY" accent="#7cd99a"
          sub="Full color hex, gloss highlight, no overlay. Tap to fire.">
          <CWHex size={78} color={sample.color} icon={sample.icon} />
        </Cell>

        {/* ACTIVE */}
        <Cell label="ACTIVE" accent="#5ec5ff"
          sub="Pulsing glow ring, ability name visible above for 0.3s on press.">
          <PulseGlow color={sample.color} />
          <div style={{ position:'absolute', top:8, left:'50%', transform:'translateX(-50%)',
            padding:'2px 7px', borderRadius:5,
            background:`linear-gradient(180deg, ${adjustColor(sample.color,30)}, ${sample.color})`,
            border:`1px solid ${adjustColor(sample.color,-20)}`, whiteSpace:'nowrap',
            fontSize:9, color:'#fff', fontFamily:"'Lilita One',cursive",
            textShadow:'0 1px 2px rgba(0,0,0,0.6)', letterSpacing:0.5,
            boxShadow:`0 0 8px ${sample.color}aa`,
          }}>{sample.name}</div>
          <div style={{ filter:`drop-shadow(0 0 8px ${sample.color}cc)` }}>
            <CWHex size={78} color={sample.color} icon={sample.icon} />
          </div>
        </Cell>

        {/* COOLDOWN */}
        <Cell label="COOLDOWN" accent="#f5c842"
          sub="Radial fill drains clockwise. ~45% alpha. Seconds remaining at center.">
          <div style={{ position:'relative', filter:'grayscale(0.5)', opacity:0.55 }}>
            <CWHex size={78} color={sample.color} icon={sample.icon} cooldownPct={0.65} />
          </div>
          <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:30, color:'#fef5e0', fontFamily:"'Lilita One',cursive",
            textShadow:'0 2px 4px rgba(0,0,0,0.85)', pointerEvents:'none' }}>3</div>
        </Cell>

        {/* LOCKED */}
        <Cell label="LOCKED" accent="#7a6a48"
          sub="Slot 3 hidden for non-Assassin. Dimmed hex + lock icon. Tap shows tooltip.">
          <div style={{ position:'relative', opacity:0.35 }}>
            <svg viewBox="0 0 100 115.5" width="78" height="90">
              <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                fill="rgba(20,12,6,0.8)" stroke={t.panelBorder} strokeWidth="2" />
              <polygon points="50,8 92,31 92,84 50,107 8,84 8,31"
                fill="rgba(40,28,16,0.6)" />
            </svg>
          </div>
          <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:28, filter:'drop-shadow(0 1px 3px rgba(0,0,0,0.6))' }}>🔒</div>
        </Cell>

      </div>

      {/* Hex anatomy strip */}
      <div style={{
        marginTop:14, padding:'10px 14px', background:t.cardBg,
        border:`2px solid ${t.cardBorder}`, borderRadius:t.cardR, boxShadow:t.cardGlow,
      }}>
        <div style={{ fontSize:11, color:t.t3, textShadow:t.tShadow, letterSpacing:1, marginBottom:8 }}>
          HEX ANATOMY
        </div>
        <div style={{ display:'flex', gap:14, alignItems:'center' }}>
          <CWHex size={72} color={sample.color} icon={sample.icon} />
          <div style={{ flex:1, display:'grid', gridTemplateColumns:'1fr 1fr', gap:'3px 12px',
            fontFamily:"'Nunito',sans-serif", fontSize:10 }}>
            {[
              ['1. Drop shadow', 'Dark hex offset, 60% α'],
              ['2. Border hex', '2px stroke, dark shade of accent'],
              ['3. Main fill', '3-stop vertical gradient'],
              ['4. Gloss overlay', 'White→dark gradient over fill'],
              ['5. Inner highlight', 'White 15% α inner rim'],
              ['6. Cooldown', 'Black 60% α, clipped bottom→up'],
            ].map(([k,v],i) => (
              <React.Fragment key={i}>
                <span style={{ color:t.t3, fontWeight:800 }}>{k}</span>
                <span style={{ color:t.t1 }}>{v}</span>
              </React.Fragment>
            ))}
          </div>
        </div>
      </div>

      {/* Sizing note */}
      <div style={{ marginTop:10, padding:'8px 14px',
        background:'rgba(245,200,66,0.08)', border:'1px solid rgba(245,200,66,0.3)',
        borderRadius:8, fontFamily:"'Nunito',sans-serif", fontSize:10, color:t.t2, lineHeight:1.4 }}>
        <span style={{ color:t.t3, fontWeight:800 }}>Sizing · </span>
        76px diameter in-match. Cooldown countdown number: Lilita One 24px (≥44px touch target via padded hit box).
        Slot index badge: top-left, 18×18. Assassin's Slot 3 badge uses gold ★ to call out the class privilege.
      </div>
    </div>
  );
};

Object.assign(window, {
  CWMatchHUDv3, CWStateChicken, CWControlStatesSheet, CWAbilityButtonSheet,
});
