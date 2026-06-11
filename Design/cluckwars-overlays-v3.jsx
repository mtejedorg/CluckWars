// CluckWars UI v3 — Match End & Lobby (portrait + landscape)
// ============================================================
// Full-bleed phone screens, not floating overlays.
// Both screens use the v0.3 visual language and surface the
// per-player class + equipped abilities (new in v0.3).

// ─── shared data ──────────────────────────────────────────
const _CW_DEMO_RESULTS = [
  { p:0, cls:'warrior',  abilities:['fly_peck','shell'],     name:'You',     score:152, kills:4, stuns:3, deposits:11 },
  { p:2, cls:'fatty',    abilities:['turtle','spine'],       name:'BrunoB',  score:128, kills:1, stuns:2, deposits:9  },
  { p:3, cls:'assassin', abilities:['steal','burst','peck'], name:'KaiZ',    score:87,  kills:6, stuns:1, deposits:6  },
  { p:1, cls:'speedy',   abilities:['burst','peck'],         name:'DashFox', score:43,  kills:2, stuns:0, deposits:3  },
];
const _CW_TARGET = 150;

const _CW_DEMO_LOBBY = [
  { p:0, cls:'warrior',  abilities:['fly_peck','shell'],     name:'You',     ready:true,  host:true },
  { p:1, cls:'speedy',   abilities:['burst','peck'],         name:'DashFox', ready:true,  host:false },
  { p:2, cls:'fatty',    abilities:['turtle','spine'],       name:'BrunoB',  ready:false, host:false },
  { p:3, cls:null,       abilities:[],                       name:null,      ready:false, host:false, empty:true },
];

// ─── tiny pieces ──────────────────────────────────────────
const _MedalDot = ({ rank, color, size = 30 }) => {
  const medals = ['🥇','🥈','🥉',''];
  const ranks = ['1st','2nd','3rd','4th'];
  return (
    <div style={{
      width:size, height:size, borderRadius:'50%', flexShrink:0,
      background: rank === 0
        ? `radial-gradient(circle at 35% 30%, #fff5b0, #f5c842 50%, #b08018)`
        : rank === 1
          ? `radial-gradient(circle at 35% 30%, #f0f0f5, #b8b8c8 50%, #6a6a78)`
          : rank === 2
            ? `radial-gradient(circle at 35% 30%, #f4c89a, #c08850 50%, #6a4020)`
            : `radial-gradient(circle at 35% 30%, ${adjustColor(color,40)}, ${color} 50%, ${adjustColor(color,-30)})`,
      border:`2px solid ${rank <= 2 ? '#1a0e04' : adjustColor(color,-30)}`,
      boxShadow:`0 2px 4px rgba(0,0,0,0.5), inset 0 1px 0 rgba(255,255,255,0.4)`,
      display:'flex', alignItems:'center', justifyContent:'center',
      fontSize: size * 0.5,
      fontFamily:"'Lilita One',cursive",
      color: rank <= 2 ? '#1a0e04' : '#fef5e0',
      textShadow: rank <= 2 ? 'none' : '0 1px 2px rgba(0,0,0,0.5)',
    }}>
      {rank <= 2 ? medals[rank] : ranks[rank]}
    </div>
  );
};

const _AbilityChip = ({ abId, size = 24 }) => {
  const ab = CW_ABILITY_BY_ID[abId];
  if (!ab) return null;
  return (
    <div title={ab.name} style={{ display:'inline-flex', alignItems:'center', flexShrink:0,
      filter:`drop-shadow(0 0 3px ${ab.color}66)` }}>
      <CWHex size={size} color={ab.color} icon={ab.icon} />
    </div>
  );
};

