// CluckWars UI v3 — Character Select + Ability Selection
// =======================================================
// 390×844 portrait, interactive: class pick → ability selection → ready
// Uses visual atoms from cluckwars-tokens-v2.jsx (CW_THEME, CWHex, CWChicken, etc.)
// Uses data from cluckwars-tokens-v3.jsx (CW_CLASSES_V3, CW_ABILITIES_V3, etc.)

const CWCharacterSelectV3 = ({ initialClass = 'warrior', initialSlots = null }) => {
  const t = CW_THEME;
  const [klass, setKlass]   = React.useState(initialClass);
  const cls = CW_CLASSES_V3[klass];

  // slots: array of ability ids; length = cls.slots
  const [slots, setSlots]   = React.useState(() => {
    const base = Array(cls.slots).fill(null);
    if (initialSlots) for (let i = 0; i < Math.min(initialSlots.length, cls.slots); i++) base[i] = initialSlots[i];
    return base;
  });
  const [activeSlot, setAS] = React.useState(() => {
    if (initialSlots) {
      const idx = Array(cls.slots).fill(null).map((_,i) => initialSlots[i] || null).findIndex(s => !s);
      return idx === -1 ? cls.slots - 1 : idx;
    }
    return 0;
  });

  // When class changes, resize the slot array preserving prefix
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
      // Swap (dedup): the active slot's current ability moves to where this one was
      const next = [...slots];
      [next[existingIdx], next[activeSlot]] = [next[activeSlot], next[existingIdx]];
      setSlots(next);
      const nextEmpty = next.findIndex(s => !s);
      if (nextEmpty !== -1) setAS(nextEmpty);
      return;
    }
    if (existingIdx === activeSlot) {
      // Already in active slot — unequip
      const next = [...slots]; next[activeSlot] = null; setSlots(next);
      return;
    }
    const next = [...slots]; next[activeSlot] = abId; setSlots(next);
    const nextEmpty = next.findIndex(s => !s);
    setAS(nextEmpty !== -1 ? nextEmpty : activeSlot);
  };

  // ─── Visual sub-pieces ─────────────────────────────────
  const ClassChip = ({ k }) => {
    const c = CW_CLASSES_V3[k];
    const active = k === klass;
    return (
      <div onClick={() => setKlass(k)} style={{
        flex:1, padding:'6px 4px 8px',
        background: active ? `linear-gradient(180deg, ${c.color}33, ${c.dark}22)` : t.cardBg,
        border: `2px solid ${active ? c.color : t.cardBorder}`,
        borderRadius: t.cardR, cursor:'pointer',
        boxShadow: active ? t.cardSelGlow : t.cardGlow,
        display:'flex', flexDirection:'column', alignItems:'center', gap:2,
        transform: active ? 'translateY(-2px)' : 'translateY(0)',
        transition:'all .15s ease', position:'relative', minWidth:0,
      }}>
        <CWChicken classKey={k} size={42} />
        <span style={{ fontSize:11, color: active?t.t3:t.t1, textShadow:t.tShadow, lineHeight:1 }}>{c.short}</span>
        {/* Slot pill */}
        <div style={{
          position:'absolute', top:4, right:4,
          fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
          padding:'1px 5px', borderRadius:6,
          background: c.slots === 3 ? `${t.t3}cc` : 'rgba(0,0,0,0.4)',
          color: c.slots === 3 ? '#1a0e04' : t.t2,
          textShadow:'none', border: c.slots === 3 ? `1px solid ${t.t3}` : '1px solid rgba(255,255,255,0.1)',
          letterSpacing:0.5,
        }}>{c.slots === 3 ? '★3' : 'x2'}</div>
      </div>
    );
  };

  const SlotHex = ({ idx }) => {
    const isAssassinSlot = cls.slots === 3 && idx === 2;
    const isActive = idx === activeSlot;
    const abId = slots[idx];
    const ab = abId ? CW_ABILITY_BY_ID[abId] : null;
    return (
      <div onClick={() => setAS(idx)} style={{ position:'relative', cursor:'pointer' }}>
        {ab ? (
          <div style={{
            position:'relative',
            filter: isActive ? `drop-shadow(0 0 8px ${ab.color}cc)` : 'none',
            transform: isActive ? 'scale(1.05)' : 'scale(1)',
            transition:'all .15s',
          }}>
            <CWHex size={58} color={ab.color} icon={ab.icon} label={ab.short} />
          </div>
        ) : (
          <div style={{
            width:58, height:67, position:'relative',
            transform: isActive ? 'scale(1.05)' : 'scale(1)', transition:'transform .15s',
          }}>
            <svg viewBox="0 0 100 115.5" width="100%" height="100%">
              <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                fill="rgba(20,12,6,0.6)" stroke={isActive ? t.t3 : t.panelBorder}
                strokeWidth={isActive ? 3 : 2}
                strokeDasharray={isActive ? '0' : '5 3'} />
              <polygon points="50,8 92,31 92,84 50,107 8,84 8,31"
                fill={isActive ? `${t.t3}11` : 'rgba(0,0,0,0.2)'} />
            </svg>
            <div style={{
              position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center',
              fontFamily:"'Lilita One',cursive", fontSize: 26, color: isActive ? t.t3 : t.panelBorder,
              textShadow: isActive ? `0 0 8px ${t.t3}88` : 'none', pointerEvents:'none',
            }}>+</div>
          </div>
        )}
        {/* Slot label */}
        <div style={{
          position:'absolute', top:-10, left:'50%', transform:'translateX(-50%)',
          fontSize:9, fontFamily:"'Nunito',sans-serif", fontWeight:800,
          color: isActive ? t.t3 : t.t2,
          textShadow: isActive ? `0 0 6px ${t.t3}88` : t.tShadow,
          letterSpacing:1, whiteSpace:'nowrap',
        }}>
          {isAssassinSlot ? '★ SLOT 3' : `SLOT ${idx+1}`}
        </div>
      </div>
    );
  };

  const AbilityCard = ({ ab }) => {
    const slotIdx = slots.indexOf(ab.id);
    const equipped = slotIdx !== -1;
    return (
      <div onClick={() => onPickAbility(ab.id)} style={{
        flex:'0 0 calc(25% - 5px)', padding:'6px 3px 5px',
        background: equipped ? `linear-gradient(180deg, ${ab.color}33, ${ab.color}11)` : t.cardBg,
        border: `1.5px solid ${equipped ? ab.color : t.cardBorder}`,
        borderRadius: 10, cursor:'pointer',
        boxShadow: equipped ? `0 0 8px ${ab.color}66, ${t.cardGlow}` : t.cardGlow,
        display:'flex', flexDirection:'column', alignItems:'center', gap:2,
        position:'relative', minWidth:0,
        transition:'all .15s',
      }}>
        {/* Slot badge if equipped */}
        {equipped && (
          <div style={{
            position:'absolute', top:-6, right:-4,
            width:18, height:18, borderRadius:'50%',
            background:`radial-gradient(circle at 35% 35%, ${adjustColor(ab.color,40)}, ${ab.color})`,
            border:'2px solid #1a0e04',
            display:'flex', alignItems:'center', justifyContent:'center',
            fontSize:10, color:'#fff', fontFamily:"'Lilita One',cursive",
            textShadow:'0 1px 2px rgba(0,0,0,0.6)',
            boxShadow:`0 0 6px ${ab.color}88`, zIndex:2,
          }}>{slotIdx + 1}</div>
        )}
        <div style={{ fontSize: 20, lineHeight:1, filter:'drop-shadow(0 1px 2px rgba(0,0,0,0.5))' }}>{ab.icon}</div>
        <div style={{ fontSize: 9, color: equipped ? t.t1 : t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:800, textShadow:t.tShadow, textAlign:'center', lineHeight:1.1, minHeight:20, display:'flex', alignItems:'center' }}>{ab.name}</div>
        <div style={{
          fontSize:7, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:0.8,
          padding:'1px 5px', borderRadius:3,
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
      <div style={{ display:'flex', flexDirection:'column', gap:6, marginBottom:8 }}>
        {/* Category header */}
        <div style={{
          display:'flex', alignItems:'center', gap:6, padding:'3px 8px',
          background: `linear-gradient(90deg, ${cat.color}33, ${cat.color}08 80%, transparent)`,
          borderLeft: `3px solid ${cat.color}`,
          borderRadius: 4,
        }}>
          <span style={{ fontSize:12, color:'#fef5e0', fontFamily:"'Lilita One',cursive", textShadow:t.tShadow, letterSpacing:1 }}>{cat.label}</span>
          <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600, flex:1 }}>{cat.blurb}</span>
        </div>
        {/* Ability cards grid (4 per row, wraps for Control which has 4 already, fits) */}
        <div style={{ display:'flex', flexWrap:'wrap', gap:6 }}>
          {list.map(ab => <AbilityCard key={ab.id} ab={ab} />)}
        </div>
      </div>
    );
  };

  return (
    <div style={{
      width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", display:'flex', flexDirection:'column',
      overflow:'hidden', position:'relative',
    }}>
      <CWSvgDefs />

      {/* Class color glow background */}
      <div style={{ position:'absolute', top:'-10%', left:'15%', right:'15%', height:'40%',
        background:`radial-gradient(ellipse, ${cls.color}1a, transparent 70%)`, pointerEvents:'none' }} />

      {/* ── Header ribbon ── */}
      <div style={{ padding:'10px 0 6px', display:'flex', justifyContent:'center', flexShrink:0 }}>
        <CWRibbon width={240}>CHOOSE YOUR CHICKEN</CWRibbon>
      </div>

      {/* ── Class row (4 chips) ── */}
      <div style={{ display:'flex', gap:6, padding:'4px 10px 8px', flexShrink:0 }}>
        {CW_CLASS_ORDER_V3.map(k => <ClassChip key={k} k={k} />)}
      </div>

      {/* ── Selected class preview band ── */}
      <div style={{
        margin:'0 10px', padding:'8px 12px',
        background: `linear-gradient(180deg, ${cls.color}1c 0%, ${cls.dark}10 100%)`,
        border:`2px solid ${cls.color}55`, borderRadius:12,
        boxShadow:`0 0 12px ${cls.color}22, inset 0 1px 0 rgba(255,255,255,0.06)`,
        display:'flex', gap:10, alignItems:'center', flexShrink:0,
      }}>
        <div style={{ position:'relative', flexShrink:0 }}>
          <div style={{ position:'absolute', inset:'-6px', borderRadius:'50%', background:`radial-gradient(circle, ${cls.color}33, transparent 70%)` }} />
          <CWChicken classKey={klass} size={66} showRing ringColor={CW_PLAYERS_V3[0].color} />
        </div>
        <div style={{ flex:1, minWidth:0 }}>
          <div style={{ fontSize:16, color:cls.color, textShadow:t.tGlow(cls.color), letterSpacing:0.5, lineHeight:1.1 }}>{cls.name}</div>
          {/* Passive */}
          <div style={{ display:'flex', alignItems:'center', gap:5, marginTop:3 }}>
            <span style={{
              fontSize:9, fontFamily:"'Nunito',sans-serif", fontWeight:800,
              padding:'1px 6px', borderRadius:8,
              background:`linear-gradient(180deg, ${cls.color}, ${cls.dark})`,
              color:'#fef5e0', textShadow:'0 1px 2px rgba(0,0,0,0.5)',
              letterSpacing:0.5, flexShrink:0,
            }}>PASSIVE · {cls.passive.toUpperCase()}</span>
          </div>
          <div style={{ fontSize:10, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600, marginTop:3, lineHeight:1.25 }}>
            {cls.passiveDesc}
          </div>
          {/* Compact stat row */}
          <div style={{ display:'flex', gap:6, marginTop:5, flexWrap:'wrap' }}>
            {CW_STAT_ORDER_V3.map(s => (
              <div key={s} style={{ display:'flex', alignItems:'center', gap:2 }}>
                <span style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:0.3 }}>
                  {CW_STAT_LABELS_V3[s].toUpperCase()}
                </span>
                <div style={{ display:'flex', gap:1 }}>
                  {Array.from({length:5}).map((_,i) => (
                    <div key={i} style={{
                      width:5, height:8, borderRadius:1,
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
      </div>

      {/* ── Slot row ── */}
      <div style={{
        margin:'10px 10px 6px', padding:'14px 8px 8px',
        background:'linear-gradient(180deg, rgba(10,6,2,0.55), rgba(10,6,2,0.25))',
        border:'1px solid rgba(138,106,58,0.4)',
        borderRadius:10, flexShrink:0,
        display:'flex', alignItems:'center', justifyContent:'center', gap:18,
        position:'relative',
      }}>
        <div style={{
          position:'absolute', top:-9, left:12,
          padding:'1px 8px', borderRadius:6,
          background:'linear-gradient(180deg, #4a3018, #2a1a0c)',
          border:`1px solid ${t.panelBorder}`,
          fontSize:9, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:1,
          color:t.t3, textShadow:t.tShadow,
        }}>
          EQUIPPED · {equipped.length}/{cls.slots}
        </div>
        {slots.map((_, i) => <SlotHex key={i} idx={i} />)}
        {/* If not assassin, show locked slot 3 hint */}
        {cls.slots === 2 && (
          <div style={{ position:'relative', opacity:0.35 }}>
            <svg viewBox="0 0 100 115.5" width="48" height="55">
              <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87" fill="rgba(20,12,6,0.7)" stroke={t.panelBorder} strokeWidth="2" strokeDasharray="4 3" />
            </svg>
            <div style={{ position:'absolute', inset:0, display:'flex', alignItems:'center', justifyContent:'center', fontSize:16 }}>🔒</div>
            <div style={{
              position:'absolute', top:-10, left:'50%', transform:'translateX(-50%)',
              fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:800,
              letterSpacing:1, whiteSpace:'nowrap',
            }}>ASSASSIN</div>
          </div>
        )}
      </div>

      {/* ── Ability grid (scrollable) ── */}
      <div style={{
        flex:1, margin:'0 10px', padding:'8px 4px 8px 8px',
        overflowY:'auto', minHeight:0,
        background:'rgba(10,6,2,0.3)',
        border:'1px solid rgba(138,106,58,0.3)', borderRadius:10,
      }}>
        {['damage','control','defense','utility'].map(k => <CategoryBlock key={k} catKey={k} />)}
        <div style={{ height:4 }} />
      </div>

      {/* ── Ready button ── */}
      <div style={{ padding:'8px 10px 12px', flexShrink:0 }}>
        <div style={{
          padding:'12px 0', borderRadius: t.btnR,
          background: allFilled ? t.startBg : 'linear-gradient(180deg, #3a3a3a, #1a1a1a)',
          border: `2px solid ${allFilled ? t.startBorder : '#0a0a0a'}`,
          boxShadow: allFilled ? t.startGlow : 'inset 0 1px 0 rgba(255,255,255,0.05), 0 2px 4px rgba(0,0,0,0.4)',
          fontFamily:"'Lilita One',cursive", fontSize:20, color: allFilled ? '#fef5e0' : '#666',
          textShadow: allFilled ? t.tShadow : 'none', textAlign:'center', letterSpacing:3,
          cursor: allFilled ? 'pointer' : 'not-allowed', opacity: allFilled ? 1 : 0.7,
        }}>
          {allFilled ? 'READY ▶' : `PICK ${cls.slots - equipped.length} MORE ABILIT${cls.slots - equipped.length === 1 ? 'Y' : 'IES'}`}
        </div>
      </div>
    </div>
  );
};

Object.assign(window, { CWCharacterSelectV3 });
