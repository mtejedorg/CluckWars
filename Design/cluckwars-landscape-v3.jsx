// CluckWars UI v3 — LANDSCAPE variants (844×390, iPhone 14 landscape)
// =====================================================================
// Mirrors the portrait flow exactly — same data, same atoms — but
// re-laid out for short/wide aspect ratio.

// ─── CHARACTER SELECT · LANDSCAPE ─────────────────────────
const CWCharacterSelectV3L = ({ initialClass = 'warrior', initialSlots = null }) => {
  const t = CW_THEME;
  const [klass, setKlass] = React.useState(initialClass);
  const cls = CW_CLASSES_V3[klass];

  const [slots, setSlots] = React.useState(() => {
    const base = Array(cls.slots).fill(null);
    if (initialSlots) for (let i = 0; i < Math.min(initialSlots.length, cls.slots); i++) base[i] = initialSlots[i];
    return base;
  });
  const [activeSlot, setAS] = React.useState(() => {
    if (initialSlots) {
      const arr = Array(cls.slots).fill(null).map((_,i) => initialSlots[i] || null);
      const idx = arr.findIndex(s => !s);
      return idx === -1 ? cls.slots - 1 : idx;
    }
    return 0;
  });

  React.useEffect(() => {
    setSlots(prev => {
      const next = Array(cls.slots).fill(null);
      for (let i = 0; i < Math.min(prev.length, cls.slots); i++) next[i] = prev[i];
      return next;
    });
    setAS(0);
  }, [klass]); // eslint-disable-line

  const equipped = slots.filter(Boolean);
  const allFilled = slots.every(Boolean);

  const onPickAbility = (abId) => {
    const existingIdx = slots.indexOf(abId);
    if (existingIdx !== -1 && existingIdx !== activeSlot) {
      const next = [...slots];
      [next[existingIdx], next[activeSlot]] = [next[activeSlot], next[existingIdx]];
      setSlots(next);
      const nextEmpty = next.findIndex(s => !s);
      if (nextEmpty !== -1) setAS(nextEmpty);
      return;
    }
    if (existingIdx === activeSlot) {
      const next = [...slots]; next[activeSlot] = null; setSlots(next);
      return;
    }
    const next = [...slots]; next[activeSlot] = abId; setSlots(next);
    const nextEmpty = next.findIndex(s => !s);
    setAS(nextEmpty !== -1 ? nextEmpty : activeSlot);
  };

  // ─── Sub-components ─────────────────────────────────────
  const ClassChip = ({ k }) => {
    const c = CW_CLASSES_V3[k];
    const active = k === klass;
    return (
      <div onClick={() => setKlass(k)} style={{
        padding:'5px 8px 6px',
        background: active ? `linear-gradient(180deg, ${c.color}33, ${c.dark}22)` : t.cardBg,
        border: `2px solid ${active ? c.color : t.cardBorder}`,
        borderRadius: 10, cursor:'pointer',
        boxShadow: active ? t.cardSelGlow : t.cardGlow,
        display:'flex', alignItems:'center', gap:8,
        transform: active ? 'translateX(2px)' : 'translateX(0)',
        transition:'all .15s ease', position:'relative',
      }}>
        <CWChicken classKey={k} size={36} />
        <div style={{ flex:1, minWidth:0 }}>
          <div style={{ fontSize:12, color: active?t.t3:t.t1, textShadow:t.tShadow, lineHeight:1 }}>{c.short}</div>
          <div style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, marginTop:2 }}>{c.role}</div>
        </div>
        <div style={{
          fontSize:7, fontFamily:"'Nunito',sans-serif", fontWeight:800,
          padding:'1px 5px', borderRadius:5,
          background: c.slots === 3 ? `${t.t3}cc` : 'rgba(0,0,0,0.4)',
          color: c.slots === 3 ? '#1a0e04' : t.t2,
          border: c.slots === 3 ? `1px solid ${t.t3}` : '1px solid rgba(255,255,255,0.1)',
          letterSpacing:0.5,
        }}>{c.slots === 3 ? '★3' : 'x2'}</div>
      </div>
    );
  };

  const SlotHex = ({ idx, size = 50 }) => {
    const isAssassinSlot = cls.slots === 3 && idx === 2;
    const isActive = idx === activeSlot;
    const abId = slots[idx];
    const ab = abId ? CW_ABILITY_BY_ID[abId] : null;
    return (
      <div onClick={() => setAS(idx)} style={{ position:'relative', cursor:'pointer' }}>
        {ab ? (
          <div style={{
            position:'relative',
            filter: isActive ? `drop-shadow(0 0 6px ${ab.color}cc)` : 'none',
            transform: isActive ? 'scale(1.06)' : 'scale(1)',
            transition:'all .15s',
          }}>
            <CWHex size={size} color={ab.color} icon={ab.icon} />
          </div>
        ) : (
          <div style={{ width:size, height:size*1.155, position:'relative',
            transform: isActive ? 'scale(1.06)' : 'scale(1)', transition:'transform .15s' }}>
            <svg viewBox="0 0 100 115.5" width="100%" height="100%">
              <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                fill="rgba(20,12,6,0.6)" stroke={isActive ? t.t3 : t.panelBorder}
                strokeWidth={isActive ? 3 : 2}
                strokeDasharray={isActive ? '0' : '5 3'} />
              <polygon points="50,8 92,31 92,84 50,107 8,84 8,31"
                fill={isActive ? `${t.t3}11` : 'rgba(0,0,0,0.2)'} />
            </svg>
            <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
              fontFamily:"'Lilita One',cursive", fontSize: 22, color: isActive ? t.t3 : t.panelBorder,
              textShadow: isActive ? `0 0 6px ${t.t3}88` : 'none', pointerEvents:'none' }}>+</div>
          </div>
        )}
        <div style={{
          position:'absolute', top:-9, left:'50%', transform:'translateX(-50%)',
          fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
          color: isActive ? t.t3 : t.t2,
          textShadow: isActive ? `0 0 6px ${t.t3}88` : t.tShadow,
          letterSpacing:1, whiteSpace:'nowrap',
        }}>{isAssassinSlot ? '★ S3' : `S${idx+1}`}</div>
      </div>
    );
  };

  const AbilityCard = ({ ab }) => {
    const slotIdx = slots.indexOf(ab.id);
    const equipped = slotIdx !== -1;
    return (
      <div onClick={() => onPickAbility(ab.id)} style={{
        flex:'0 0 calc(25% - 4px)', padding:'4px 2px 4px',
        background: equipped ? `linear-gradient(180deg, ${ab.color}33, ${ab.color}11)` : t.cardBg,
        border: `1.5px solid ${equipped ? ab.color : t.cardBorder}`,
        borderRadius: 8, cursor:'pointer',
        boxShadow: equipped ? `0 0 6px ${ab.color}66, ${t.cardGlow}` : t.cardGlow,
        display:'flex', flexDirection:'column', alignItems:'center', gap:1,
        position:'relative', minWidth:0, transition:'all .15s',
      }}>
        {equipped && (
          <div style={{
            position:'absolute', top:-5, right:-3,
            width:15, height:15, borderRadius:'50%',
            background:`radial-gradient(circle at 35% 35%, ${adjustColor(ab.color,40)}, ${ab.color})`,
            border:'2px solid #1a0e04',
            display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:8, color:'#fff', fontFamily:"'Lilita One',cursive",
            boxShadow:`0 0 5px ${ab.color}88`, zIndex:2,
          }}>{slotIdx + 1}</div>
        )}
        <div style={{ fontSize:16, lineHeight:1, filter:'drop-shadow(0 1px 2px rgba(0,0,0,0.5))' }}>{ab.icon}</div>
        <div style={{ fontSize:8, color: equipped ? t.t1 : t.t2, fontFamily:"'Nunito',sans-serif",
          fontWeight:800, textShadow:t.tShadow, textAlign:'center', lineHeight:1, padding:'0 1px' }}>{ab.short}</div>
        <div style={{
          fontSize:6, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:0.6,
          padding:'0px 4px', borderRadius:2,
          background: ab.cd === 'S' ? 'rgba(74,230,106,0.18)' : 'rgba(245,200,66,0.18)',
          color: ab.cd === 'S' ? '#7cd99a' : t.t3,
          border: `1px solid ${ab.cd === 'S' ? '#4ae66a55' : '#f5c84255'}`,
        }}>{ab.cd === 'S' ? 'SHORT' : 'MED'}</div>
      </div>
    );
  };

  const CategoryBlock = ({ catKey }) => {
    const cat = CW_ABILITY_CATS[catKey];
    const list = CW_ABILITIES_V3.filter(a => a.cat === catKey);
    return (
      <div style={{ display:'flex', flexDirection:'column', gap:4, marginBottom:5 }}>
        <div style={{
          display:'flex', alignItems:'center', gap:6, padding:'2px 6px',
          background: `linear-gradient(90deg, ${cat.color}33, ${cat.color}08 80%, transparent)`,
          borderLeft: `3px solid ${cat.color}`, borderRadius:3,
        }}>
          <span style={{ fontSize:10, color:'#fef5e0', fontFamily:"'Lilita One',cursive", textShadow:t.tShadow, letterSpacing:1 }}>{cat.label}</span>
        </div>
        <div style={{ display:'flex', flexWrap:'wrap', gap:4 }}>
          {list.map(ab => <AbilityCard key={ab.id} ab={ab} />)}
        </div>
      </div>
    );
  };

  return (
    <div style={{
      width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", overflow:'hidden', position:'relative',
      display:'flex', flexDirection:'column',
    }}>
      <CWSvgDefs />
      <div style={{ position:'absolute', top:'-20%', left:'25%', right:'25%', height:'50%',
        background:`radial-gradient(ellipse, ${cls.color}1a, transparent 70%)`, pointerEvents:'none' }} />

      {/* Top ribbon */}
      <div style={{ padding:'6px 0 4px', display:'flex', justifyContent:'center', flexShrink:0 }}>
        <CWRibbon width={220}>CHOOSE YOUR CHICKEN</CWRibbon>
      </div>

      {/* Body — 3 columns */}
      <div style={{ flex:1, display:'flex', gap:8, padding:'0 10px 4px', minHeight:0 }}>

        {/* ─ Column A: class list ─ */}
        <div style={{ width:160, display:'flex', flexDirection:'column', gap:4, flexShrink:0 }}>
          {CW_CLASS_ORDER_V3.map(k => <ClassChip key={k} k={k} />)}
        </div>

        {/* ─ Column B: preview ─ */}
        <div style={{ width:170, display:'flex', flexDirection:'column', alignItems:'center', gap:4, flexShrink:0, position:'relative' }}>
          <div style={{ position:'relative' }}>
            <div style={{ position:'absolute', inset:'-12%', borderRadius:'50%',
              background:`radial-gradient(circle, ${cls.color}28, transparent 70%)` }} />
            <div style={{
              width:130, height:130, borderRadius:'50%',
              background:`radial-gradient(circle at 40% 35%, ${cls.light}22, ${cls.color}11, ${cls.dark}08)`,
              border:`2.5px solid ${cls.color}55`,
              boxShadow:`0 0 22px ${cls.color}22, inset 0 0 16px ${cls.color}11`,
              display:'flex', alignItems:'center', justifyContent:'center', position:'relative',
            }}>
              <CWChicken classKey={klass} size={100} showRing ringColor={CW_PLAYERS_V3[0].color} />
            </div>
          </div>
          <div style={{ fontSize:14, color:cls.color, textShadow:t.tGlow(cls.color), letterSpacing:0.5, lineHeight:1, marginTop:2 }}>{cls.name}</div>
          {/* Passive */}
          <div style={{
            fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            padding:'2px 7px', borderRadius:7,
            background:`linear-gradient(180deg, ${cls.color}, ${cls.dark})`,
            color:'#fef5e0', textShadow:'0 1px 2px rgba(0,0,0,0.5)',
            letterSpacing:0.5, marginTop:1,
          }}>PASSIVE · {cls.passive.toUpperCase()}</div>
          <div style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600,
            marginTop:1, lineHeight:1.25, textAlign:'center', padding:'0 4px' }}>{cls.passiveDesc}</div>
          {/* Stats */}
          <div style={{ display:'grid', gridTemplateColumns:'1fr 1fr', gap:'2px 6px', marginTop:3, width:'100%', padding:'0 4px' }}>
            {CW_STAT_ORDER_V3.map(s => (
              <div key={s} style={{ display:'flex', alignItems:'center', gap:3 }}>
                <span style={{ fontSize:7, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:0.3, width:30 }}>
                  {CW_STAT_LABELS_V3[s].toUpperCase()}
                </span>
                <div style={{ display:'flex', gap:1, flex:1 }}>
                  {Array.from({length:5}).map((_,i) => (
                    <div key={i} style={{
                      width:4, height:7, borderRadius:1,
                      background: i < cls.stats[s]
                        ? `linear-gradient(180deg, ${adjustColor(cls.color,40)}, ${cls.color})`
                        : 'rgba(20,12,6,0.6)',
                      border: i < cls.stats[s] ? 'none' : '1px solid rgba(255,200,100,0.06)',
                    }} />
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* ─ Column C: slots + ability grid + ready ─ */}
        <div style={{ flex:1, display:'flex', flexDirection:'column', gap:5, minWidth:0 }}>
          {/* Slot row */}
          <div style={{
            padding:'12px 8px 6px', position:'relative',
            background:'linear-gradient(180deg, rgba(10,6,2,0.55), rgba(10,6,2,0.25))',
            border:'1px solid rgba(138,106,58,0.4)', borderRadius:8,
            display:'flex', alignItems:'center', justifyContent:'center', gap:14,
            flexShrink:0,
          }}>
            <div style={{
              position:'absolute', top:-8, left:10,
              padding:'1px 7px', borderRadius:5,
              background:'linear-gradient(180deg, #4a3018, #2a1a0c)',
              border:`1px solid ${t.panelBorder}`,
              fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:1,
              color:t.t3, textShadow:t.tShadow,
            }}>EQUIPPED · {equipped.length}/{cls.slots}</div>
            {slots.map((_, i) => <SlotHex key={i} idx={i} />)}
            {cls.slots === 2 && (
              <div style={{ position:'relative', opacity:0.32 }}>
                <svg viewBox="0 0 100 115.5" width="42" height="49">
                  <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                    fill="rgba(20,12,6,0.7)" stroke={t.panelBorder} strokeWidth="2" strokeDasharray="4 3" />
                </svg>
                <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center', fontSize:14 }}>🔒</div>
                <div style={{
                  position:'absolute', top:-9, left:'50%', transform:'translateX(-50%)',
                  fontSize:7, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:800,
                  letterSpacing:1, whiteSpace:'nowrap',
                }}>ASSASSIN</div>
              </div>
            )}
          </div>

          {/* Ability grid */}
          <div style={{
            flex:1, padding:'4px 4px 4px 6px', overflowY:'auto', minHeight:0,
            background:'rgba(10,6,2,0.3)',
            border:'1px solid rgba(138,106,58,0.3)', borderRadius:8,
          }}>
            {['damage','control','defense','utility'].map(k => <CategoryBlock key={k} catKey={k} />)}
          </div>

          {/* Ready button */}
          <div style={{
            padding:'7px 0', borderRadius: t.btnR,
            background: allFilled ? t.startBg : 'linear-gradient(180deg, #3a3a3a, #1a1a1a)',
            border: `2px solid ${allFilled ? t.startBorder : '#0a0a0a'}`,
            boxShadow: allFilled ? t.startGlow : 'inset 0 1px 0 rgba(255,255,255,0.05), 0 2px 4px rgba(0,0,0,0.4)',
            fontFamily:"'Lilita One',cursive", fontSize:14, color: allFilled ? '#fef5e0' : '#666',
            textShadow: allFilled ? t.tShadow : 'none', textAlign:'center', letterSpacing:3,
            cursor: allFilled ? 'pointer' : 'not-allowed', opacity: allFilled ? 1 : 0.7,
            flexShrink:0,
          }}>
            {allFilled ? 'READY ▶' : `PICK ${cls.slots - equipped.length} MORE`}
          </div>
        </div>
      </div>
    </div>
  );
};

// ─── MATCH HUD · LANDSCAPE ────────────────────────────────
const CWMatchHUDv3L = ({
  abilityIds = ['fly_peck','shell'],
  hpPct = 0.72, cargoPct = 0.45, cargo = '9/20',
  cooldowns = [0, 0.4, 0],
  showStateAnnotations = false,
}) => {
  const t = CW_THEME;
  const abilities = abilityIds.map(id => CW_ABILITY_BY_ID[id]).filter(Boolean);
  const isAssassin = abilities.length === 3;

  const cargoColor = cargoPct >= 1.0 ? '#e84040'
    : cargoPct >= 0.7 ? '#ff7a30' : null;
  const cargoBg = cargoColor
    ? `linear-gradient(180deg, ${adjustColor(cargoColor,30)}, ${cargoColor})`
    : t.cargo;
  const cargoFull = cargoPct >= 1.0;

  const players = [
    { p:0, cls:'warrior',  score:42, me:true },
    { p:1, cls:'speedy',   score:17, me:false },
    { p:2, cls:'fatty',    score:28, me:false },
    { p:3, cls:'assassin', score:31, me:false },
  ];
  const ranked = [...players].sort((a,b) => b.score - a.score);
  const targetFood = 150;
  const posLabels = ['1st','2nd','3rd','4th'];

  // iso grid bg
  const grid = [];
  for (let i = 0; i < 16; i++) {
    grid.push(
      <line key={`a${i}`} x1={i*70-100} y1="0" x2={i*70+200} y2="390" stroke="rgba(80,120,40,0.05)" strokeWidth="1" />,
      <line key={`b${i}`} x1={i*70+200} y1="0" x2={i*70-100} y2="390" stroke="rgba(80,120,40,0.05)" strokeWidth="1" />
    );
  }

  // Landscape ability cluster — tucked into bottom-right corner
  // 2-ability: horizontal pair
  // 3-ability: triangle with primary closest to thumb base (bottom-right corner)
  const btnSize = 64;
  const slotPositions = isAssassin
    ? [
        { bottom: 18, right: 18 },   // S1 — primary, corner
        { bottom: 80, right: 60 },   // S2 — upper-left of primary
        { bottom: 22, right: 95 },   // S3 — far-left of primary
      ]
    : [
        { bottom: 18, right: 18 },   // S1
        { bottom: 70, right: 60 },   // S2 — upper-left
      ];

  const AbilityBtn = ({ ab, cd, pos, slotNum }) => (
    <div style={{ position:'absolute', ...pos }}>
      <div style={{
        position:'relative',
        filter: cd > 0 ? 'grayscale(0.4)' : `drop-shadow(0 0 6px ${ab.color}55)`,
        opacity: cd > 0 ? 0.85 : 1,
      }}>
        <CWHex size={btnSize} color={ab.color} icon={ab.icon} cooldownPct={cd} />
        {cd > 0 && (
          <div style={{
            position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:20, color:'#fef5e0', fontFamily:"'Lilita One',cursive",
            textShadow:'0 2px 4px rgba(0,0,0,0.8)', pointerEvents:'none',
          }}>{Math.ceil(ab.cdSec * cd)}</div>
        )}
        <div style={{
          position:'absolute', top:-2, left:-2,
          width:16, height:16, borderRadius:'50%',
          background: slotNum === 3
            ? `linear-gradient(180deg, ${t.t3}, ${adjustColor(t.t3,-30)})`
            : 'linear-gradient(180deg, #4a3018, #2a1a0c)',
          border: `1.5px solid ${slotNum === 3 ? '#fef5e0' : t.panelBorder}`,
          fontSize:9, fontFamily:"'Lilita One',cursive",
          color: slotNum === 3 ? '#1a0e04' : '#fef5e0',
          display:'flex', alignItems:'center', justifyContent:'center',
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

      {/* Decorative food */}
      <div style={{ position:'absolute', left:'50%', top:'50%', transform:'translate(-50%,-50%)' }}>
        <div style={{ width:48, height:34, borderRadius:'50%',
          background:'radial-gradient(circle at 40% 35%, #ffe066, #f5c842, #d4a020)',
          boxShadow:'0 0 30px rgba(245,200,66,0.3), 0 4px 8px rgba(0,0,0,0.3)' }} />
      </div>
      {[{x:'30%',y:'30%',s:18},{x:'70%',y:'30%',s:20},{x:'30%',y:'72%',s:16},{x:'70%',y:'72%',s:18}].map((p,i) => (
        <div key={i} style={{ position:'absolute', left:p.x, top:p.y,
          width:p.s, height:p.s*0.65, borderRadius:'50%',
          background:'radial-gradient(circle, #e8b830, #b08018)',
          boxShadow:'0 0 10px rgba(200,160,40,0.15)' }} />
      ))}

      {/* Player chicken */}
      <div style={{ position:'absolute', left:'42%', top:'62%', transform:'translate(-50%,-50%)',
        display:'flex', flexDirection:'column', alignItems:'center' }}>
        <CWChicken classKey={isAssassin ? 'assassin' : 'warrior'} size={70}
          showRing ringColor={CW_PLAYERS_V3[0].color} />
        <div style={{ width:54, height:7, background:'#0a0604', borderRadius:4, marginTop:-5,
          overflow:'hidden', border:'1.5px solid #4a2818',
          boxShadow:'inset 0 1px 2px rgba(0,0,0,0.6)' }}>
          <div style={{ width:`${hpPct*100}%`, height:'100%', background:t.hp,
            boxShadow:'inset 0 1px 0 rgba(255,200,200,0.3)' }} />
        </div>
        <div style={{ width:54, height:5, background:'#0a0604', borderRadius:3, marginTop:1,
          overflow:'hidden', border:'1px solid #4a3018',
          boxShadow: cargoFull ? `inset 0 1px 2px rgba(0,0,0,0.6), 0 0 8px ${cargoColor}88` : 'inset 0 1px 2px rgba(0,0,0,0.6)' }}>
          <div style={{ width:`${cargoPct*100}%`, height:'100%', background:cargoBg,
            boxShadow:'inset 0 1px 0 rgba(255,240,180,0.3)' }} />
        </div>
        <div style={{ display:'flex', alignItems:'center', gap:2, marginTop:2 }}>
          <CWFoodIcon size={9} />
          <span style={{ fontSize:9, color:'#fef5e0', fontFamily:"'Nunito',sans-serif",
            fontWeight:800, textShadow:'0 1px 3px rgba(0,0,0,0.8)' }}>{cargo}</span>
        </div>
        {cargoFull && (
          <div style={{
            marginTop:3, padding:'2px 8px', borderRadius:5,
            background:`linear-gradient(180deg, ${adjustColor(cargoColor,30)}, ${cargoColor})`,
            border:`1.5px solid ${adjustColor(cargoColor,-30)}`,
            boxShadow:`0 0 8px ${cargoColor}88, inset 0 1px 0 rgba(255,255,255,0.3)`,
            fontSize:8, color:'#fff', fontFamily:"'Lilita One',cursive",
            textShadow:'0 1px 2px rgba(0,0,0,0.6)', letterSpacing:1, whiteSpace:'nowrap',
          }}>FULL — RETURN TO BASE!</div>
        )}
      </div>

      {/* Enemies */}
      {[{x:'22%',y:'30%',cls:'speedy',p:1,hp:0.5},
        {x:'72%',y:'28%',cls:'fatty',p:2,hp:0.8},
        {x:'66%',y:'68%',cls:'assassin',p:3,hp:0.35}].map((e,i) => (
        <div key={i} style={{ position:'absolute', left:e.x, top:e.y,
          display:'flex', flexDirection:'column', alignItems:'center',
          transform:'scale(0.7)', opacity:0.9 }}>
          <CWChicken classKey={e.cls} size={48} showRing ringColor={CW_PLAYERS_V3[e.p].color} />
          <div style={{ width:38, height:5, background:'#0a0604', borderRadius:3, marginTop:-3,
            overflow:'hidden', border:'1px solid #4a2818' }}>
            <div style={{ width:`${e.hp*100}%`, height:'100%', background:t.hp }} />
          </div>
        </div>
      ))}

      {/* TOP-LEFT: leaderboard */}
      <div style={{
        position:'absolute', top:6, left:6, zIndex:10, width:178,
        background:'linear-gradient(180deg, rgba(30,20,10,0.88), rgba(20,14,8,0.78))',
        border:`2px solid ${t.panelBorder}88`, borderRadius:8,
        boxShadow:'inset 0 1px 0 rgba(255,200,100,0.12), 0 3px 10px rgba(0,0,0,0.5)',
        overflow:'hidden',
      }}>
        {ranked.map((r, i) => {
          const pc = CW_PLAYERS_V3[r.p].color;
          const pct = Math.min(100, (r.score / targetFood) * 100);
          return (
            <div key={r.p} style={{
              display:'flex', alignItems:'center', gap:4,
              height:22, padding:'0 6px',
              background: r.me ? `linear-gradient(90deg, ${pc}30, ${pc}08)`
                : i === 0 ? 'rgba(245,200,66,0.07)' : 'transparent',
              borderBottom: i < 3 ? '1px solid rgba(138,106,58,0.22)' : 'none',
              borderLeft: r.me ? `3px solid ${pc}` : '3px solid transparent',
            }}>
              <span style={{ fontSize:8, color: i===0 ? t.t3 : t.t2, fontWeight:700,
                width:16, textAlign:'center', fontFamily:"'Nunito',sans-serif" }}>{posLabels[i]}</span>
              <div style={{ width:7, height:7, borderRadius:'50%', flexShrink:0,
                background:`radial-gradient(circle at 35% 35%, ${adjustColor(pc,50)}, ${pc})`,
                boxShadow:`0 0 4px ${pc}88` }} />
              <span style={{ fontSize:9, color: r.me ? '#fef5e0' : pc, fontWeight: r.me?800:600,
                width:16, fontFamily:"'Nunito',sans-serif" }}>P{r.p+1}</span>
              <div style={{ flex:1, display:'flex', alignItems:'center', gap:3 }}>
                <div style={{ flex:1, height:5, background:'#0a0604', borderRadius:3,
                  overflow:'hidden', border:'1px solid #3a2816' }}>
                  <div style={{ width:`${pct}%`, height:'100%',
                    background:`linear-gradient(90deg, ${adjustColor(pc,20)}, ${pc})` }} />
                </div>
                <span style={{ fontSize:9, color:t.t1, width:18, textAlign:'right',
                  fontFamily:"'Nunito',sans-serif", fontWeight:700 }}>{r.score}</span>
              </div>
            </div>
          );
        })}
      </div>

      {/* Target badge */}
      <div style={{
        position:'absolute', top:100, left:6, zIndex:10,
        padding:'2px 7px', borderRadius:5,
        background:'linear-gradient(180deg, rgba(245,200,66,0.18), rgba(245,200,66,0.06))',
        border:'1px solid rgba(245,200,66,0.4)',
        fontSize:8, color:t.t3, fontFamily:"'Nunito',sans-serif", fontWeight:800,
        letterSpacing:0.5, display:'flex', alignItems:'center', gap:3,
      }}>
        <CWFoodIcon size={9} /><span>FIRST TO 150</span>
      </div>

      {/* TOP-RIGHT: timer */}
      <div style={{ position:'absolute', top:6, right:6, zIndex:10 }}>
        <CWTimer time="02:34" />
      </div>

      {/* Joystick bottom-left */}
      <div style={{ position:'absolute', bottom:18, left:18 }}>
        <div style={{ width:100, height:100, borderRadius:'50%',
          background:'radial-gradient(circle, rgba(255,255,255,0.06), rgba(255,255,255,0.02))',
          border:'2.5px solid rgba(255,255,255,0.12)',
          boxShadow:'inset 0 0 10px rgba(0,0,0,0.3), 0 2px 8px rgba(0,0,0,0.3)',
          display:'flex', alignItems:'center', justifyContent:'center' }}>
          <div style={{ width:42, height:42, borderRadius:'50%',
            background:'radial-gradient(circle at 40% 35%, rgba(255,255,255,0.25), rgba(255,255,255,0.08))',
            border:'2px solid rgba(255,255,255,0.2)',
            boxShadow:'inset 0 1px 0 rgba(255,255,255,0.3), 0 2px 4px rgba(0,0,0,0.3)',
            transform:'translate(5px,-3px)' }} />
        </div>
      </div>

      {/* Ability cluster bottom-right */}
      <div style={{ position:'absolute', bottom:0, right:0, width:180, height:180 }}>
        {abilities.map((ab, i) => (
          <AbilityBtn key={i} ab={ab} cd={cooldowns[i] || 0}
            pos={slotPositions[i]} slotNum={i+1} />
        ))}
      </div>

      {showStateAnnotations && (
        <>
          <Annotation x={130} y={350} label="Joystick — MOVE (left thumb)" arrow="up" />
          <Annotation x={680} y={350}
            label={isAssassin ? "Triangle: ★3 = combo finisher (furthest reach)" : "2 buttons: S1 anchor, S2 upper-left"}
            arrow="up" />
        </>
      )}
    </div>
  );
};

// ─── CONTROL STATES SHEET · LANDSCAPE ─────────────────────
// 4 states in a row instead of 2×2 grid
const CWControlStatesSheetL = () => {
  const t = CW_THEME;
  const states = [
    { key:'stunned',   label:'STUNNED',      trigger:'HP → 0',                    duration:'5s',  effect:'Full incap. Drops all cargo at feet.',
      visual:'Stars overhead, body tilted/desat, 💀 nameplate.' },
    { key:'slowed',    label:'SLOWED',       trigger:'Collision / slow ability',  duration:'1–4s',
      effect:'Move speed reduced. Speedy resists.',
      visual:'Blue tint, fading speed-trail, 🐌 nameplate.' },
    { key:'knockback', label:'KNOCKED BACK', trigger:'Push abilities',            duration:'Instant',
      effect:'Involuntary displacement. Fatty resists.',
      visual:'Motion lines + ghost trail. No persistent overlay.' },
    { key:'rooted',    label:'ROOTED',       trigger:'Root Egg trap',             duration:'2–3s',
      effect:'Cannot move, can still cast & attack.',
      visual:'Vines wrap legs. 🌱 nameplate.' },
  ];

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", padding:'10px 14px', overflow:'hidden',
      display:'flex', flexDirection:'column' }}>
      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:8 }}>
        <CWRibbon width={240}>CONTROL STATE OVERLAYS</CWRibbon>
        <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>v0.3 — new</span>
      </div>

      <div style={{ display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:8, flex:1, minHeight:0 }}>
        {states.map(s => (
          <div key={s.key} style={{
            background:t.cardBg, border:`2px solid ${t.cardBorder}`,
            borderRadius:t.cardR, boxShadow:t.cardGlow,
            padding:'8px 10px 9px', display:'flex', flexDirection:'column', gap:5,
            minHeight:0,
          }}>
            <div style={{ display:'flex', alignItems:'center', gap:6 }}>
              <span style={{ fontSize:13, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>{s.label}</span>
              <div style={{ flex:1, height:1, background:`linear-gradient(90deg, ${t.t3}66, transparent)` }} />
            </div>

            <div style={{
              height: 130, background: t.mapBg, borderRadius:6,
              border:'1px solid rgba(138,106,58,0.4)',
              display:'flex', alignItems:'center', justifyContent:'center',
              boxShadow:'inset 0 1px 4px rgba(0,0,0,0.5)', position:'relative', overflow:'hidden',
            }}>
              <CWStateChicken state={s.key} classKey="warrior" size={80} playerIdx={0} />
            </div>

            <div style={{ display:'grid', gridTemplateColumns:'auto 1fr', gap:'2px 5px',
              fontFamily:"'Nunito',sans-serif", fontSize:8.5, lineHeight:1.3 }}>
              <span style={{ color:t.t3, fontWeight:800 }}>Trig.</span>
              <span style={{ color:t.t1 }}>{s.trigger}</span>
              <span style={{ color:t.t3, fontWeight:800 }}>Dur.</span>
              <span style={{ color:t.t1 }}>{s.duration}</span>
              <span style={{ color:t.t3, fontWeight:800 }}>Effect</span>
              <span style={{ color:t.t1 }}>{s.effect}</span>
              <span style={{ color:t.t3, fontWeight:800 }}>Visual</span>
              <span style={{ color:t.t2, fontStyle:'italic' }}>{s.visual}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

// ─── ABILITY BUTTON SHEET · LANDSCAPE ─────────────────────
// 4 states in a row + anatomy strip below
const CWAbilityButtonSheetL = () => {
  const t = CW_THEME;
  const sample = CW_ABILITY_BY_ID['fly_peck'];

  const Cell = ({ label, sub, children, accent }) => (
    <div style={{
      flex:1, background:t.cardBg, border:`2px solid ${t.cardBorder}`,
      borderRadius:t.cardR, boxShadow:t.cardGlow,
      padding:'10px 8px 8px', display:'flex', flexDirection:'column', alignItems:'center', gap:6,
      minWidth:0,
    }}>
      <div style={{ display:'flex', alignItems:'center', gap:5, alignSelf:'stretch' }}>
        <div style={{ width:7, height:7, borderRadius:2, background: accent || t.t3,
          boxShadow:`0 0 6px ${accent || t.t3}aa` }} />
        <span style={{ fontSize:10, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>{label}</span>
      </div>
      <div style={{
        flex:1, width:'100%', minHeight:0, background: t.mapBg, borderRadius:6,
        border:'1px solid rgba(138,106,58,0.4)',
        display:'flex', alignItems:'center', justifyContent:'center',
        boxShadow:'inset 0 1px 4px rgba(0,0,0,0.5)', position:'relative',
      }}>
        {children}
      </div>
      <span style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600,
        textAlign:'center', lineHeight:1.3 }}>{sub}</span>
    </div>
  );

  const PulseGlow = ({ color }) => (
    <div style={{
      position:'absolute', inset:'18% 12%', borderRadius:'50%',
      background:`radial-gradient(circle, ${color}66, transparent 70%)`,
      animation: 'cwPulseL 1.4s ease-in-out infinite',
    }} />
  );

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", padding:'10px 14px', overflow:'hidden',
      display:'flex', flexDirection:'column', gap:8 }}>
      <style>{`@keyframes cwPulseL { 0%,100%{opacity:0.5} 50%{opacity:1} }`}</style>

      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between' }}>
        <CWRibbon width={220}>ABILITY BUTTON · STATES</CWRibbon>
        <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>Sample: Flying Peck</span>
      </div>

      <div style={{ display:'flex', gap:8, flex:1, minHeight:0 }}>
        <Cell label="READY" accent="#7cd99a" sub="Full color, gloss, no overlay. Tap to fire.">
          <CWHex size={64} color={sample.color} icon={sample.icon} />
        </Cell>
        <Cell label="ACTIVE" accent="#5ec5ff" sub="Pulse glow, name flash for 0.3s on press.">
          <PulseGlow color={sample.color} />
          <div style={{ position:'absolute', top:6, left:'50%', transform:'translateX(-50%)',
            padding:'1px 6px', borderRadius:4,
            background:`linear-gradient(180deg, ${adjustColor(sample.color,30)}, ${sample.color})`,
            border:`1px solid ${adjustColor(sample.color,-20)}`, whiteSpace:'nowrap',
            fontSize:8, color:'#fff', fontFamily:"'Lilita One',cursive",
            boxShadow:`0 0 8px ${sample.color}aa` }}>{sample.name}</div>
          <div style={{ filter:`drop-shadow(0 0 8px ${sample.color}cc)` }}>
            <CWHex size={64} color={sample.color} icon={sample.icon} />
          </div>
        </Cell>
        <Cell label="COOLDOWN" accent="#f5c842" sub="Radial drain, ~45% α, seconds at center.">
          <div style={{ position:'relative', filter:'grayscale(0.5)', opacity:0.55 }}>
            <CWHex size={64} color={sample.color} icon={sample.icon} cooldownPct={0.65} />
          </div>
          <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:24, color:'#fef5e0', fontFamily:"'Lilita One',cursive",
            textShadow:'0 2px 4px rgba(0,0,0,0.85)', pointerEvents:'none' }}>3</div>
        </Cell>
        <Cell label="LOCKED" accent="#7a6a48" sub="Slot 3 hidden for non-Assassin. Dimmed + lock.">
          <div style={{ position:'relative', opacity:0.35 }}>
            <svg viewBox="0 0 100 115.5" width="64" height="74">
              <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                fill="rgba(20,12,6,0.8)" stroke={t.panelBorder} strokeWidth="2" />
              <polygon points="50,8 92,31 92,84 50,107 8,84 8,31" fill="rgba(40,28,16,0.6)" />
            </svg>
          </div>
          <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:24, filter:'drop-shadow(0 1px 3px rgba(0,0,0,0.6))' }}>🔒</div>
        </Cell>
      </div>

      <div style={{
        padding:'8px 12px', background:t.cardBg,
        border:`2px solid ${t.cardBorder}`, borderRadius:t.cardR, boxShadow:t.cardGlow,
        display:'flex', gap:12, alignItems:'center', flexShrink:0,
      }}>
        <CWHex size={56} color={sample.color} icon={sample.icon} />
        <div style={{ flex:1, display:'grid', gridTemplateColumns:'repeat(3, 1fr)', gap:'2px 12px',
          fontFamily:"'Nunito',sans-serif", fontSize:9 }}>
          {[
            ['1. Drop shadow', 'Offset hex, 60% α'],
            ['2. Border', '2px stroke, dark accent'],
            ['3. Main fill', '3-stop vertical gradient'],
            ['4. Gloss', 'White→dark over fill'],
            ['5. Inner rim', 'White 15% α'],
            ['6. Cooldown', 'Black 60% α, bottom→up'],
          ].map(([k,v],i) => (
            <div key={i} style={{ display:'flex', flexDirection:'column' }}>
              <span style={{ color:t.t3, fontWeight:800 }}>{k}</span>
              <span style={{ color:t.t1 }}>{v}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

Object.assign(window, {
  CWCharacterSelectV3L, CWMatchHUDv3L,
  CWControlStatesSheetL, CWAbilityButtonSheetL,
});