// ─── MATCH END · PORTRAIT ─────────────────────────────────
const CWMatchEndV3 = () => {
  const t = CW_THEME;
  const ranked = [..._CW_DEMO_RESULTS].sort((a,b) => b.score - a.score);
  const winner = ranked[0];
  const wc = CW_PLAYERS_V3[winner.p].color;
  const maxScore = Math.max(_CW_TARGET, ranked[0].score);

  // confetti particles
  const confetti = Array.from({length: 28}).map((_,i) => ({
    left: (i * 137) % 100,
    top: (i * 79) % 100,
    size: 3 + (i % 4) * 2,
    color: [wc, '#f5c842', '#fef5e0', CW_PLAYERS_V3[2].color][i % 4],
    delay: i * 0.08,
  }));

  return (
    <div style={{ width:'100%', height:'100%', background:t.mapBg,
      fontFamily:"'Lilita One',cursive", position:'relative', overflow:'hidden',
      display:'flex', flexDirection:'column' }}>

      {/* Winner-color spotlight */}
      <div style={{ position:'absolute', top:'-20%', left:'-20%', right:'-20%', height:'80%',
        background:`radial-gradient(ellipse at 50% 35%, ${wc}33, transparent 60%)`,
        pointerEvents:'none' }} />

      {/* Confetti */}
      <style>{`
        @keyframes cwConfetti { 0%{opacity:0;transform:translateY(-20px)} 20%{opacity:1} 100%{opacity:1;transform:translateY(0)} }
      `}</style>
      {confetti.map((c, i) => (
        <div key={i} style={{
          position:'absolute', left:`${c.left}%`, top:`${c.top * 0.55}%`,
          width:c.size, height:c.size, borderRadius: i % 3 === 0 ? '50%' : 2,
          background:c.color, opacity:0.4,
          boxShadow:`0 0 4px ${c.color}88`,
          animation:`cwConfetti 1.4s ${c.delay}s ease-out backwards`,
          pointerEvents:'none',
        }} />
      ))}

      {/* ───── WINNER HERO BLOCK ───── */}
      <div style={{
        margin:'14px 14px 8px', padding:'12px 14px 14px', position:'relative',
        background:`linear-gradient(180deg, ${wc}28, ${wc}08 80%)`,
        border:`2px solid ${wc}88`, borderRadius:14,
        boxShadow:`0 0 20px ${wc}33, inset 0 1px 0 rgba(255,255,255,0.1), 0 4px 12px rgba(0,0,0,0.5)`,
        display:'flex', flexDirection:'column', alignItems:'center', gap:6, flexShrink:0,
      }}>
        <div style={{ fontSize:34, lineHeight:1, filter:`drop-shadow(0 2px 6px ${wc}aa)` }}>👑</div>

        <CWRibbon color={wc} width={260}>VICTORY!</CWRibbon>

        {/* Big winner chicken */}
        <div style={{ position:'relative' }}>
          <div style={{ position:'absolute', inset:'-15%', borderRadius:'50%',
            background:`radial-gradient(circle, ${wc}33, transparent 70%)` }} />
          <CWChicken classKey={winner.cls} size={88} showRing ringColor={wc} />
        </div>

        <div style={{ fontSize:18, color:'#fef5e0', textShadow:`0 0 10px ${wc}aa, 0 2px 4px rgba(0,0,0,0.6)`,
          letterSpacing:1, lineHeight:1 }}>
          {winner.name.toUpperCase()}
        </div>
        <div style={{ fontSize:10, color:wc, fontFamily:"'Nunito',sans-serif", fontWeight:800,
          letterSpacing:1.2, textShadow:`0 0 6px ${wc}88`, marginTop:-2 }}>
          {CW_CLASSES_V3[winner.cls].short.toUpperCase()} · P{winner.p+1}
        </div>

        {/* Winner abilities pinned */}
        <div style={{ display:'flex', gap:5, marginTop:2 }}>
          {winner.abilities.map(id => <_AbilityChip key={id} abId={id} size={32} />)}
        </div>

        {/* Big food number */}
        <div style={{ display:'flex', alignItems:'center', gap:5, marginTop:2 }}>
          <CWFoodIcon size={18} />
          <span style={{ fontSize:30, color:t.t3, textShadow:t.tGlow(t.t3), lineHeight:1, letterSpacing:1 }}>
            {winner.score}
          </span>
          <span style={{ fontSize:11, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700,
            alignSelf:'flex-end', marginBottom:3 }}>FOOD</span>
        </div>
      </div>

      {/* ───── LEADERBOARD ───── */}
      <div style={{ flex:1, margin:'0 14px', padding:'8px 4px 8px 4px', overflow:'auto', minHeight:0 }}>
        <div style={{ display:'flex', alignItems:'center', gap:6, padding:'0 8px 4px' }}>
          <span style={{ fontSize:12, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>FINAL STANDINGS</span>
          <div style={{ flex:1, height:1, background:`linear-gradient(90deg, ${t.t3}66, transparent)` }} />
        </div>

        <div style={{ display:'flex', flexDirection:'column', gap:5 }}>
          {ranked.map((r, i) => {
            const pc = CW_PLAYERS_V3[r.p].color;
            const pct = (r.score / maxScore) * 100;
            return (
              <div key={r.p} style={{
                display:'flex', alignItems:'center', gap:8, padding:'7px 9px',
                background: i === 0
                  ? `linear-gradient(90deg, ${pc}26, ${pc}08)`
                  : t.cardBg,
                border: `1.5px solid ${i === 0 ? pc + '66' : t.cardBorder}`,
                borderRadius: 10,
                boxShadow: i === 0 ? `0 0 8px ${pc}33, ${t.cardGlow}` : t.cardGlow,
              }}>
                <_MedalDot rank={i} color={pc} size={28} />
                <CWChicken classKey={r.cls} size={36} showRing ringColor={pc} />
                <div style={{ flex:1, minWidth:0, display:'flex', flexDirection:'column', gap:1 }}>
                  <div style={{ display:'flex', alignItems:'center', gap:5 }}>
                    <span style={{ fontSize:12, color:'#fef5e0', textShadow:t.tShadow, letterSpacing:0.3, lineHeight:1 }}>
                      {r.name}
                    </span>
                    <span style={{ fontSize:8, color:pc, fontFamily:"'Nunito',sans-serif", fontWeight:800,
                      letterSpacing:0.5 }}>P{r.p+1}</span>
                  </div>
                  <div style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, lineHeight:1 }}>
                    {CW_CLASSES_V3[r.cls].short.toUpperCase()}
                  </div>
                  {/* Food bar */}
                  <div style={{ height:5, background:'#0a0604', borderRadius:3, marginTop:2,
                    overflow:'hidden', border:'1px solid #3a2816' }}>
                    <div style={{ width:`${pct}%`, height:'100%',
                      background:`linear-gradient(90deg, ${adjustColor(pc,20)}, ${pc})`,
                      boxShadow:`inset 0 1px 0 rgba(255,255,255,0.2)` }} />
                  </div>
                </div>
                {/* Score */}
                <div style={{ display:'flex', flexDirection:'column', alignItems:'flex-end', flexShrink:0 }}>
                  <div style={{ display:'flex', alignItems:'center', gap:3 }}>
                    <CWFoodIcon size={12} />
                    <span style={{ fontSize:18, color:'#fef5e0', textShadow:t.tShadow, lineHeight:1 }}>
                      {r.score}
                    </span>
                  </div>
                  <div style={{ display:'flex', gap:6, fontFamily:"'Nunito',sans-serif", fontWeight:700,
                    fontSize:8, color:t.t2, marginTop:2, lineHeight:1 }}>
                    <span>K {r.kills}</span>
                    <span>·</span>
                    <span>D {r.deposits}</span>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* ───── ACTION BAR ───── */}
      <div style={{ display:'flex', gap:8, padding:'10px 14px 14px', flexShrink:0 }}>
        <div style={{
          flex:1, padding:'12px 0', borderRadius: t.btnR,
          background: t.cardBg, border:`2px solid ${t.cardBorder}`,
          boxShadow: t.cardGlow,
          fontFamily:"'Lilita One',cursive", fontSize:13, color:t.t1,
          textShadow:t.tShadow, textAlign:'center', letterSpacing:2, cursor:'pointer',
        }}>LEAVE</div>
        <div style={{
          flex:2, padding:'12px 0', borderRadius: t.btnR,
          background: t.startBg, border:`2px solid ${t.startBorder}`,
          boxShadow: t.startGlow,
          fontFamily:"'Lilita One',cursive", fontSize:15, color:'#fef5e0',
          textShadow:t.tShadow, textAlign:'center', letterSpacing:2, cursor:'pointer',
        }}>REMATCH ▶</div>
      </div>
    </div>
  );
};

// ─── MATCH END · LANDSCAPE ────────────────────────────────
const CWMatchEndV3L = () => {
  const t = CW_THEME;
  const ranked = [..._CW_DEMO_RESULTS].sort((a,b) => b.score - a.score);
  const winner = ranked[0];
  const wc = CW_PLAYERS_V3[winner.p].color;
  const maxScore = Math.max(_CW_TARGET, ranked[0].score);

  const confetti = Array.from({length: 22}).map((_,i) => ({
    left: (i * 137) % 100, top: (i * 79) % 100,
    size: 3 + (i % 4) * 2,
    color: [wc, '#f5c842', '#fef5e0', CW_PLAYERS_V3[2].color][i % 4],
    delay: i * 0.08,
  }));

  return (
    <div style={{ width:'100%', height:'100%', background:t.mapBg,
      fontFamily:"'Lilita One',cursive", position:'relative', overflow:'hidden',
      display:'flex' }}>
      <style>{`@keyframes cwConfettiL { 0%{opacity:0;transform:translateY(-15px)} 20%{opacity:1} 100%{opacity:1;transform:translateY(0)} }`}</style>

      <div style={{ position:'absolute', top:'-20%', left:'-10%', width:'60%', height:'140%',
        background:`radial-gradient(ellipse at 40% 50%, ${wc}33, transparent 60%)`, pointerEvents:'none' }} />

      {confetti.map((c, i) => (
        <div key={i} style={{
          position:'absolute', left:`${c.left}%`, top:`${c.top}%`,
          width:c.size, height:c.size, borderRadius: i % 3 === 0 ? '50%' : 2,
          background:c.color, opacity:0.4, boxShadow:`0 0 4px ${c.color}88`,
          animation:`cwConfettiL 1.4s ${c.delay}s ease-out backwards`, pointerEvents:'none',
        }} />
      ))}

      {/* ─ LEFT: hero ─ */}
      <div style={{ width:300, padding:'12px 14px',
        display:'flex', flexDirection:'column', alignItems:'center', gap:5,
        flexShrink:0, position:'relative', zIndex:2,
      }}>
        <div style={{ fontSize:26, lineHeight:1, filter:`drop-shadow(0 2px 6px ${wc}aa)` }}>👑</div>
        <CWRibbon color={wc} width={200}>VICTORY!</CWRibbon>
        <div style={{ position:'relative' }}>
          <div style={{ position:'absolute', inset:'-15%', borderRadius:'50%',
            background:`radial-gradient(circle, ${wc}33, transparent 70%)` }} />
          <CWChicken classKey={winner.cls} size={70} showRing ringColor={wc} />
        </div>
        <div style={{ fontSize:15, color:'#fef5e0', textShadow:`0 0 10px ${wc}aa, 0 2px 4px rgba(0,0,0,0.6)`,
          letterSpacing:1, lineHeight:1 }}>
          {winner.name.toUpperCase()}
        </div>
        <div style={{ fontSize:9, color:wc, fontFamily:"'Nunito',sans-serif", fontWeight:800,
          letterSpacing:1, textShadow:`0 0 6px ${wc}88`, marginTop:-2 }}>
          {CW_CLASSES_V3[winner.cls].short.toUpperCase()} · P{winner.p+1}
        </div>
        <div style={{ display:'flex', gap:4, marginTop:1 }}>
          {winner.abilities.map(id => <_AbilityChip key={id} abId={id} size={26} />)}
        </div>
        <div style={{ display:'flex', alignItems:'center', gap:4, marginTop:1 }}>
          <CWFoodIcon size={16} />
          <span style={{ fontSize:26, color:t.t3, textShadow:t.tGlow(t.t3), lineHeight:1, letterSpacing:1 }}>{winner.score}</span>
          <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700,
            alignSelf:'flex-end', marginBottom:2 }}>FOOD</span>
        </div>
      </div>

      {/* ─ Vertical divider ─ */}
      <div style={{ width:1, background:`linear-gradient(180deg, transparent, ${t.panelBorder}66 30%, ${t.panelBorder}66 70%, transparent)`,
        margin:'14px 0', flexShrink:0 }} />

      {/* ─ RIGHT: leaderboard + actions ─ */}
      <div style={{ flex:1, padding:'10px 14px', display:'flex', flexDirection:'column', gap:6, minWidth:0 }}>
        <div style={{ display:'flex', alignItems:'center', gap:6 }}>
          <span style={{ fontSize:12, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>FINAL STANDINGS</span>
          <div style={{ flex:1, height:1, background:`linear-gradient(90deg, ${t.t3}66, transparent)` }} />
          <span style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>
            target {_CW_TARGET} · {ranked[0].score >= _CW_TARGET ? 'reached' : 'timer out'}
          </span>
        </div>

        <div style={{ flex:1, display:'flex', flexDirection:'column', gap:4, overflow:'auto', minHeight:0 }}>
          {ranked.map((r, i) => {
            const pc = CW_PLAYERS_V3[r.p].color;
            const pct = (r.score / maxScore) * 100;
            return (
              <div key={r.p} style={{
                display:'flex', alignItems:'center', gap:7, padding:'5px 8px',
                background: i === 0 ? `linear-gradient(90deg, ${pc}26, ${pc}08)` : t.cardBg,
                border: `1.5px solid ${i === 0 ? pc + '66' : t.cardBorder}`,
                borderRadius: 8,
                boxShadow: i === 0 ? `0 0 6px ${pc}33, ${t.cardGlow}` : t.cardGlow,
              }}>
                <_MedalDot rank={i} color={pc} size={24} />
                <CWChicken classKey={r.cls} size={30} showRing ringColor={pc} />
                <div style={{ width:64, flexShrink:0 }}>
                  <div style={{ fontSize:11, color:'#fef5e0', textShadow:t.tShadow, lineHeight:1 }}>{r.name}</div>
                  <div style={{ fontSize:7, color:pc, fontFamily:"'Nunito',sans-serif", fontWeight:800,
                    letterSpacing:0.5, marginTop:1 }}>
                    P{r.p+1} · {CW_CLASSES_V3[r.cls].short.toUpperCase()}
                  </div>
                </div>
                {/* Abilities */}
                <div style={{ display:'flex', gap:2, width:78, flexShrink:0 }}>
                  {r.abilities.map(id => <_AbilityChip key={id} abId={id} size={22} />)}
                </div>
                {/* Bar */}
                <div style={{ flex:1, height:8, background:'#0a0604', borderRadius:4,
                  overflow:'hidden', border:'1px solid #3a2816', minWidth:0 }}>
                  <div style={{ width:`${pct}%`, height:'100%',
                    background:`linear-gradient(90deg, ${adjustColor(pc,20)}, ${pc})`,
                    boxShadow:`inset 0 1px 0 rgba(255,255,255,0.2)` }} />
                </div>
                {/* Stats */}
                <div style={{ display:'flex', alignItems:'center', gap:8, flexShrink:0, width:120, justifyContent:'flex-end' }}>
                  <div style={{ display:'flex', gap:6, fontFamily:"'Nunito',sans-serif", fontWeight:700,
                    fontSize:9, color:t.t2 }}>
                    <span>K{r.kills}</span><span>S{r.stuns}</span><span>D{r.deposits}</span>
                  </div>
                  <div style={{ display:'flex', alignItems:'center', gap:3 }}>
                    <CWFoodIcon size={11} />
                    <span style={{ fontSize:16, color:'#fef5e0', textShadow:t.tShadow, lineHeight:1, width:30, textAlign:'right' }}>{r.score}</span>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        <div style={{ display:'flex', gap:6, flexShrink:0 }}>
          <div style={{
            flex:1, padding:'8px 0', borderRadius: t.btnR,
            background: t.cardBg, border:`2px solid ${t.cardBorder}`, boxShadow: t.cardGlow,
            fontFamily:"'Lilita One',cursive", fontSize:11, color:t.t1,
            textShadow:t.tShadow, textAlign:'center', letterSpacing:2, cursor:'pointer',
          }}>LEAVE</div>
          <div style={{
            flex:2, padding:'8px 0', borderRadius: t.btnR,
            background: t.startBg, border:`2px solid ${t.startBorder}`, boxShadow: t.startGlow,
            fontFamily:"'Lilita One',cursive", fontSize:13, color:'#fef5e0',
            textShadow:t.tShadow, textAlign:'center', letterSpacing:2, cursor:'pointer',
          }}>REMATCH ▶</div>
        </div>
      </div>
    </div>
  );
};

// ═══════════════════════════════════════════════════════════
// ─── LOBBY ──────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════

// Player card used by lobby (portrait, full-width)
const _LobbyCardP = ({ pl, idx }) => {
  const t = CW_THEME;
  const pc = CW_PLAYERS_V3[idx].color;

  if (pl.empty) {
    return (
      <div style={{
        display:'flex', alignItems:'center', justifyContent:'center', gap:8,
        padding:'18px 10px', borderRadius:12,
        border:`2px dashed ${t.panelBorder}`,
        background:'rgba(20,14,8,0.3)',
      }}>
        <div style={{ width:14, height:14, borderRadius:'50%', border:`2px dashed ${t.t2}`, opacity:0.4 }} />
        <span style={{ fontSize:11, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:1 }}>
          WAITING FOR PLAYER {idx+1}…
        </span>
      </div>
    );
  }

  const cls = CW_CLASSES_V3[pl.cls];
  const ready = pl.ready;

  return (
    <div style={{
      display:'flex', alignItems:'center', gap:9, padding:'8px 10px', position:'relative',
      borderRadius:12, background: ready ? `linear-gradient(90deg, ${pc}22, ${pc}06)` : t.cardBg,
      border:`2px solid ${ready ? pc+'88' : pc + '44'}`,
      boxShadow: ready
        ? `0 0 10px ${pc}33, inset 0 1px 0 rgba(255,255,255,0.06), 0 3px 8px rgba(0,0,0,0.3)`
        : `${t.cardGlow}, inset 0 0 8px ${pc}10`,
    }}>
      {/* Player color stripe on left */}
      <div style={{
        position:'absolute', left:0, top:0, bottom:0, width:5,
        background:`linear-gradient(180deg, ${adjustColor(pc,30)}, ${pc}, ${adjustColor(pc,-20)})`,
        borderRadius:'10px 0 0 10px',
        boxShadow:`inset -1px 0 0 rgba(0,0,0,0.3)`,
      }} />
      <CWChicken classKey={pl.cls} size={48} showRing ringColor={pc} />
      <div style={{ flex:1, minWidth:0, display:'flex', flexDirection:'column', gap:1 }}>
        <div style={{ display:'flex', alignItems:'center', gap:5 }}>
          <span style={{ fontSize:13, color:'#fef5e0', textShadow:t.tShadow, lineHeight:1 }}>{pl.name}</span>
          <span style={{ fontSize:8, color:pc, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            letterSpacing:0.5 }}>P{idx+1}</span>
          {pl.host && (
            <span style={{
              fontSize:7, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:0.5,
              padding:'1px 5px', borderRadius:3,
              background:`linear-gradient(180deg, ${t.t3}, ${adjustColor(t.t3,-25)})`,
              color:'#1a0e04', boxShadow:`0 0 4px ${t.t3}88`,
            }}>HOST</span>
          )}
        </div>
        <div style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, lineHeight:1.1 }}>
          {cls.short.toUpperCase()} · <span style={{ color:t.t3 }}>{cls.passive}</span>
        </div>
        {/* Abilities */}
        <div style={{ display:'flex', gap:4, marginTop:3 }}>
          {pl.abilities.map(id => <_AbilityChip key={id} abId={id} size={22} />)}
          {pl.abilities.length < cls.slots && Array.from({length: cls.slots - pl.abilities.length}).map((_,i) => (
            <div key={i} style={{ width:22, height:25, position:'relative' }}>
              <svg viewBox="0 0 100 115.5" width="100%" height="100%">
                <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                  fill="rgba(20,12,6,0.5)" stroke={t.panelBorder} strokeWidth="2" strokeDasharray="4 3" />
              </svg>
            </div>
          ))}
        </div>
      </div>
      {/* Ready chip */}
      <div style={{
        display:'flex', flexDirection:'column', alignItems:'center', gap:2, flexShrink:0,
        padding:'5px 10px', borderRadius:8, minWidth:64, textAlign:'center',
        background: ready
          ? `linear-gradient(180deg, #5ac54f, #228b22)`
          : `linear-gradient(180deg, rgba(40,30,20,0.6), rgba(20,14,8,0.4))`,
        border: `1.5px solid ${ready ? '#4ae66a' : t.panelBorder}`,
        boxShadow: ready
          ? `inset 0 1px 0 rgba(180,255,180,0.4), 0 0 8px rgba(74,230,106,0.4)`
          : 'inset 0 1px 2px rgba(0,0,0,0.5)',
      }}>
        <span style={{
          fontSize: ready ? 14 : 11, color: ready ? '#fef5e0' : t.t2,
          fontFamily:"'Lilita One',cursive",
          textShadow: ready ? '0 1px 2px rgba(0,0,0,0.5)' : 'none', lineHeight:1, letterSpacing:0.5,
        }}>{ready ? '✓ READY' : 'PICKING'}</span>
      </div>
    </div>
  );
};

// ─── LOBBY · PORTRAIT ─────────────────────────────────────
const CWLobbyV3 = () => {
  const t = CW_THEME;
  const players = _CW_DEMO_LOBBY;
  const filled = players.filter(p => !p.empty).length;
  const allReady = players.filter(p => !p.empty).every(p => p.ready);

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", overflow:'hidden',
      display:'flex', flexDirection:'column', position:'relative' }}>

      {/* Atmospheric glow */}
      <div style={{ position:'absolute', top:'-15%', left:'25%', right:'25%', height:'50%',
        background:'radial-gradient(ellipse, rgba(245,200,66,0.12), transparent 70%)', pointerEvents:'none' }} />

      {/* Top bar */}
      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between',
        padding:'10px 14px 6px', flexShrink:0 }}>
        <CWRibbon width={170}>MATCH LOBBY</CWRibbon>
      </div>

      {/* Join code panel */}
      <div style={{ margin:'4px 14px 6px', padding:'8px 12px', position:'relative',
        background: t.cardBg, border:`2px solid ${t.cardBorder}`, borderRadius:10,
        boxShadow: t.cardGlow, display:'flex', alignItems:'center', gap:10, flexShrink:0,
      }}>
        <div style={{ display:'flex', flexDirection:'column', gap:1 }}>
          <span style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:1 }}>
            INVITE CODE · TAP TO COPY
          </span>
          <div style={{
            display:'flex', alignItems:'center', gap:6, marginTop:2,
            fontFamily:"'Lilita One',cursive",
          }}>
            {'XK4P2M'.split('').map((ch, i) => (
              <span key={i} style={{
                fontSize:22, color:'#fef5e0',
                padding:'2px 7px',
                background:`linear-gradient(180deg, ${t.t3}, ${adjustColor(t.t3,-25)})`,
                color:'#1a0e04',
                borderRadius:5,
                boxShadow:'inset 0 1px 0 rgba(255,255,255,0.4), 0 2px 4px rgba(0,0,0,0.4)',
                border:`1px solid ${adjustColor(t.t3,-15)}`,
              }}>{ch}</span>
            ))}
          </div>
        </div>
        <div style={{ flex:1 }} />
        <div style={{
          padding:'6px 10px', borderRadius:8, cursor:'pointer',
          background:t.btnBg, border:`2px solid ${t.btnBorder}`, boxShadow:t.btnGlow,
          fontSize:10, color:'#fef5e0', fontFamily:"'Lilita One',cursive",
          textShadow:t.tShadow, letterSpacing:1, lineHeight:1,
        }}>SHARE</div>
      </div>

      {/* Status row */}
      <div style={{
        margin:'0 14px 6px', padding:'5px 10px', flexShrink:0,
        display:'flex', alignItems:'center', gap:8,
        background:'rgba(245,200,66,0.06)', border:'1px solid rgba(245,200,66,0.25)',
        borderRadius:6,
        fontFamily:"'Nunito',sans-serif", fontSize:9, color:t.t2, fontWeight:700,
      }}>
        <div style={{
          width:7, height:7, borderRadius:'50%', flexShrink:0,
          background: allReady ? '#4ae66a' : t.t3,
          boxShadow: `0 0 6px ${allReady ? '#4ae66a' : t.t3}aa`,
          animation: 'cwBlink 1.4s ease-in-out infinite',
        }} />
        <style>{`@keyframes cwBlink { 0%,100%{opacity:1} 50%{opacity:0.5} }`}</style>
        <span>PLAYERS</span>
        <span style={{ color:t.t1, fontWeight:800 }}>{filled} / 4</span>
        <div style={{ flex:1 }} />
        <span style={{ color: allReady ? '#7cd99a' : t.t3 }}>
          {filled < 4 ? 'Waiting for more players…' : allReady ? 'All ready — host can start' : 'Waiting for picks…'}
        </span>
      </div>

      {/* Player cards */}
      <div style={{ flex:1, padding:'4px 14px 6px', display:'flex', flexDirection:'column', gap:7, minHeight:0, overflow:'auto' }}>
        {players.map((pl, i) => <_LobbyCardP key={i} pl={pl} idx={i} />)}
      </div>

      {/* Match settings preview */}
      <div style={{
        margin:'0 14px 8px', padding:'7px 10px', flexShrink:0,
        background:t.cardBg, border:`1px solid ${t.cardBorder}`, borderRadius:8,
        display:'flex', alignItems:'center', gap:10,
        fontFamily:"'Nunito',sans-serif", fontSize:9, color:t.t2, fontWeight:700,
      }}>
        {[
          ['ARENA','Sunny Farm'],
          ['TIME','3:00'],
          ['GOAL', `${_CW_TARGET} food`],
        ].map(([k,v], i) => (
          <div key={i} style={{ display:'flex', flexDirection:'column', lineHeight:1.1, flex:1 }}>
            <span style={{ fontSize:7, color:t.t3, letterSpacing:1 }}>{k}</span>
            <span style={{ fontSize:10, color:t.t1, fontWeight:800 }}>{v}</span>
          </div>
        ))}
      </div>

      {/* Action button */}
      <div style={{ padding:'0 14px 12px', flexShrink:0 }}>
        <div style={{
          padding:'12px 0', borderRadius: t.btnR,
          background: allReady ? t.startBg : 'linear-gradient(180deg, #3a3a3a, #1a1a1a)',
          border:`2px solid ${allReady ? t.startBorder : '#0a0a0a'}`,
          boxShadow: allReady ? t.startGlow : 'inset 0 1px 0 rgba(255,255,255,0.05), 0 2px 4px rgba(0,0,0,0.4)',
          fontFamily:"'Lilita One',cursive", fontSize:18, color: allReady ? '#fef5e0' : '#666',
          textShadow: allReady ? t.tShadow : 'none', textAlign:'center', letterSpacing:3,
          cursor: allReady ? 'pointer' : 'not-allowed', opacity: allReady ? 1 : 0.7,
        }}>
          {allReady ? 'START MATCH ▶' : 'WAITING…'}
        </div>
      </div>
    </div>
  );
};

