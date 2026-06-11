// CluckWars UI v3 — Ability reference sheets (portrait & landscape)
// ===================================================================
// All 14 abilities with their hex icon, name, cooldown tier, category,
// effect tag, and description. Grouped by category.

// ─── shared sub-pieces ────────────────────────────────────
const _CWAbilityRow = ({ ab, size = 56, tight = false }) => {
  const t = CW_THEME;
  return (
    <div style={{
      display:'flex', gap:tight?9:12, alignItems:'center',
      padding: tight ? '6px 8px' : '8px 10px',
      background: t.cardBg,
      border: `1.5px solid ${ab.color}55`,
      borderRadius: 10,
      boxShadow: `inset 0 0 8px ${ab.color}10, ${t.cardGlow}`,
      minWidth:0,
    }}>
      <div style={{ flexShrink:0, filter:`drop-shadow(0 0 4px ${ab.color}55)` }}>
        <CWHex size={size} color={ab.color} icon={ab.icon} />
      </div>
      <div style={{ flex:1, minWidth:0, display:'flex', flexDirection:'column', gap:1 }}>
        <div style={{ display:'flex', alignItems:'center', gap:5, flexWrap:'wrap' }}>
          <span style={{ fontSize: tight?12:14, color:t.t1, textShadow:t.tShadow,
            fontFamily:"'Lilita One',cursive", letterSpacing:0.3, lineHeight:1 }}>{ab.name}</span>
          <span style={{
            fontSize:7, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:0.8,
            padding:'1px 5px', borderRadius:3,
            background: ab.cd === 'S' ? 'rgba(74,230,106,0.18)' : 'rgba(245,200,66,0.18)',
            color: ab.cd === 'S' ? '#7cd99a' : t.t3,
            border: `1px solid ${ab.cd === 'S' ? '#4ae66a55' : '#f5c84255'}`,
          }}>{ab.cd === 'S' ? `SHORT · ${ab.cdSec}s` : `MED · ${ab.cdSec}s`}</span>
        </div>
        <div style={{ fontSize:8, color: ab.color, fontFamily:"'Nunito',sans-serif",
          fontWeight:800, letterSpacing:0.5, lineHeight:1 }}>{ab.tag}</div>
        <div style={{ fontSize: tight?9:10, color: t.t2, fontFamily:"'Nunito',sans-serif",
          fontWeight:600, lineHeight:1.3, marginTop:2 }}>{ab.desc}</div>
      </div>
    </div>
  );
};

const _CWCategoryHeader = ({ catKey, count }) => {
  const cat = CW_ABILITY_CATS[catKey];
  const t = CW_THEME;
  return (
    <div style={{
      display:'flex', alignItems:'center', gap:8, padding:'5px 10px',
      background: `linear-gradient(90deg, ${cat.color}33, ${cat.color}08 80%, transparent)`,
      borderLeft: `4px solid ${cat.color}`,
      borderRadius: 4,
      marginBottom:4,
    }}>
      <span style={{ fontSize:14, color:'#fef5e0', fontFamily:"'Lilita One',cursive",
        textShadow:t.tShadow, letterSpacing:1.2 }}>{cat.label}</span>
      <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>{cat.blurb}</span>
      <div style={{ flex:1 }} />
      <span style={{
        fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
        padding:'1px 6px', borderRadius:8,
        background:`${cat.color}33`, color:'#fef5e0',
        border:`1px solid ${cat.color}66`, letterSpacing:0.5,
      }}>{count}</span>
    </div>
  );
};

