// CluckWars UI v3 — Data deltas for v0.3 spec
// =============================================
// Visual atoms (CWHex, CWChicken, CWPanel, CWRibbon, CWButton, CWStatBar,
// CWFoodIcon, CWTimer, CWPlayerBadge, CW_THEME, adjustColor) are reused
// unchanged from cluckwars-tokens-v2.jsx — only data shapes change here.

// ─── CLASSES v0.3 ─────────────────────────────────────────
// • Attack stat removed (combat is now ability-driven only)
// • Slot count: Warrior/Speedy/Fatty = 2, Assassin = 3
// • Each class has a passive (named) — no class restriction on abilities
const CW_CLASSES_V3 = {
  warrior: {
    name: 'Warrior Chicken',  short: 'Warrior',  role: 'All-Rounder',
    passive: 'Tough',          passiveDesc: 'Deals increased damage with abilities.',
    color: '#C04030', dark: '#8A2A20', light:'#F09888',
    stats: { cargo:3, rate:3, hp:4, resist:3, speed:3 },
    shape:'broad', slots: 2,
    lore: '"The farm is a battlefield."',
  },
  speedy: {
    name: 'Speedy Chicken',   short: 'Speedy',   role: 'Hit & Run',
    passive: 'Slippery',       passiveDesc: 'Control effects on you have reduced duration.',
    color: '#E85A2A', dark: '#B84418', light:'#FFB088',
    stats: { cargo:2, rate:3, hp:2, resist:1, speed:5 },
    shape:'lean', slots: 2,
    lore: '"If you can\'t catch me, you can\'t kill me."',
  },
  fatty: {
    name: 'Fatty Chicken',    short: 'Fatty',    role: 'Bulk Carrier',
    passive: 'Immovable',      passiveDesc: 'Greatly reduced knockback from all sources.',
    color: '#F5D75A', dark: '#B89E20', light:'#FFF3B0',
    stats: { cargo:5, rate:5, hp:5, resist:5, speed:2 },
    shape:'wide', slots: 2,
    lore: '"Slow and steady wins the race — if it survives long enough."',
  },
  assassin: {
    name: 'Assassin Chicken', short: 'Assassin', role: 'Disruptor',
    passive: 'Combo',          passiveDesc: 'Equips 3 abilities instead of 2.',
    color: '#7B68EE', dark: '#5A48C8', light:'#C4B8FF',
    stats: { cargo:2, rate:2, hp:2, resist:2, speed:4 },
    shape:'sleek', slots: 3,
    lore: '"Blink and your food is gone."',
  },
};
const CW_CLASS_ORDER_V3 = ['warrior','speedy','fatty','assassin'];

// 5 stats only (attack removed)
const CW_STAT_ORDER_V3 = ['cargo','rate','hp','resist','speed'];
const CW_STAT_LABELS_V3 = { cargo:'Cargo', rate:'Rate', hp:'HP', resist:'Resist', speed:'Speed' };

// ─── ABILITIES v0.3 (14 total, 4 categories) ──────────────
// Short CD = 3–6s · Medium CD = 8–12s
const CW_ABILITY_CATS = {
  damage:  { label:'DAMAGE',  blurb:'Deals HP — can stun at 0',     color:'#FF5722', icon:'⚔︎' },
  control: { label:'CONTROL', blurb:'No HP — disrupts movement',    color:'#9C27B0', icon:'⏚' },
  defense: { label:'DEFENSE', blurb:'Protect self or cargo',        color:'#4CAF50', icon:'⛨' },
  utility: { label:'UTILITY', blurb:'Non-combat advantage',         color:'#00BCD4', icon:'✦' },
};

