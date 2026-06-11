// CluckWars UI v2 — Screen Components (Rich / Dimensional)
// =========================================================

// ─── CHARACTER SELECT ──────────────────────────────────────
const CWCharacterSelect = ({ platform = 'mobile' }) => {
  const t = CW_THEME;
  const [selected, setSelected] = React.useState('warrior');
  const cls = CW_CLASSES[selected];
  const isConsole = platform === 'console';
  const scale = isConsole ? 1.15 : 1;

  const equipped = cls.twoSlots
    ? [CW_ABILITIES[0], CW_ABILITIES[2]]
    : [CW_ABILITIES[0]];

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg, fontFamily:"'Lilita One',cursive", display:'flex', flexDirection:'column', overflow:'hidden', position:'relative' }}>
      <CWSvgDefs />

      {/* Decorative bg glow */}
      <div style={{ position:'absolute', top:'-20%', left:'30%', width:'40%', height:'60%', background:`radial-gradient(ellipse, ${cls.color}15, transparent 70%)`, pointerEvents:'none' }} />

      {/* Top ribbon */}
      <div style={{ padding: isConsole?'14px 0 8px':'10px 0 6px', display:'flex', justifyContent:'center' }}>
        <CWRibbon width={isConsole?320:260}>CHOOSE YOUR CHICKEN</CWRibbon>
      </div>

      {/* Body: cards | preview | detail */}
      <div style={{ flex:1, display:'flex', gap: isConsole?16:12, padding: isConsole?'0 24px 8px':'0 14px 6px', minHeight:0 }}>

        {/* Class cards */}
        <div style={{ display:'flex', flexDirection:'column', gap: isConsole?8:6, justifyContent:'center' }}>
          {CW_CLASS_KEYS.map(key => {
            const c = CW_CLASSES[key];
            const active = key === selected;
            return (
              <div key={key} onClick={() => setSelected(key)} style={{
                width: isConsole?135:118, padding: isConsole?'8px 6px':'6px 4px',
                background: active ? `linear-gradient(180deg, ${c.color}33, ${c.dark}22)` : t.cardBg,
                border: `2px solid ${active ? c.color : t.cardBorder}`,
                borderRadius: t.cardR, cursor:'pointer',
                boxShadow: active ? t.cardSelGlow : t.cardGlow,
                display:'flex', flexDirection:'column', alignItems:'center', gap:3,
                transition:'all .15s ease',
                transform: active ? 'scale(1.04)' : 'scale(1)',
              }}>
                <CWChicken classKey={key} size={isConsole?48:40} />
                <span style={{ fontSize: isConsole?13:11, color: active?t.t3:t.t1, textShadow:t.tShadow }}>{c.short}</span>
                <span style={{ fontSize: isConsole?9:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>{c.role}</span>
              </div>
            );
          })}
        </div>

        {/* Center: character showcase */}
        <div style={{ flex:1, display:'flex', flexDirection:'column', alignItems:'center', justifyContent:'center', gap:6, minWidth:0 }}>
          {/* Glowing platform */}
          <div style={{ position:'relative' }}>
            <div style={{ position:'absolute', inset:'-20%', borderRadius:'50%', background:`radial-gradient(circle, ${cls.color}20, transparent 70%)`, pointerEvents:'none' }} />
            <div style={{
              width: isConsole?180:150, height: isConsole?180:150, borderRadius:'50%',
              background:`radial-gradient(circle at 40% 35%, ${cls.light}22, ${cls.color}11, ${cls.dark}08)`,
              border:`3px solid ${cls.color}55`,
              boxShadow:`0 0 30px ${cls.color}22, inset 0 0 20px ${cls.color}11`,
              display:'flex', alignItems:'center', justifyContent:'center',
            }}>
              <CWChicken classKey={selected} size={isConsole?130:105} showRing ringColor={CW_PLAYER_COLORS[0]} />
            </div>
          </div>

          <span style={{ fontSize: isConsole?22:18, color:cls.color, textShadow:t.tGlow(cls.color), letterSpacing:1 }}>{cls.name}</span>
          <span style={{ fontSize: isConsole?11:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontStyle:'italic', fontWeight:400, textAlign:'center', maxWidth:200, textShadow:'0 1px 2px rgba(0,0,0,0.4)' }}>{cls.lore}</span>

          {/* Skin slots */}
          <div style={{ display:'flex', gap:6, marginTop:2 }}>
            {['Default','Pirate','Golden'].map((s,i) => (
              <div key={i} style={{
                width: isConsole?34:28, height: isConsole?34:28, borderRadius:8,
                background: i===0 ? `linear-gradient(135deg, ${cls.light}, ${cls.color}, ${cls.dark})` : `${cls.color}22`,
                border: i===0 ? `2px solid ${cls.color}` : `1.5px solid ${t.cardBorder}`,
                boxShadow: i===0 ? `inset 0 1px 0 rgba(255,255,255,0.3), 0 2px 4px rgba(0,0,0,0.3)` : 'none',
                cursor:'pointer', display:'flex', alignItems:'center', justifyContent:'center',
              }}>
                {i > 0 && <span style={{ fontSize:10, color:t.t2 }}>🔒</span>}
              </div>
            ))}
          </div>
        </div>

        {/* Right: stats + abilities panel */}
        <CWPanel width={isConsole?300:260} style={{ padding: isConsole?'14px 16px':'10px 12px', display:'flex', flexDirection:'column', gap: isConsole?8:6, alignSelf:'center' }}>
          {/* Stats header */}
          <div style={{ display:'flex', alignItems:'center', gap:6, borderBottom:`1px solid ${t.panelBorder}44`, paddingBottom:4 }}>
            <span style={{ fontSize: isConsole?14:12, color:t.t3, textShadow:t.tShadow }}>STATS</span>
            <div style={{ flex:1, height:1, background:`linear-gradient(90deg, ${t.t3}44, transparent)` }} />
          </div>
          {CW_STAT_ORDER.map(stat => (
            <CWStatBar key={stat} value={cls.stats[stat]} label={CW_STAT_LABELS[stat]} color={cls.color} />
          ))}

          {/* Abilities */}
          <div style={{ display:'flex', alignItems:'center', gap:6, borderBottom:`1px solid ${t.panelBorder}44`, paddingBottom:4, marginTop:4 }}>
            <span style={{ fontSize: isConsole?14:12, color:t.t3, textShadow:t.tShadow }}>{cls.twoSlots ? 'ABILITIES ×2' : 'ABILITY'}</span>
            <div style={{ flex:1, height:1, background:`linear-gradient(90deg, ${t.t3}44, transparent)` }} />
          </div>
          {equipped.map((ab, i) => (
            <div key={i} style={{ display:'flex', alignItems:'center', gap:8 }}>
              <CWHex size={isConsole?44:38} color={ab.color} icon={ab.icon} label={ab.short} />
              <div>
                <div style={{ fontSize: isConsole?12:10, color:t.t1, textShadow:t.tShadow }}>{ab.name}</div>
                <div style={{ fontSize: isConsole?9:8, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>{ab.type}</div>
              </div>
            </div>
          ))}
        </CWPanel>
      </div>

      {/* Bottom bar: session */}
      <div style={{
        display:'flex', alignItems:'center', justifyContent:'space-between',
        padding: isConsole?'8px 24px 14px':'6px 14px 10px',
        background:'linear-gradient(0deg, rgba(10,6,2,0.6), transparent)',
        borderTop:`1px solid ${t.panelBorder}44`,
      }}>
        <div style={{ display:'flex', gap:8 }}>
          {['Solo','Host','Join'].map((m,i) => (
            <CWButton key={m} bg={i===0 ? t.btnBg : t.cardBg} border={i===0 ? t.btnBorder : t.cardBorder} glow={i===0 ? t.btnGlow : t.cardGlow} style={{ fontSize:isConsole?14:12, padding:isConsole?'7px 18px':'5px 14px' }}>{m}</CWButton>
          ))}
        </div>
        <CWButton bg={t.startBg} border={t.startBorder} glow={t.startGlow} style={{ fontSize:isConsole?18:15, padding:isConsole?'8px 32px':'6px 24px', letterSpacing:2 }}>START MATCH</CWButton>
      </div>

      {/* Console hints */}
      {isConsole && (
        <div style={{ position:'absolute', bottom:12, left:24, display:'flex', gap:12, opacity:0.6 }}>
          {[{b:'←→',l:'Select'},{b:'A',l:'Confirm'},{b:'LB/RB',l:'Tab'}].map((h,i) => (
            <div key={i} style={{ display:'flex', alignItems:'center', gap:4 }}>
              <div style={{ padding:'2px 6px', borderRadius:4, background:'rgba(255,255,255,0.1)', border:'1px solid rgba(255,255,255,0.2)', fontSize:10, color:'#fef5e0', fontWeight:700, fontFamily:"'Nunito',sans-serif" }}>{h.b}</div>
              <span style={{ fontSize:10, color:'#fef5e088', fontFamily:"'Nunito',sans-serif" }}>{h.l}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

// ─── MATCH HUD ─────────────────────────────────────────────
const CWMatchHUD = ({ platform = 'mobile' }) => {
  const t = CW_THEME;
  const isConsole = platform === 'console';

  // Scores sorted for leaderboard (highest first)
  const playerData = [
    { p:0, cls:'warrior',  score:42 },
    { p:2, cls:'fatty',    score:28 },
    { p:3, cls:'assassin', score:31 },
    { p:1, cls:'speedy',   score:17 },
  ];
  const ranked = [...playerData].sort((a,b) => b.score - a.score);
  const posLabels = ['1st','2nd','3rd','4th'];
  const isMe = (p) => p === 0; // P1 is "you"
  const targetFood = 150;

  // Isometric grid bg lines
  const gridLines = [];
  for (let i = 0; i < 14; i++) {
    gridLines.push(
      <line key={`a${i}`} x1={i*90} y1="0" x2={i*90+280} y2="540" stroke="rgba(80,120,40,0.06)" strokeWidth="1" />,
      <line key={`b${i}`} x1={i*90+600} y1="0" x2={i*90-280} y2="540" stroke="rgba(80,120,40,0.06)" strokeWidth="1" />
    );
  }

  const rowH = isConsole ? 30 : 24;
  const lbW = isConsole ? 170 : 140;

  return (
    <div style={{ width:'100%', height:'100%', background:t.mapBg, fontFamily:"'Lilita One',cursive", position:'relative', overflow:'hidden' }}>
      <svg style={{ position:'absolute', inset:0, width:'100%', height:'100%' }}>{gridLines}</svg>

      {/* Food piles — decorative */}
      <div style={{ position:'absolute', left:'50%', top:'40%', transform:'translate(-50%,-50%)' }}>
        <div style={{ width:50, height:35, borderRadius:'50%', background:'radial-gradient(circle at 40% 35%, #ffe066, #f5c842, #d4a020)', boxShadow:'0 0 30px rgba(245,200,66,0.3), 0 4px 8px rgba(0,0,0,0.3)', margin:'0 auto' }} />
        <div style={{ width:30, height:4, borderRadius:2, background:'rgba(0,0,0,0.2)', margin:'4px auto 0' }} />
      </div>
      {/* Small piles */}
      {[{x:'22%',y:'55%',s:20},{x:'78%',y:'48%',s:22},{x:'35%',y:'25%',s:18},{x:'65%',y:'70%',s:16}].map((p,i) => (
        <div key={i} style={{ position:'absolute', left:p.x, top:p.y }}>
          <div style={{ width:p.s, height:p.s*0.65, borderRadius:'50%', background:'radial-gradient(circle, #e8b830, #b08018)', boxShadow:'0 0 10px rgba(200,160,40,0.15)' }} />
        </div>
      ))}

      {/* Player chicken (center) with on-character bars */}
      <div style={{ position:'absolute', left:'50%', top:'55%', transform:'translate(-50%,-50%)', display:'flex', flexDirection:'column', alignItems:'center' }}>
        <CWChicken classKey="warrior" size={isConsole?90:72} showRing ringColor={CW_PLAYER_COLORS[0]} />
        {/* HP bar */}
        <div style={{ width: isConsole?64:52, height:7, background:'#0a0604', borderRadius:4, marginTop:-6, overflow:'hidden', border:'1.5px solid #4a2818', boxShadow:'inset 0 1px 2px rgba(0,0,0,0.6), 0 1px 2px rgba(0,0,0,0.3)' }}>
          <div style={{ width:'72%', height:'100%', background:t.hp, borderRadius:3, boxShadow:'inset 0 1px 0 rgba(255,200,200,0.3)' }} />
        </div>
        {/* Cargo bar */}
        <div style={{ width: isConsole?64:52, height:5, background:'#0a0604', borderRadius:3, marginTop:2, overflow:'hidden', border:'1px solid #4a3018', boxShadow:'inset 0 1px 2px rgba(0,0,0,0.6)' }}>
          <div style={{ width:'45%', height:'100%', background:t.cargo, borderRadius:2, boxShadow:'inset 0 1px 0 rgba(255,240,180,0.3)' }} />
        </div>
        <div style={{ display:'flex', alignItems:'center', gap:3, marginTop:2 }}>
          <CWFoodIcon size={10} />
          <span style={{ fontSize:10, color:'#fef5e0', fontFamily:"'Nunito',sans-serif", fontWeight:700, textShadow:'0 1px 3px rgba(0,0,0,0.8)' }}>9/20</span>
        </div>
      </div>

      {/* Enemy chickens */}
      {[{x:'28%',y:'32%',cls:'speedy',p:1},{x:'72%',y:'28%',cls:'fatty',p:2},{x:'70%',y:'62%',cls:'assassin',p:3}].map((e,i) => (
        <div key={i} style={{ position:'absolute', left:e.x, top:e.y, display:'flex', flexDirection:'column', alignItems:'center', transform:'scale(0.65)', opacity:0.85 }}>
          <CWChicken classKey={e.cls} size={52} showRing ringColor={CW_PLAYER_COLORS[e.p]} />
          <div style={{ width:40, height:5, background:'#0a0604', borderRadius:3, marginTop:-4, overflow:'hidden', border:'1px solid #4a2818' }}>
            <div style={{ width:`${50+i*15}%`, height:'100%', background:t.hp, borderRadius:2 }} />
          </div>
        </div>
      ))}

      {/* ═══ TOP-LEFT: Race-style ranked leaderboard ═══ */}
      <div style={{
        position:'absolute', top: isConsole?8:6, left: isConsole?12:8, zIndex:10,
        width: lbW,
        background:'linear-gradient(180deg, rgba(30,20,10,0.85), rgba(20,14,8,0.75))',
        border:`2px solid ${t.panelBorder}88`,
        borderRadius:10,
        boxShadow:'inset 0 1px 0 rgba(255,200,100,0.12), 0 3px 10px rgba(0,0,0,0.5)',
        overflow:'hidden',
      }}>
        {ranked.map((r, i) => {
          const pc = CW_PLAYER_COLORS[r.p];
          const me = isMe(r.p);
          const pct = Math.min(100, (r.score / targetFood) * 100);
          return (
            <div key={r.p} style={{
              display:'flex', alignItems:'center', gap: isConsole?6:4,
              height: rowH, padding: isConsole?'0 10px':'0 7px',
              background: me
                ? `linear-gradient(90deg, ${pc}30, ${pc}10)`
                : i === 0
                  ? 'rgba(245,200,66,0.08)'
                  : 'transparent',
              borderBottom: i < 3 ? '1px solid rgba(138,106,58,0.25)' : 'none',
              borderLeft: me ? `3px solid ${pc}` : '3px solid transparent',
            }}>
              {/* Position number */}
              <span style={{
                fontSize: isConsole?11:9, color: i===0 ? t.t3 : t.t2,
                fontWeight:700, width: isConsole?22:18, textAlign:'center',
                textShadow: i===0 ? `0 0 6px ${t.t3}66` : t.tShadow,
                fontFamily:"'Nunito',sans-serif",
              }}>{posLabels[i]}</span>

              {/* Player dot */}
              <div style={{
                width: isConsole?10:8, height: isConsole?10:8, borderRadius:'50%', flexShrink:0,
                background:`radial-gradient(circle at 35% 35%, ${adjustColor(pc,50)}, ${pc})`,
                boxShadow:`0 0 4px ${pc}88`,
              }} />

              {/* Player label */}
              <span style={{
                fontSize: isConsole?12:10, color: me ? '#fef5e0' : pc,
                fontWeight: me ? 800 : 400, width: isConsole?24:20,
                textShadow: me ? `0 0 6px ${pc}66` : 'none',
                fontFamily:"'Nunito',sans-serif",
              }}>P{r.p+1}</span>

              {/* Score bar + number */}
              <div style={{ flex:1, display:'flex', alignItems:'center', gap: isConsole?5:3 }}>
                <div style={{ flex:1, height: isConsole?8:6, background:'#0a0604', borderRadius:4, overflow:'hidden', border:'1px solid #3a2816' }}>
                  <div style={{
                    width:`${pct}%`, height:'100%', borderRadius:3,
                    background:`linear-gradient(90deg, ${adjustColor(pc,20)}, ${pc})`,
                    boxShadow:`inset 0 1px 0 rgba(255,255,255,0.2)`,
                    transition:'width 0.5s ease',
                  }} />
                </div>
                <span style={{
                  fontSize: isConsole?11:9, color:t.t1, width: isConsole?26:22, textAlign:'right',
                  fontFamily:"'Nunito',sans-serif", fontWeight:700,
                  textShadow:t.tShadow,
                }}>{r.score}</span>
              </div>
            </div>
          );
        })}
      </div>

      {/* ═══ TOP-RIGHT: Timer ═══ */}
      <div style={{ position:'absolute', top: isConsole?8:6, right: isConsole?12:8, zIndex:10 }}>
        <CWTimer time="02:34" />
      </div>

      {/* MOBILE: MOBA-style controls */}
      {!isConsole && <>
        {/* Joystick */}
        <div style={{ position:'absolute', bottom:20, left:28 }}>
          <div style={{ width:135, height:135, borderRadius:'50%', background:'radial-gradient(circle, rgba(255,255,255,0.06), rgba(255,255,255,0.02))', border:'2.5px solid rgba(255,255,255,0.12)', boxShadow:'inset 0 0 10px rgba(0,0,0,0.3), 0 2px 8px rgba(0,0,0,0.3)', display:'flex', alignItems:'center', justifyContent:'center' }}>
            <div style={{ width:55, height:55, borderRadius:'50%', background:'radial-gradient(circle at 40% 35%, rgba(255,255,255,0.25), rgba(255,255,255,0.08))', border:'2px solid rgba(255,255,255,0.2)', boxShadow:'inset 0 1px 0 rgba(255,255,255,0.3), 0 2px 4px rgba(0,0,0,0.3)', transform:'translate(8px,-4px)' }} />
          </div>
        </div>

        {/* MOBA right cluster: attack right, abilities arc to its left */}
        <div style={{ position:'absolute', bottom:0, right:0, width:280, height:220 }}>
          {/* Ability 1 — arc position: left of attack, slightly up */}
          <div style={{ position:'absolute', bottom:68, right:165 }}>
            <CWHex size={58} color={CW_ABILITIES[0].color} icon={CW_ABILITIES[0].icon} label={CW_ABILITIES[0].short} cooldownPct={0} />
          </div>
          {/* Ability 2 — arc position: above-left of attack */}
          <div style={{ position:'absolute', bottom:148, right:115 }}>
            <CWHex size={58} color={CW_ABILITIES[2].color} icon={CW_ABILITIES[2].icon} label={CW_ABILITIES[2].short} cooldownPct={0.35} />
          </div>
          {/* Attack button — large, bottom-right anchor */}
          <div style={{
            position:'absolute', bottom:18, right:22,
            width:118, height:118, borderRadius:'50%',
            background:`radial-gradient(circle at 40% 35%, ${adjustColor(t.hpFlat,40)}, ${t.hpFlat}, ${adjustColor(t.hpFlat,-30)})`,
            border:`3px solid ${adjustColor(t.hpFlat,-20)}`,
            boxShadow:`inset 0 3px 0 rgba(255,200,180,0.4), inset 0 -3px 0 rgba(0,0,0,0.3), 0 0 20px ${t.hpFlat}44, 0 4px 10px rgba(0,0,0,0.4)`,
            display:'flex', flexDirection:'column', alignItems:'center', justifyContent:'center',
          }}>
            <span style={{ fontSize:14, color:'#fef5e0', textShadow:'0 2px 4px rgba(0,0,0,0.6)', letterSpacing:1 }}>ATTACK</span>
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#fef5e0" strokeWidth="2.5" strokeLinecap="round" style={{ marginTop:2, filter:'drop-shadow(0 1px 2px rgba(0,0,0,0.5))' }}>
              <path d="M14 2L8 12h5l-1 10 6-10h-5l1-10z" fill="#fef5e088" />
            </svg>
          </div>
        </div>
      </>}

      {/* CONSOLE: gamepad hints */}
      {isConsole && (
        <div style={{ position:'absolute', bottom:14, right:20, display:'flex', gap:14, opacity:0.7 }}>
          {[{btn:'A',label:'Attack',c:t.hpFlat},{btn:'X',label:'Speed Burst',c:'#00BCD4'},{btn:'Y',label:'Roll & Trample',c:'#FF5722'}].map((b,i) => (
            <div key={i} style={{ display:'flex', alignItems:'center', gap:5 }}>
              <div style={{
                width:30, height:30, borderRadius:8,
                background:`linear-gradient(180deg, ${adjustColor(b.c,30)}, ${b.c}, ${adjustColor(b.c,-30)})`,
                border:`2px solid ${adjustColor(b.c,-20)}`,
                boxShadow:`inset 0 1px 0 rgba(255,255,255,0.4), 0 2px 4px rgba(0,0,0,0.4)`,
                display:'flex', alignItems:'center', justifyContent:'center',
                fontSize:14, color:'#fef5e0', fontWeight:700, fontFamily:"'Nunito',sans-serif",
                textShadow:'0 1px 2px rgba(0,0,0,0.5)',
              }}>{b.btn}</div>
              <span style={{ fontSize:11, color:'#fef5e0aa', fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>{b.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

// ─── MATCH END ─────────────────────────────────────────────
const CWMatchEnd = () => {
  const t = CW_THEME;
  const results = [ {p:0,score:152}, {p:2,score:128}, {p:3,score:87}, {p:1,score:43} ];
  const medals = ['🥇','🥈','🥉',''];
  const maxScore = 152;

  return (
    <div style={{ width:'100%', height:'100%', background:t.mapBg, fontFamily:"'Lilita One',cursive", position:'relative', overflow:'hidden', display:'flex', alignItems:'center', justifyContent:'center' }}>
      <div style={{ position:'absolute', inset:0, background:t.overlayBg }} />

      {/* Celebratory particles */}
      <div style={{ position:'absolute', inset:0, overflow:'hidden', pointerEvents:'none' }}>
        {Array.from({length:12}).map((_,i) => (
          <div key={i} style={{
            position:'absolute',
            left:`${10+Math.random()*80}%`, top:`${Math.random()*80}%`,
            width:4+Math.random()*6, height:4+Math.random()*6,
            borderRadius:'50%',
            background: [CW_PLAYER_COLORS[0],'#f5c842','#fef5e0',CW_PLAYER_COLORS[2]][i%4],
            opacity:0.3+Math.random()*0.4,
          }} />
        ))}
      </div>

      <CWPanel width={440} style={{ padding:'20px 24px', display:'flex', flexDirection:'column', alignItems:'center', gap:14, position:'relative', zIndex:2 }}>
        {/* Winner crown */}
        <div style={{ fontSize:36, filter:'drop-shadow(0 2px 6px rgba(245,200,66,0.4))' }}>👑</div>

        {/* Winner banner */}
        <CWRibbon color={CW_PLAYER_COLORS[results[0].p]} width={300}>
          PLAYER {results[0].p + 1} WINS!
        </CWRibbon>

        {/* Winner chicken */}
        <CWChicken classKey="warrior" size={60} showRing ringColor={CW_PLAYER_COLORS[results[0].p]} />

        {/* Leaderboard */}
        <div style={{ width:'100%', display:'flex', flexDirection:'column', gap:6 }}>
          {results.map((r,i) => {
            const pc = CW_PLAYER_COLORS[r.p];
            return (
              <div key={i} style={{
                display:'flex', alignItems:'center', gap:8, padding:'5px 10px',
                borderRadius:8, background: i===0 ? `linear-gradient(90deg, ${pc}22, transparent)` : 'transparent',
                border: i===0 ? `1px solid ${pc}44` : 'none',
              }}>
                <span style={{ fontSize:18, width:26, textAlign:'center' }}>{medals[i]}</span>
                <div style={{ width:12, height:12, borderRadius:'50%', background:`radial-gradient(circle at 35% 35%, ${adjustColor(pc,40)}, ${pc})`, boxShadow:`0 0 4px ${pc}66`, flexShrink:0 }} />
                <span style={{ fontSize:15, color:pc, width:36, textShadow:`0 0 6px ${pc}44` }}>P{r.p+1}</span>
                <div style={{ flex:1, height:16, background:t.barBg, borderRadius:8, overflow:'hidden', border:t.barBorder, boxShadow:t.barGlow }}>
                  <div style={{ width:`${(r.score/maxScore)*100}%`, height:'100%', background:`linear-gradient(90deg, ${adjustColor(pc,20)}, ${pc})`, borderRadius:8, boxShadow:`inset 0 1px 0 rgba(255,255,255,0.25)` }} />
                </div>
                <div style={{ display:'flex', alignItems:'center', gap:3 }}>
                  <CWFoodIcon size={12} />
                  <span style={{ fontSize:14, color:t.t1, width:36, textAlign:'right', textShadow:t.tShadow }}>{r.score}</span>
                </div>
              </div>
            );
          })}
        </div>

        <span style={{ fontSize:13, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600, textShadow:t.tShadow }}>
          Next match in 5s…
        </span>
      </CWPanel>
    </div>
  );
};

// ─── LOBBY ─────────────────────────────────────────────────
const CWLobby = () => {
  const t = CW_THEME;
  const players = [
    { p:0, cls:'warrior', name:'P1 (You)', ready:true },
    { p:1, cls:'speedy', name:'P2', ready:true },
    { p:2, cls:'fatty', name:'P3', ready:false },
  ];

  return (
    <div style={{ width:'100%', height:'100%', background:t.screenBg, fontFamily:"'Lilita One',cursive", display:'flex', alignItems:'center', justifyContent:'center' }}>
      <CWPanel width={480} style={{ padding:'18px 22px', display:'flex', flexDirection:'column', gap:12 }}>
        <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between' }}>
          <CWRibbon width={200}>MATCH LOBBY</CWRibbon>
          <div style={{
            padding:'4px 14px', borderRadius:8,
            background:'linear-gradient(180deg, #3a8a3a, #228b22)',
            border:'2px solid #4ae66a',
            boxShadow:'inset 0 1px 0 rgba(180,255,180,0.4), 0 0 10px rgba(74,230,106,0.3)',
            fontSize:18, color:'#fef5e0', letterSpacing:3, fontFamily:"'Nunito',sans-serif", fontWeight:800,
            textShadow:'0 1px 3px rgba(0,0,0,0.5)',
          }}>
            XK4P2M
          </div>
        </div>

        <span style={{ fontSize:12, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600, textShadow:t.tShadow }}>
          Players: 3 / 4 — Waiting for more players…
        </span>

        {/* Player list */}
        <div style={{ display:'flex', flexDirection:'column', gap:6 }}>
          {players.map((pl,i) => (
            <div key={i} style={{
              display:'flex', alignItems:'center', gap:10, padding:'8px 12px',
              borderRadius:10, background:t.cardBg,
              border:`2px solid ${CW_PLAYER_COLORS[pl.p]}55`,
              boxShadow:`inset 0 0 8px ${CW_PLAYER_COLORS[pl.p]}11, ${t.cardGlow}`,
            }}>
              <div style={{ width:14, height:14, borderRadius:'50%', background:`radial-gradient(circle at 35% 35%, ${adjustColor(CW_PLAYER_COLORS[pl.p],40)}, ${CW_PLAYER_COLORS[pl.p]})`, boxShadow:`0 0 6px ${CW_PLAYER_COLORS[pl.p]}66` }} />
              <CWChicken classKey={pl.cls} size={34} />
              <div style={{ flex:1 }}>
                <div style={{ fontSize:13, color:t.t1, textShadow:t.tShadow }}>{pl.name}</div>
                <div style={{ fontSize:9, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>{CW_CLASSES[pl.cls].short} — {CW_CLASSES[pl.cls].role}</div>
              </div>
              <span style={{ fontSize:11, color: pl.ready ? '#4ae66a' : t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:700, textShadow: pl.ready ? '0 0 6px rgba(74,230,106,0.4)' : 'none' }}>
                {pl.ready ? '✓ Ready' : 'Picking…'}
              </span>
            </div>
          ))}

          {/* Empty slot */}
          <div style={{
            display:'flex', alignItems:'center', justifyContent:'center',
            padding:'14px', borderRadius:10, border:`2px dashed ${t.cardBorder}`,
            background:'rgba(20,14,8,0.3)',
          }}>
            <span style={{ fontSize:12, color:t.t2, fontFamily:"'Nunito',sans-serif", fontWeight:600 }}>Waiting for Player 4…</span>
          </div>
        </div>

        <CWButton bg={t.startBg} border={t.startBorder} glow={t.startGlow} style={{ fontSize:17, padding:'10px 0', letterSpacing:2 }}>
          START MATCH
        </CWButton>
      </CWPanel>
    </div>
  );
};

// ─── INTRO COUNTDOWN ───────────────────────────────────────
const CWIntroCountdown = ({ number = 3 }) => {
  const t = CW_THEME;
  return (
    <div style={{ width:'100%', height:'100%', background:t.mapBg, fontFamily:"'Lilita One',cursive", position:'relative', overflow:'hidden', display:'flex', alignItems:'center', justifyContent:'center' }}>
      <div style={{ position:'absolute', inset:0, background:'rgba(0,0,0,0.45)' }} />

      {/* Radiating glow */}
      <div style={{ position:'absolute', width:300, height:300, borderRadius:'50%', background:`radial-gradient(circle, ${t.t3}33, transparent 70%)`, pointerEvents:'none' }} />

      <div style={{ position:'relative', zIndex:2, display:'flex', flexDirection:'column', alignItems:'center', gap:10 }}>
        <span style={{
          fontSize:180, color:t.t3, lineHeight:1,
          textShadow:`0 0 60px ${t.t3}88, 0 0 30px ${t.t3}66, 0 6px 16px rgba(0,0,0,0.6)`,
          filter:'drop-shadow(0 0 20px rgba(245,200,66,0.4))',
        }}>
          {number}
        </span>
        <CWRibbon width={220} color="#8a6a3a">GET READY</CWRibbon>
      </div>

      {/* Top bar (faded) */}
      <div style={{ position:'absolute', top:0, left:0, right:0, display:'flex', alignItems:'center', justifyContent:'center', padding:'6px 12px', gap:8, opacity:0.4 }}>
        {[0,1,2,3].map(i => <CWPlayerBadge key={i} index={i} score={0} small />)}
        <CWTimer time="03:00" />
      </div>
    </div>
  );
};

// ─── TOUCH CONTROLS REFERENCE ──────────────────────────────
const CWTouchLayout = () => {
  const t = CW_THEME;
  return (
    <div style={{ width:'100%', height:'100%', background:'#060402', fontFamily:"'Lilita One',cursive", position:'relative', display:'flex', alignItems:'center', justifyContent:'center' }}>
      {/* Phone frame */}
      <div style={{ width:'94%', height:'82%', border:'2.5px solid #3a2816', borderRadius:22, position:'relative', overflow:'hidden', background:t.mapBg, boxShadow:'inset 0 0 20px rgba(0,0,0,0.3)' }}>

        {/* Thumb zone guides */}
        <div style={{ position:'absolute', left:0, bottom:0, width:'38%', height:'60%', background:`linear-gradient(135deg, ${CW_PLAYER_COLORS[0]}06, transparent)`, borderTopRightRadius:80 }}>
          <span style={{ position:'absolute', top:8, left:10, fontSize:9, color:'rgba(255,200,100,0.3)', fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:1 }}>LEFT THUMB</span>
        </div>
        <div style={{ position:'absolute', right:0, bottom:0, width:'38%', height:'60%', background:`linear-gradient(225deg, rgba(200,50,50,0.04), transparent)`, borderTopLeftRadius:80 }}>
          <span style={{ position:'absolute', top:8, right:10, fontSize:9, color:'rgba(255,200,100,0.3)', fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:1 }}>RIGHT THUMB</span>
        </div>

        {/* Joystick */}
        <div style={{ position:'absolute', bottom:22, left:28 }}>
          <div style={{ width:120, height:120, borderRadius:'50%', background:'radial-gradient(circle, rgba(255,255,255,0.06), rgba(255,255,255,0.02))', border:'2.5px solid rgba(255,255,255,0.12)', boxShadow:'inset 0 0 10px rgba(0,0,0,0.3)', display:'flex', alignItems:'center', justifyContent:'center' }}>
            <div style={{ width:48, height:48, borderRadius:'50%', background:'radial-gradient(circle at 40% 35%, rgba(255,255,255,0.25), rgba(255,255,255,0.08))', border:'2px solid rgba(255,255,255,0.2)', boxShadow:'inset 0 1px 0 rgba(255,255,255,0.3)' }} />
          </div>
          <span style={{ display:'block', textAlign:'center', fontSize:9, color:'rgba(255,200,100,0.5)', marginTop:4, fontFamily:"'Nunito',sans-serif", fontWeight:700, letterSpacing:1 }}>MOVE</span>
        </div>

        {/* MOBA cluster */}
        <div style={{ position:'absolute', bottom:0, right:0, width:260, height:200 }}>
          {/* Ability 1 — arc left of attack */}
          <div style={{ position:'absolute', bottom:58, right:148 }}>
            <CWHex size={48} color={CW_ABILITIES[0].color} icon={CW_ABILITIES[0].icon} label={CW_ABILITIES[0].short} />
            <span style={{ display:'block', textAlign:'center', fontSize:7, color:'rgba(255,200,100,0.4)', marginTop:2, fontFamily:"'Nunito',sans-serif", fontWeight:700 }}>ABILITY 1</span>
          </div>
          {/* Ability 2 — arc above-left of attack */}
          <div style={{ position:'absolute', bottom:130, right:102 }}>
            <CWHex size={48} color={CW_ABILITIES[2].color} icon={CW_ABILITIES[2].icon} label={CW_ABILITIES[2].short} cooldownPct={0.4} />
            <span style={{ display:'block', textAlign:'center', fontSize:7, color:'rgba(255,200,100,0.4)', marginTop:2, fontFamily:"'Nunito',sans-serif", fontWeight:700 }}>ABILITY 2</span>
          </div>
          {/* Attack */}
          <div style={{
            position:'absolute', bottom:14, right:16,
            width:100, height:100, borderRadius:'50%',
            background:`radial-gradient(circle at 40% 35%, ${adjustColor(t.hpFlat,40)}, ${t.hpFlat}, ${adjustColor(t.hpFlat,-30)})`,
            border:`3px solid ${adjustColor(t.hpFlat,-20)}`,
            boxShadow:`inset 0 3px 0 rgba(255,200,180,0.4), inset 0 -3px 0 rgba(0,0,0,0.3), 0 0 16px ${t.hpFlat}33, 0 4px 8px rgba(0,0,0,0.4)`,
            display:'flex', flexDirection:'column', alignItems:'center', justifyContent:'center',
          }}>
            <span style={{ fontSize:12, color:'#fef5e0', textShadow:'0 2px 3px rgba(0,0,0,0.6)', letterSpacing:1 }}>ATTACK</span>
            <span style={{ fontSize:7, color:'#fef5e088', fontFamily:"'Nunito',sans-serif", marginTop:1 }}>MASH</span>
          </div>
        </div>

        {/* Top bar */}
        <div style={{ position:'absolute', top:0, left:0, right:0, display:'flex', alignItems:'center', justifyContent:'center', padding:'4px 8px', gap:6, background:t.hudGrad }}>
          {[0,1,2,3].map(i => <CWPlayerBadge key={i} index={i} score={0} small />)}
          <CWTimer time="03:00" />
        </div>
      </div>
    </div>
  );
};

Object.assign(window, {
  CWCharacterSelect, CWMatchHUD, CWMatchEnd, CWLobby, CWIntroCountdown, CWTouchLayout,
});