// ─── ABILITY REFERENCE · PORTRAIT (one big scrollable column) ─
const CWAbilityReference = () => {
  const t = CW_THEME;
  const cats = ['damage','control','defense','utility'];

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", padding:'14px 16px', overflow:'auto',
      display:'flex', flexDirection:'column', gap:12 }}>

      {/* Header */}
      <div style={{ display:'flex', alignItems:'flex-end', justifyContent:'space-between', gap:10 }}>
        <div style={{ display:'flex', flexDirection:'column', gap:4 }}>
          <CWRibbon width={260}>ABILITY REFERENCE</CWRibbon>
          <span style={{ fontSize:10, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600,
            marginLeft:6 }}>
            14 abilities · 4 categories · no class restrictions
          </span>
        </div>
        <div style={{ display:'flex', gap:6 }}>
          {/* Legend pills */}
          <div style={{ fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            padding:'2px 7px', borderRadius:4,
            background:'rgba(74,230,106,0.18)', color:'#7cd99a',
            border:'1px solid #4ae66a55', letterSpacing:0.5 }}>SHORT = 3–6s</div>
          <div style={{ fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            padding:'2px 7px', borderRadius:4,
            background:'rgba(245,200,66,0.18)', color:t.t3,
            border:'1px solid #f5c84255', letterSpacing:0.5 }}>MED = 8–12s</div>
        </div>
      </div>

      {/* Category sections */}
      {cats.map(catKey => {
        const list = CW_ABILITIES_V3.filter(a => a.cat === catKey);
        return (
          <div key={catKey} style={{ display:'flex', flexDirection:'column', gap:5 }}>
            <_CWCategoryHeader catKey={catKey} count={list.length} />
            <div style={{ display:'grid', gridTemplateColumns:'1fr 1fr', gap:6 }}>
              {list.map(ab => <_CWAbilityRow key={ab.id} ab={ab} />)}
            </div>
          </div>
        );
      })}

      {/* Cooldown ladder footer */}
      <div style={{
        marginTop:4, padding:'10px 14px',
        background:'rgba(245,200,66,0.06)', border:'1px solid rgba(245,200,66,0.25)',
        borderRadius:8,
        fontFamily:"'Nunito',sans-serif", fontSize:10, color:t.t2, lineHeight:1.4,
      }}>
        <span style={{ color:t.t3, fontWeight:800 }}>Design note · </span>
        Damage abilities can stun (when HP reaches zero); control abilities never deal HP.
        Defense protects self or cargo; utility gives a non-combat edge.
        Combine across categories — there are no class restrictions on selection.
      </div>
    </div>
  );
};

// ─── ABILITY REFERENCE · LANDSCAPE (4-column, one per category) ─
const CWAbilityReferenceL = () => {
  const t = CW_THEME;
  const cats = ['damage','control','defense','utility'];

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", padding:'12px 14px', overflow:'hidden',
      display:'flex', flexDirection:'column', gap:8 }}>

      {/* Header */}
      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', flexShrink:0 }}>
        <CWRibbon width={240}>ABILITY REFERENCE</CWRibbon>
        <span style={{ fontSize:10, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>
          14 abilities · 4 categories · pick freely, no class lock
        </span>
        <div style={{ display:'flex', gap:5 }}>
          <div style={{ fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            padding:'2px 7px', borderRadius:4,
            background:'rgba(74,230,106,0.18)', color:'#7cd99a',
            border:'1px solid #4ae66a55', letterSpacing:0.5 }}>SHORT 3–6s</div>
          <div style={{ fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            padding:'2px 7px', borderRadius:4,
            background:'rgba(245,200,66,0.18)', color:t.t3,
            border:'1px solid #f5c84255', letterSpacing:0.5 }}>MED 8–12s</div>
        </div>
      </div>

      {/* 4 columns */}
      <div style={{ flex:1, display:'grid', gridTemplateColumns:'repeat(4, 1fr)', gap:8, minHeight:0 }}>
        {cats.map(catKey => {
          const cat = CW_ABILITY_CATS[catKey];
          const list = CW_ABILITIES_V3.filter(a => a.cat === catKey);
          return (
            <div key={catKey} style={{
              display:'flex', flexDirection:'column', gap:5, minHeight:0,
              background:'rgba(10,6,2,0.3)',
              border:'1px solid rgba(138,106,58,0.3)',
              borderRadius:10, padding:8,
            }}>
              {/* Category mini-header */}
              <div style={{
                display:'flex', alignItems:'center', gap:6, padding:'4px 8px',
                background:`linear-gradient(90deg, ${cat.color}40, ${cat.color}10 80%, transparent)`,
                borderLeft:`3px solid ${cat.color}`, borderRadius:3,
                flexShrink:0,
              }}>
                <span style={{ fontSize:12, color:'#fef5e0', textShadow:t.tShadow, letterSpacing:1.2 }}>{cat.label}</span>
                <div style={{ flex:1 }} />
                <span style={{
                  fontSize:8, fontFamily:"'Nunito',sans-serif", fontWeight:800,
                  padding:'1px 5px', borderRadius:7,
                  background:`${cat.color}33`, color:'#fef5e0',
                  border:`1px solid ${cat.color}66`, letterSpacing:0.5,
                }}>{list.length}</span>
              </div>
              <div style={{ fontSize:8.5, color:t.t2, fontFamily:"'Nunito',sans-serif",
                fontWeight:600, padding:'0 4px', lineHeight:1.3, flexShrink:0 }}>{cat.blurb}.</div>

              {/* Cards */}
              <div style={{ display:'flex', flexDirection:'column', gap:5, overflow:'auto', minHeight:0 }}>
                {list.map(ab => <_CWAbilityRow key={ab.id} ab={ab} size={46} tight />)}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

Object.assign(window, { CWAbilityReference, CWAbilityReferenceL });
