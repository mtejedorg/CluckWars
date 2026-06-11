// CluckWars UI — Screen Components
// ==================================

const {
  CW_PLAYER_COLORS, CW_PLAYER_NAMES, CW_CLASSES, CW_CLASS_KEYS,
  CW_STAT_LABELS, CW_STAT_ORDER, CW_ABILITIES, CW_THEMES,
  CWHex, CWStatBar, CWPlayerBadge, CWChicken, CWCooldownHex, CWSvgDefs, CWTimer,
} = window;

// ─── CHARACTER SELECT ──────────────────────────────────────
const CWCharacterSelect = ({ theme = 'barnyard', platform = 'mobile' }) => {
  const t = CW_THEMES[theme];
  const [selected, setSelected] = React.useState('warrior');
  const cls = CW_CLASSES[selected];
  const isConsole = platform === 'console';
  const cardW = isConsole ? 130 : 115;
  const detailW = isConsole ? 340 : 300;

  const charSelectStyles = {
    root: { width: '100%', height: '100%', background: t.screenBg, fontFamily: "'Lilita One',cursive", display: 'flex', flexDirection: 'column', overflow: 'hidden', position: 'relative' },
    topBar: { display: 'flex', alignItems: 'center', justifyContent: 'center', padding: isConsole ? '14px 24px 8px' : '10px 20px 6px', gap: 12 },
    title: { fontSize: isConsole ? 32 : 26, color: t.t3, letterSpacing: 2, textShadow: '0 2px 6px rgba(0,0,0,0.5)' },
    body: { flex: 1, display: 'flex', gap: isConsole ? 20 : 14, padding: isConsole ? '0 28px 12px' : '0 16px 10px', minHeight: 0 },
    classGrid: { display: 'flex', flexDirection: 'column', gap: isConsole ? 10 : 8, justifyContent: 'center' },
    card: (key) => ({
      width: cardW, padding: isConsole ? '10px 8px' : '8px 6px',
      background: t.card, border: key === selected ? t.cardSelBdr : t.cardBdr,
      borderRadius: t.cardR, cursor: 'pointer',
      boxShadow: key === selected ? `0 0 12px ${cls.color}44` : 'none',
      display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4,
      transition: 'all .15s ease',
    }),
    detail: {
      width: detailW, background: t.panel, border: t.panelBdr, borderRadius: t.panelR,
      boxShadow: t.panelShd, padding: isConsole ? '16px 18px' : '12px 14px',
      display: 'flex', flexDirection: 'column', gap: isConsole ? 10 : 7,
      overflow: 'hidden',
    },
    previewArea: {
      flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center',
      justifyContent: 'center', gap: 8, minWidth: 0,
    },
    previewFrame: {
      width: isConsole ? 200 : 160, height: isConsole ? 200 : 160,
      borderRadius: '50%', background: `radial-gradient(circle, ${cls.color}22, ${cls.dark}11)`,
      border: `3px solid ${cls.color}44`, display: 'flex', alignItems: 'center',
      justifyContent: 'center', flexShrink: 0,
    },
    abilityRow: {
      display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'center',
      padding: '6px 0',
    },
    bottomBar: {
      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
      padding: isConsole ? '10px 28px 16px' : '8px 16px 12px',
      background: t.hudBarBg, borderTop: t.panelBdr,
    },
    modeBtn: (active) => ({
      padding: isConsole ? '8px 20px' : '6px 14px',
      background: active ? t.btn : 'transparent',
      border: active ? t.btnBdr : `1px solid ${t.t2}44`,
      borderRadius: t.btnR, color: active ? '#fef5e7' : t.t2,
      fontSize: isConsole ? 15 : 13, cursor: 'pointer',
      boxShadow: active ? t.btnShd : 'none',
    }),
    startBtn: {
      padding: isConsole ? '10px 36px' : '8px 28px',
      background: t.startBtn, border: t.btnBdr, borderRadius: t.btnR,
      color: '#fef5e7', fontSize: isConsole ? 20 : 17, cursor: 'pointer',
      boxShadow: t.btnShd, letterSpacing: 1,
    },
  };

  const abilitySlots = cls.twoSlots ? 2 : 1;
  const equipped = [CW_ABILITIES[0], CW_ABILITIES[2]];

  return (
    <div style={charSelectStyles.root}>
      <CWSvgDefs />
      {/* Top bar */}
      <div style={charSelectStyles.topBar}>
        <span style={charSelectStyles.title}>CLUCK WARS</span>
      </div>

      {/* Body: class cards | preview | detail */}
      <div style={charSelectStyles.body}>
        {/* Class cards column */}
        <div style={charSelectStyles.classGrid}>
          {CW_CLASS_KEYS.map(key => {
            const c = CW_CLASSES[key];
            const active = key === selected;
            return (
              <div key={key} style={charSelectStyles.card(key)} onClick={() => setSelected(key)}>
                <CWChicken classKey={key} size={isConsole ? 52 : 44} />
                <span style={{ fontSize: isConsole ? 13 : 11, color: active ? t.t3 : t.t1, textAlign: 'center' }}>{c.short}</span>
                <span style={{ fontSize: isConsole ? 9 : 8, color: t.t2, fontFamily: "'Nunito',sans-serif", fontWeight: 400 }}>{c.role}</span>
              </div>
            );
          })}
        </div>

        {/* Center preview */}
        <div style={charSelectStyles.previewArea}>
          <div style={charSelectStyles.previewFrame}>
            <CWChicken classKey={selected} size={isConsole ? 140 : 110} />
          </div>
          <span style={{ fontSize: isConsole ? 22 : 18, color: cls.color, textShadow: `0 0 12px ${cls.color}44` }}>{cls.name}</span>
          <span style={{ fontSize: isConsole ? 12 : 10, color: t.t2, fontFamily: "'Nunito',sans-serif", fontStyle: 'italic', fontWeight: 400, textAlign: 'center', maxWidth: 200 }}>{cls.lore}</span>

          {/* Skin placeholder */}
          <div style={{ display: 'flex', gap: 6, marginTop: 4 }}>
            {['Default', 'Skin 2', 'Skin 3'].map((s, i) => (
              <div key={i} style={{
                width: isConsole ? 36 : 28, height: isConsole ? 36 : 28,
                borderRadius: 6, background: i === 0 ? cls.color : `${cls.color}33`,
                border: i === 0 ? `2px solid ${t.t3}` : `1px solid ${t.t2}44`,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                cursor: 'pointer',
              }}>
                {i === 0 && <div style={{ width: '60%', height: '60%', borderRadius: 3, background: cls.dark }} />}
              </div>
            ))}
          </div>
        </div>

        {/* Detail panel */}
        <div style={charSelectStyles.detail}>
          <span style={{ fontSize: isConsole ? 16 : 13, color: t.t3, borderBottom: `1px solid ${t.t2}33`, paddingBottom: 4 }}>STATS</span>
          {CW_STAT_ORDER.map(stat => (
            <CWStatBar key={stat} value={cls.stats[stat]} label={CW_STAT_LABELS[stat]} color={cls.color} theme={theme} />
          ))}

          <span style={{ fontSize: isConsole ? 16 : 13, color: t.t3, marginTop: 6, borderBottom: `1px solid ${t.t2}33`, paddingBottom: 4 }}>
            {abilitySlots > 1 ? 'ABILITIES (×2)' : 'ABILITY'}
          </span>
          <div style={charSelectStyles.abilityRow}>
            {Array.from({ length: abilitySlots }).map((_, i) => (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <CWHex size={isConsole ? 48 : 40} color={equipped[i].color} label={equipped[i].short} />
                <div>
                  <div style={{ fontSize: isConsole ? 11 : 10, color: t.t1 }}>{equipped[i].name}</div>
                  <div style={{ fontSize: isConsole ? 9 : 8, color: t.t2, fontFamily: "'Nunito',sans-serif", fontWeight: 400 }}>{equipped[i].type}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Bottom bar: session mode + start */}
      <div style={charSelectStyles.bottomBar}>
        <div style={{ display: 'flex', gap: 8 }}>
          {['Solo', 'Host', 'Join'].map((m, i) => (
            <div key={m} style={charSelectStyles.modeBtn(i === 0)}>{m}</div>
          ))}
        </div>
        <div style={charSelectStyles.startBtn}>START MATCH</div>
      </div>
    </div>
  );
};

// ─── MATCH HUD ─────────────────────────────────────────────
const CWMatchHUD = ({ theme = 'barnyard', platform = 'mobile' }) => {
  const t = CW_THEMES[theme];
  const isConsole = platform === 'console';
  const scores = [42, 17, 28, 31];

  const hudStyles = {
    root: { width: '100%', height: '100%', background: t.mapBg, fontFamily: "'Lilita One',cursive", position: 'relative', overflow: 'hidden' },
    topBar: {
      position: 'absolute', top: 0, left: 0, right: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      padding: isConsole ? '10px 24px' : '6px 12px', gap: isConsole ? 16 : 8,
      background: 'linear-gradient(180deg, rgba(0,0,0,0.5) 0%, transparent 100%)',
      zIndex: 10,
    },
    joystick: {
      position: 'absolute', bottom: isConsole ? 40 : 24, left: isConsole ? 60 : 30,
      width: isConsole ? 160 : 130, height: isConsole ? 160 : 130,
    },
    attackBtn: {
      position: 'absolute', bottom: isConsole ? 40 : 24, right: isConsole ? 60 : 30,
    },
    abilityArea: {
      position: 'absolute', right: isConsole ? 70 : 36,
      display: 'flex', flexDirection: 'column', gap: 10,
    },
    chickenGroup: {
      position: 'absolute', left: '50%', top: '50%', transform: 'translate(-50%, -50%)',
      display: 'flex', flexDirection: 'column', alignItems: 'center',
    },
  };

  // isometric grid lines (subtle background detail)
  const gridLines = [];
  for (let i = 0; i < 12; i++) {
    gridLines.push(
      <line key={`a${i}`} x1={i * 100} y1="0" x2={i * 100 + 300} y2="540" stroke="rgba(255,255,255,0.03)" strokeWidth="1" />,
      <line key={`b${i}`} x1={i * 100 + 600} y1="0" x2={i * 100 - 300} y2="540" stroke="rgba(255,255,255,0.03)" strokeWidth="1" />
    );
  }

  return (
    <div style={hudStyles.root}>
      {/* Subtle iso grid */}
      <svg style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', opacity: 0.6 }}>{gridLines}</svg>

      {/* Map elements placeholder */}
      <div style={{ position: 'absolute', left: '50%', top: '45%', transform: 'translate(-50%,-50%)' }}>
        {/* Central food pile */}
        <div style={{ width: 40, height: 30, borderRadius: '50%', background: 'radial-gradient(circle, #f5c842, #d48c20)', boxShadow: '0 0 20px rgba(245,200,66,0.3)', margin: '0 auto' }} />
      </div>

      {/* Player chicken with on-character bars */}
      <div style={hudStyles.chickenGroup}>
        <CWChicken classKey="warrior" size={isConsole ? 80 : 64} />
        {/* HP bar on character */}
        <div style={{ width: isConsole ? 60 : 48, height: 5, background: 'rgba(0,0,0,0.5)', borderRadius: 3, marginTop: -4, overflow: 'hidden', border: '1px solid rgba(255,255,255,0.1)' }}>
          <div style={{ width: '72%', height: '100%', background: t.hp, borderRadius: 3 }} />
        </div>
        {/* Cargo bar */}
        <div style={{ width: isConsole ? 60 : 48, height: 4, background: 'rgba(0,0,0,0.5)', borderRadius: 2, marginTop: 2, overflow: 'hidden', border: '1px solid rgba(255,255,255,0.1)' }}>
          <div style={{ width: '45%', height: '100%', background: t.cargo, borderRadius: 2 }} />
        </div>
        {/* Cargo text */}
        <span style={{ fontSize: 9, color: '#fef5e7', marginTop: 2, textShadow: '0 1px 2px rgba(0,0,0,0.8)', fontFamily: "'Nunito',sans-serif", fontWeight: 700 }}>9/20</span>
        {/* Player marker ring */}
        <div style={{ position: 'absolute', bottom: 16, left: '50%', transform: 'translateX(-50%)', width: isConsole ? 36 : 28, height: 6, borderRadius: 3, background: CW_PLAYER_COLORS[0], opacity: 0.7 }} />
      </div>

      {/* Enemy chickens (smaller, in background) */}
      {[{x:'28%',y:'35%',cls:'speedy',p:1},{x:'68%',y:'30%',cls:'fatty',p:2},{x:'72%',y:'65%',cls:'assassin',p:3}].map((e,i) => (
        <div key={i} style={{ position:'absolute', left:e.x, top:e.y, display:'flex', flexDirection:'column', alignItems:'center', opacity: 0.75, transform:'scale(0.7)' }}>
          <CWChicken classKey={e.cls} size={48} />
          <div style={{ width:36, height:4, background:'rgba(0,0,0,0.5)', borderRadius:2, marginTop:-2, overflow:'hidden' }}>
            <div style={{ width:'60%', height:'100%', background:t.hp, borderRadius:2 }} />
          </div>
          <div style={{ position:'absolute', bottom:8, left:'50%', transform:'translateX(-50%)', width:20, height:4, borderRadius:2, background:CW_PLAYER_COLORS[e.p], opacity:0.7 }} />
        </div>
      ))}

      {/* Top HUD bar */}
      <div style={hudStyles.topBar}>
        {scores.map((s, i) => <CWPlayerBadge key={i} index={i} score={s} small={!isConsole} />)}
        <CWTimer time="02:34" theme={theme} />
      </div>

      {/* Touch controls (mobile only) */}
      {!isConsole && <>
        {/* Joystick */}
        <div style={hudStyles.joystick}>
          <div style={{ width: '100%', height: '100%', borderRadius: '50%', background: 'rgba(255,255,255,0.08)', border: '2px solid rgba(255,255,255,0.12)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <div style={{ width: '45%', height: '45%', borderRadius: '50%', background: 'rgba(255,255,255,0.2)', border: '2px solid rgba(255,255,255,0.25)', transform: 'translate(8px, -4px)' }} />
          </div>
        </div>

        {/* Attack button */}
        <div style={hudStyles.attackBtn}>
          <div style={{ width: isConsole ? 140 : 110, height: isConsole ? 140 : 110, borderRadius: '50%', background: `${t.hp}88`, border: `3px solid ${t.hp}aa`, display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: `0 0 16px ${t.hp}33` }}>
            <span style={{ color: '#fef5e7', fontSize: isConsole ? 20 : 16, textShadow: '0 1px 3px rgba(0,0,0,0.5)' }}>ATTACK</span>
          </div>
        </div>

        {/* Ability buttons (hex) */}
        <div style={{ ...hudStyles.abilityArea, bottom: isConsole ? 180 : 140 }}>
          <CWCooldownHex size={isConsole ? 64 : 52} color="#00BCD4" label="SPD" pct={0} />
          <CWCooldownHex size={isConsole ? 64 : 52} color="#FF5722" label="ROLL" pct={0.35} />
        </div>
      </>}

      {/* Console button hints */}
      {isConsole && (
        <div style={{ position: 'absolute', bottom: 16, right: 24, display: 'flex', gap: 16, opacity: 0.7 }}>
          {[{btn:'A', label:'Attack', c:t.hp},{btn:'X', label:'Ability 1', c:'#00BCD4'},{btn:'Y', label:'Ability 2', c:'#FF5722'}].map((b,i) => (
            <div key={i} style={{ display:'flex', alignItems:'center', gap:5 }}>
              <div style={{ width:28, height:28, borderRadius:6, background: b.c+'44', border:`2px solid ${b.c}88`, display:'flex', alignItems:'center', justifyContent:'center', fontSize:13, color:'#fef5e7', fontWeight:700 }}>{b.btn}</div>
              <span style={{ fontSize:11, color:'#fef5e7aa', fontFamily:"'Nunito',sans-serif" }}>{b.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

// ─── MATCH END OVERLAY ─────────────────────────────────────
const CWMatchEnd = ({ theme = 'barnyard' }) => {
  const t = CW_THEMES[theme];
  const results = [
    { p: 0, score: 152 },
    { p: 2, score: 128 },
    { p: 3, score: 87 },
    { p: 1, score: 43 },
  ];
  const medals = ['🥇', '🥈', '🥉', ''];
  const maxScore = 152;

  return (
    <div style={{ width: '100%', height: '100%', background: t.mapBg, fontFamily: "'Lilita One',cursive", position: 'relative', overflow: 'hidden', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      {/* Dimmed background */}
      <div style={{ position: 'absolute', inset: 0, background: t.overlayBg }} />

      {/* End panel */}
      <div style={{
        position: 'relative', zIndex: 2, width: 420, padding: '24px 28px',
        background: t.panel, border: t.panelBdr, borderRadius: t.panelR,
        boxShadow: t.panelShd,
        display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14,
      }}>
        {/* Winner banner */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{ fontSize: 28 }}>🏆</span>
          <span style={{ fontSize: 24, color: CW_PLAYER_COLORS[results[0].p], textShadow: `0 0 10px ${CW_PLAYER_COLORS[results[0].p]}44` }}>
            PLAYER {results[0].p + 1} WINS!
          </span>
          <span style={{ fontSize: 28 }}>🏆</span>
        </div>

        {/* Leaderboard */}
        <div style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: 8 }}>
          {results.map((r, i) => {
            const pc = CW_PLAYER_COLORS[r.p];
            return (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '6px 10px', borderRadius: 8, background: i === 0 ? `${pc}18` : 'transparent' }}>
                <span style={{ fontSize: 20, width: 28, textAlign: 'center' }}>{medals[i]}</span>
                <div style={{ width: 12, height: 12, borderRadius: '50%', background: pc, flexShrink: 0 }} />
                <span style={{ fontSize: 16, color: pc, width: 40 }}>P{r.p + 1}</span>
                <div style={{ flex: 1, height: 14, background: t.barBg, borderRadius: 7, overflow: 'hidden', border: t.barBdr }}>
                  <div style={{ width: `${(r.score / maxScore) * 100}%`, height: '100%', background: pc, borderRadius: 7, transition: 'width 1s ease' }} />
                </div>
                <span style={{ fontSize: 15, color: t.t1, width: 40, textAlign: 'right' }}>{r.score}</span>
                <svg viewBox="0 0 20 20" width="14" height="14"><circle cx="10" cy="12" r="7" fill="#f5c842" /><ellipse cx="10" cy="7" rx="3" ry="4" fill="#4CAF50" /></svg>
              </div>
            );
          })}
        </div>

        {/* Restart countdown */}
        <span style={{ fontSize: 14, color: t.t2, fontFamily: "'Nunito',sans-serif", fontWeight: 400 }}>
          Next match in 5s…
        </span>
      </div>
    </div>
  );
};

// ─── LOBBY SCREEN ──────────────────────────────────────────
const CWLobby = ({ theme = 'barnyard' }) => {
  const t = CW_THEMES[theme];
  const players = [
    { p: 0, cls: 'warrior', name: 'P1 (You)', ready: true },
    { p: 1, cls: 'speedy', name: 'P2', ready: true },
    { p: 2, cls: 'fatty', name: 'P3', ready: false },
  ];

  return (
    <div style={{ width: '100%', height: '100%', background: t.screenBg, fontFamily: "'Lilita One',cursive", display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <div style={{
        width: 460, padding: '22px 26px',
        background: t.panel, border: t.panelBdr, borderRadius: t.panelR,
        boxShadow: t.panelShd,
        display: 'flex', flexDirection: 'column', gap: 14,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <span style={{ fontSize: 22, color: t.t1 }}>MATCH LOBBY</span>
          <span style={{ fontSize: 16, color: '#4ae66a', fontWeight: 700, letterSpacing: 2, fontFamily: "'Nunito',sans-serif" }}>XK4P2M</span>
        </div>

        <div style={{ fontSize: 13, color: t.t2, fontFamily: "'Nunito',sans-serif", fontWeight: 400 }}>
          Players: 3 / 4 — Waiting for more players…
        </div>

        {/* Player list */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {players.map((pl, i) => (
            <div key={i} style={{
              display: 'flex', alignItems: 'center', gap: 10, padding: '8px 12px',
              borderRadius: 8, background: `${CW_PLAYER_COLORS[pl.p]}11`,
              border: `1px solid ${CW_PLAYER_COLORS[pl.p]}33`,
            }}>
              <div style={{ width: 14, height: 14, borderRadius: '50%', background: CW_PLAYER_COLORS[pl.p] }} />
              <CWChicken classKey={pl.cls} size={36} />
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 14, color: t.t1 }}>{pl.name}</div>
                <div style={{ fontSize: 10, color: t.t2, fontFamily: "'Nunito',sans-serif", fontWeight: 400 }}>{CW_CLASSES[pl.cls].short} — {CW_CLASSES[pl.cls].role}</div>
              </div>
              <span style={{ fontSize: 11, color: pl.ready ? '#4ae66a' : t.t2, fontFamily: "'Nunito',sans-serif" }}>
                {pl.ready ? '✓ Ready' : 'Picking…'}
              </span>
            </div>
          ))}

          {/* Empty slot */}
          <div style={{
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            padding: '12px', borderRadius: 8, border: `2px dashed ${t.t2}33`,
          }}>
            <span style={{ fontSize: 13, color: t.t2, fontFamily: "'Nunito',sans-serif", fontWeight: 400 }}>Waiting for Player 4…</span>
          </div>
        </div>

        {/* Start button (host) */}
        <div style={{
          padding: '10px 0', textAlign: 'center',
          background: t.startBtn, border: t.btnBdr, borderRadius: t.btnR,
          boxShadow: t.btnShd, color: '#fef5e7', fontSize: 18, cursor: 'pointer',
          letterSpacing: 1,
        }}>
          START MATCH
        </div>
      </div>
    </div>
  );
};

// ─── INTRO COUNTDOWN ───────────────────────────────────────
const CWIntroCountdown = ({ theme = 'barnyard', number = 2 }) => {
  const t = CW_THEMES[theme];
  return (
    <div style={{ width: '100%', height: '100%', background: t.mapBg, fontFamily: "'Lilita One',cursive", position: 'relative', overflow: 'hidden', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      {/* Dim overlay */}
      <div style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.4)' }} />

      {/* Number */}
      <div style={{ position: 'relative', zIndex: 2, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
        <span style={{
          fontSize: 160, color: t.t3, textShadow: `0 0 40px ${t.t3}66, 0 4px 12px rgba(0,0,0,0.5)`,
          lineHeight: 1,
        }}>
          {number}
        </span>
        <span style={{ fontSize: 18, color: t.t2, letterSpacing: 4 }}>GET READY</span>
      </div>

      {/* Faint top bar (still visible) */}
      <div style={{ position: 'absolute', top: 0, left: 0, right: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '8px 16px', gap: 10, opacity: 0.5 }}>
        {[0,1,2,3].map(i => <CWPlayerBadge key={i} index={i} score={0} small />)}
        <CWTimer time="03:00" theme={theme} />
      </div>
    </div>
  );
};

// ─── TOUCH CONTROLS DETAIL ─────────────────────────────────
const CWTouchLayout = ({ theme = 'barnyard' }) => {
  const t = CW_THEMES[theme];
  return (
    <div style={{ width: '100%', height: '100%', background: '#0a0a0a', fontFamily: "'Lilita One',cursive", position: 'relative', overflow: 'hidden', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      {/* Phone outline */}
      <div style={{ width: '92%', height: '80%', border: '2px solid #333', borderRadius: 20, position: 'relative', overflow: 'hidden', background: t.mapBg }}>
        {/* Thumb zones */}
        <div style={{ position: 'absolute', left: 0, bottom: 0, width: '40%', height: '65%', background: 'rgba(255,255,255,0.03)', borderTopRightRadius: 60 }}>
          <span style={{ position: 'absolute', top: 8, left: 12, fontSize: 10, color: '#ffffff44', fontFamily: "'Nunito',sans-serif" }}>LEFT THUMB ZONE</span>
        </div>
        <div style={{ position: 'absolute', right: 0, bottom: 0, width: '40%', height: '65%', background: 'rgba(255,255,255,0.03)', borderTopLeftRadius: 60 }}>
          <span style={{ position: 'absolute', top: 8, right: 12, fontSize: 10, color: '#ffffff44', fontFamily: "'Nunito',sans-serif", textAlign: 'right' }}>RIGHT THUMB ZONE</span>
        </div>

        {/* Joystick */}
        <div style={{ position: 'absolute', bottom: 30, left: 40 }}>
          <div style={{ width: 120, height: 120, borderRadius: '50%', background: 'rgba(255,255,255,0.08)', border: '2px solid rgba(255,255,255,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <div style={{ width: 50, height: 50, borderRadius: '50%', background: 'rgba(255,255,255,0.2)', border: '2px solid rgba(255,255,255,0.3)' }} />
          </div>
          <span style={{ display: 'block', textAlign: 'center', fontSize: 9, color: '#ffffff66', marginTop: 4, fontFamily: "'Nunito',sans-serif" }}>MOVE</span>
        </div>

        {/* Attack */}
        <div style={{ position: 'absolute', bottom: 30, right: 40 }}>
          <div style={{ width: 100, height: 100, borderRadius: '50%', background: `${t.hp}55`, border: `3px solid ${t.hp}88`, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <span style={{ color: '#fef5e7', fontSize: 14 }}>ATTACK</span>
          </div>
          <span style={{ display: 'block', textAlign: 'center', fontSize: 9, color: '#ffffff66', marginTop: 4, fontFamily: "'Nunito',sans-serif" }}>MASH</span>
        </div>

        {/* Abilities (hex) */}
        <div style={{ position: 'absolute', bottom: 120, right: 48, display: 'flex', flexDirection: 'column', gap: 8 }}>
          <CWCooldownHex size={50} color="#00BCD4" label="SPD" pct={0} />
          <CWCooldownHex size={50} color="#FF5722" label="ROLL" pct={0} />
          <span style={{ fontSize: 9, color: '#ffffff66', textAlign: 'center', fontFamily: "'Nunito',sans-serif" }}>ABILITIES</span>
        </div>

        {/* Mini top bar */}
        <div style={{ position: 'absolute', top: 0, left: 0, right: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '4px 10px', gap: 6, background: 'linear-gradient(180deg, rgba(0,0,0,0.4), transparent)' }}>
          {[0,1,2,3].map(i => <CWPlayerBadge key={i} index={i} score={0} small />)}
          <CWTimer time="03:00" theme={theme} />
        </div>
      </div>
    </div>
  );
};

// Export all screen components
Object.assign(window, {
  CWCharacterSelect, CWMatchHUD, CWMatchEnd, CWLobby, CWIntroCountdown, CWTouchLayout,
});