const CW_ABILITIES_V3 = [
  // DAMAGE
  { id:'fly_peck', name:'Flying Peck', short:'FLY',   cat:'damage',  cd:'S', cdSec:4,  color:'#FF7043', icon:'🪽',
    desc:'Dash forward, dealing HP damage on contact with the first chicken hit.', tag:'Dash · HP', flavor:'The classic opener.' },
  { id:'cluck',    name:'Cluck Shock', short:'SHOCK', cat:'damage',  cd:'M', cdSec:10, color:'#FF5722', icon:'⚡',
    desc:'A one-shot AoE HP burst centered on yourself. Hits everyone in range.', tag:'AoE · HP', flavor:'Get off me.' },
  { id:'peck',     name:'Peck',        short:'PECK',  cat:'damage',  cd:'S', cdSec:3,  color:'#E64A19', icon:'🐦',
    desc:'Instant short-range HP hit with a minor knockback. Spammable poke.', tag:'Melee · HP + KB', flavor:'Just a little prick.' },
  // CONTROL
  { id:'roll',     name:'Roll & Push', short:'ROLL',  cat:'control', cd:'S', cdSec:5,  color:'#AB47BC', icon:'🌀',
    desc:'Roll forward and shove the first target backwards. No damage, pure displacement.', tag:'Dash · KB', flavor:'Make room.' },
  { id:'trap',     name:'Feather Trap',short:'TRAP',  cat:'control', cd:'M', cdSec:9,  color:'#9C27B0', icon:'🪤',
    desc:'Throw a feather cloud to a location. Slows anyone walking through.', tag:'Placed · Slow', flavor:'Layer the lane.' },
  { id:'aura',     name:'Feather Aura',short:'AURA',  cat:'control', cd:'M', cdSec:11, color:'#8E24AA', icon:'💨',
    desc:'Emit a feather cloud around yourself. Slows all nearby chickens.', tag:'Self · Slow', flavor:'Personal bubble.' },
  { id:'root',     name:'Root Egg',    short:'ROOT',  cat:'control', cd:'M', cdSec:10, color:'#7B1FA2', icon:'🌱',
    desc:'Place an egg that roots the first chicken to step on it. Consumed on trigger.', tag:'Placed · Root', flavor:'One-shot trap.' },
  // DEFENSE
  { id:'shell',    name:'Egg Shell',   short:'SHELL', cat:'defense', cd:'S', cdSec:6,  color:'#66BB6A', icon:'🥚',
    desc:'Become an invulnerable egg. Immobile while active — cargo and HP are safe.', tag:'Invuln · Immobile', flavor:'Wait it out.' },
  { id:'turtle',   name:'Turtle Mode', short:'TRTL',  cat:'defense', cd:'S', cdSec:5,  color:'#4CAF50', icon:'🐢',
    desc:'Near-zero speed but greatly increased resistance. Walk it home with cargo.', tag:'Tank · Slow self', flavor:'Slow & sturdy.' },
  { id:'spine',    name:'Spine Coat',  short:'SPINE', cat:'defense', cd:'M', cdSec:9,  color:'#43A047', icon:'🦔',
    desc:'Damages and knocks back any chicken that contacts you. Reactive armor.', tag:'Reactive · HP + KB', flavor:'Don\'t touch.' },
  // UTILITY
  { id:'burst',    name:'Speed Burst', short:'BURST', cat:'utility', cd:'S', cdSec:4,  color:'#26C6DA', icon:'💨',
    desc:'Short, sharp movement-speed boost. The bread-and-butter escape.', tag:'Self · +Speed', flavor:'Catch me if you can.' },
  { id:'invis',    name:'Invisibility',short:'INVIS', cat:'utility', cd:'M', cdSec:10, color:'#00ACC1', icon:'👻',
    desc:'Become invisible to other players for a short window. Carry uncontested.', tag:'Self · Stealth', flavor:'Now you don\'t.' },
  { id:'doppel',   name:'Doppelganger',short:'DPLG',  cat:'utility', cd:'M', cdSec:12, color:'#0097A7', icon:'👥',
    desc:'Spawn a decoy copy of yourself. Draws attention while you reposition.', tag:'Decoy · AI', flavor:'Which one is real?' },
  { id:'steal',    name:'Sneaky Steal',short:'STEAL', cat:'utility', cd:'S', cdSec:5,  color:'#00BCD4', icon:'🤏',
    desc:'Instantly siphon a small amount of cargo from a nearby rival. No HP interaction.', tag:'Target · Drain', flavor:'A little off the top.' },
];

const CW_ABILITY_BY_ID = Object.fromEntries(CW_ABILITIES_V3.map(a => [a.id, a]));

// Player colors (Okabe-Ito) — unchanged from v0.2
const CW_PLAYERS_V3 = [
  { name:'P1', color:'#E8751A', label:'Sunset Orange' },
  { name:'P2', color:'#1A7FC4', label:'Ocean Blue' },
  { name:'P3', color:'#C4286F', label:'Berry Pink' },
  { name:'P4', color:'#0D9E7A', label:'Forest Teal' },
];

Object.assign(window, {
  CW_CLASSES_V3, CW_CLASS_ORDER_V3,
  CW_STAT_ORDER_V3, CW_STAT_LABELS_V3,
  CW_ABILITY_CATS, CW_ABILITIES_V3, CW_ABILITY_BY_ID,
  CW_PLAYERS_V3,
});