// ─── LOBBY · LANDSCAPE ────────────────────────────────────
const CWLobbyV3L = () => {
  const t = CW_THEME;
  const players = _CW_DEMO_LOBBY;
  const filled = players.filter(p => !p.empty).length;
  const allReady = players.filter(p => !p.empty).every(p => p.ready);

  // Compact lobby card for 2×2 grid
  const Card = ({ pl, idx }) => {
    const pc = CW_PLAYERS_V3[idx].color;
    if (pl.empty) {
      return (
        <div style={{
          display:'flex', alignItems:'center', justifyContent:'center', gap:8,
          borderRadius:10, border:`2px dashed ${t.panelBorder}`,
          background:'rgba(20,14,8,0.3)',
        }}>
          <div style={{ width:12, height:12, borderRadius:'50%', border:`2px dashed ${t.t2}`, opacity:0.4 }} />
          <span style={{ fontSize:10, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:1 }}>
            WAITING FOR P{idx+1}
          </span>
        </div>
      );
    }
    const cls = CW_CLASSES_V3[pl.cls];
    const ready = pl.ready;
    return (
      <div style={{
        display:'flex', alignItems:'center', gap:8, padding:'7px 9px', position:'relative',
        borderRadius:10, background: ready ? `linear-gradient(90deg, ${pc}22, ${pc}06)` : t.cardBg,
        border:`2px solid ${ready ? pc+'88' : pc + '44'}`,
        boxShadow: ready ? `0 0 8px ${pc}33, ${t.cardGlow}` : `${t.cardGlow}, inset 0 0 6px ${pc}10`,
      }}>
        <div style={{
          position:'absolute', left:0, top:0, bottom:0, width:4,
          background:`linear-gradient(180deg, ${adjustColor(pc,30)}, ${pc}, ${adjustColor(pc,-20)})`,
          borderRadius:'8px 0 0 8px',
        }} />
        <CWChicken classKey={pl.cls} size={44} showRing ringColor={pc} />
        <div style={{ flex:1, minWidth:0 }}>
          <div style={{ display:'flex', alignItems:'center', gap:4 }}>
            <span style={{ fontSize:12, color:'#fef5e0', textShadow:t.tShadow, lineHeight:1 }}>{pl.name}</span>
            <span style={{ fontSize:7, color:pc, fontFamily:"'Nunito',sans-serif", fontWeight:800 }}>P{idx+1}</span>
            {pl.host && (
              <span style={{
                fontSize:6, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:0.5,
                padding:'1px 4px', borderRadius:3,
                background:`linear-gradient(180deg, ${t.t3}, ${adjustColor(t.t3,-25)})`,
                color:'#1a0e04',
              }}>HOST</span>
            )}
          </div>
          <div style={{ fontSize:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, lineHeight:1.1, marginTop:1 }}>
            {cls.short.toUpperCase()} · <span style={{ color:t.t3 }}>{cls.passive}</span>
          </div>
          <div style={{ display:'flex', gap:3, marginTop:3 }}>
            {pl.abilities.map(id => <_AbilityChip key={id} abId={id} size={20} />)}
            {pl.abilities.length < cls.slots && Array.from({length: cls.slots - pl.abilities.length}).map((_,i) => (
              <div key={i} style={{ width:20, height:23, position:'relative' }}>
                <svg viewBox="0 0 100 115.5" width="100%" height="100%">
                  <polygon points="50,4 96,29.87 96,85.6 50,111.5 4,85.6 4,29.87"
                    fill="rgba(20,12,6,0.5)" stroke={t.panelBorder} strokeWidth="2" strokeDasharray="4 3" />
                </svg>
              </div>
            ))}
          </div>
        </div>
        <div style={{
          flexShrink:0, padding:'3px 8px', borderRadius:6, textAlign:'center',
          background: ready
            ? `linear-gradient(180deg, #5ac54f, #228b22)`
            : `linear-gradient(180deg, rgba(40,30,20,0.6), rgba(20,14,8,0.4))`,
          border: `1.5px solid ${ready ? '#4ae66a' : t.panelBorder}`,
          boxShadow: ready
            ? `inset 0 1px 0 rgba(180,255,180,0.4), 0 0 6px rgba(74,230,106,0.4)`
            : 'inset 0 1px 2px rgba(0,0,0,0.5)',
        }}>
          <span style={{
            fontSize: ready ? 12 : 9, color: ready ? '#fef5e0' : t.t2,
            fontFamily:"'Lilita One',cursive",
            textShadow: ready ? '0 1px 2px rgba(0,0,0,0.5)' : 'none', letterSpacing:0.5,
          }}>{ready ? '✓ READY' : 'PICKING'}</span>
        </div>
      </div>
    );
  };

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg,
      fontFamily:"'Lilita One',cursive", overflow:'hidden',
      display:'flex', position:'relative' }}>

      <div style={{ position:'absolute', top:'-20%', left:'-15%', width:'55%', height:'140%',
        background:'radial-gradient(ellipse, rgba(245,200,66,0.12), transparent 60%)', pointerEvents:'none' }} />

      {/* ─ LEFT: invite + settings ─ */}
      <div style={{ width:260, padding:'10px 12px', display:'flex', flexDirection:'column', gap:8,
        flexShrink:0, position:'relative', zIndex:2,
        borderRight:`1px solid ${t.panelBorder}44` }}>
        <CWRibbon width={200}>MATCH LOBBY</CWRibbon>

        {/* Invite code */}
        <div style={{ padding:'8px 10px',
          background:t.cardBg, border:`2px solid ${t.cardBorder}`, borderRadius:10,
          boxShadow:t.cardGlow,
        }}>
          <div style={{ fontSize:7, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:800, letterSpacing:1 }}>
            INVITE CODE · TAP TO COPY
          </div>
          <div style={{ display:'flex', gap:4, marginTop:4, flexWrap:'wrap' }}>
            {'XK4P2M'.split('').map((ch, i) => (
              <span key={i} style={{
                fontSize:16, padding:'2px 6px',
                background:`linear-gradient(180deg, ${t.t3}, ${adjustColor(t.t3,-25)})`,
                color:'#1a0e04', borderRadius:4,
                fontFamily:"'Lilita One',cursive",
                boxShadow:'inset 0 1px 0 rgba(255,255,255,0.4), 0 2px 4px rgba(0,0,0,0.4)',
                border:`1px solid ${adjustColor(t.t3,-15)}`,
              }}>{ch}</span>
            ))}
          </div>
          <div style={{ display:'flex', gap:5, marginTop:7 }}>
            <div style={{ flex:1, padding:'4px 0', borderRadius:6, textAlign:'center',
              background:t.btnBg, border:`1.5px solid ${t.btnBorder}`, boxShadow:t.btnGlow,
              fontSize:9, color:'#fef5e0', fontFamily:"'Lilita One',cursive", letterSpacing:1,
              cursor:'pointer' }}>SHARE</div>
            <div style={{ flex:1, padding:'4px 0', borderRadius:6, textAlign:'center',
              background:t.cardBg, border:`1.5px solid ${t.cardBorder}`, boxShadow:t.cardGlow,
              fontSize:9, color:t.t1, fontFamily:"'Lilita One',cursive", letterSpacing:1,
              cursor:'pointer' }}>COPY</div>
          </div>
        </div>

        {/* Settings */}
        <div style={{ padding:'8px 10px',
          background:t.cardBg, border:`2px solid ${t.cardBorder}`, borderRadius:10,
          boxShadow:t.cardGlow,
          display:'flex', flexDirection:'column', gap:4,
        }}>
          <div style={{ fontSize:10, color:t.t3, textShadow:t.tShadow, letterSpacing:1, marginBottom:2 }}>
            MATCH SETTINGS
          </div>
          {[
            ['ARENA','Sunny Farm'],
            ['TIME','3:00'],
            ['GOAL', `${_CW_TARGET} food`],
            ['MODE','Free-for-all'],
          ].map(([k,v], i) => (
            <div key={i} style={{ display:'flex', justifyContent:'space-between',
              fontFamily:"'Nunito',sans-serif", fontSize:9, lineHeight:1.2 }}>
              <span style={{ color:t.t2, fontWeight:700, letterSpacing:0.5 }}>{k}</span>
              <span style={{ color:t.t1, fontWeight:800 }}>{v}</span>
            </div>
          ))}
        </div>

        {/* Status */}
        <div style={{
          padding:'5px 8px',
          background:'rgba(245,200,66,0.06)', border:'1px solid rgba(245,200,66,0.25)',
          borderRadius:6,
          fontFamily:"'Nunito',sans-serif", fontSize:9, color:t.t2, fontWeight:700,
          display:'flex', alignItems:'center', gap:5,
        }}>
          <div style={{
            width:7, height:7, borderRadius:'50%',
            background: allReady ? '#4ae66a' : t.t3,
            boxShadow: `0 0 6px ${allReady ? '#4ae66a' : t.t3}aa`,
            animation: 'cwBlinkL 1.4s ease-in-out infinite',
          }} />
          <style>{`@keyframes cwBlinkL { 0%,100%{opacity:1} 50%{opacity:0.5} }`}</style>
          <span style={{ color:t.t1, fontWeight:800 }}>{filled}/4</span>
          <span style={{ color: allReady ? '#7cd99a' : t.t3, flex:1, textAlign:'right' }}>
            {filled < 4 ? 'Waiting…' : allReady ? 'All ready!' : 'Picking…'}
          </span>
        </div>

        <div style={{ flex:1 }} />

        {/* Start */}
        <div style={{
          padding:'10px 0', borderRadius: t.btnR,
          background: allReady ? t.startBg : 'linear-gradient(180deg, #3a3a3a, #1a1a1a)',
          border:`2px solid ${allReady ? t.startBorder : '#0a0a0a'}`,
          boxShadow: allReady ? t.startGlow : 'inset 0 1px 0 rgba(255,255,255,0.05), 0 2px 4px rgba(0,0,0,0.4)',
          fontFamily:"'Lilita One',cursive", fontSize:14, color: allReady ? '#fef5e0' : '#666',
          textShadow: allReady ? t.tShadow : 'none', textAlign:'center', letterSpacing:2,
          cursor: allReady ? 'pointer' : 'not-allowed', opacity: allReady ? 1 : 0.7,
        }}>{allReady ? 'START MATCH ▶' : 'WAITING…'}</div>
      </div>

      {/* ─ RIGHT: 2×2 player grid ─ */}
      <div style={{ flex:1, padding:'10px 12px', display:'flex', flexDirection:'column', gap:6, minWidth:0 }}>
        <div style={{ display:'flex', alignItems:'center', gap:6 }}>
          <span style={{ fontSize:11, color:t.t3, textShadow:t.tShadow, letterSpacing:1 }}>PLAYERS</span>
          <div style={{ flex:1, height:1, background:`linear-gradient(90deg, ${t.t3}66, transparent)` }} />
        </div>
        <div style={{ flex:1, display:'grid', gridTemplateColumns:'1fr 1fr', gridTemplateRows:'1fr 1fr', gap:7, minHeight:0 }}>
          {players.map((pl, i) => <Card key={i} pl={pl} idx={i} />)}
        </div>
      </div>
    </div>
  );
};

Object.assign(window, {
  CWMatchEndV3, CWMatchEndV3L,
  CWLobbyV3, CWLobbyV3L,
});
