// In-memory session store. Survives across requests as long as the Vercel
// function instance stays warm — fine for a 1–2 hour live demo. Swap for
// Vercel KV / Upstash Redis later for cold-start persistence.
//
// Layout:
//   sessions[code] = {
//     p1: PlayerSlot,
//     p2: PlayerSlot,
//   }

export type PlayerStatus =
  | 'empty'
  | 'queued'
  | 'generating'
  | 'rigging'
  | 'cry'
  | 'ready'
  | 'error';

export interface PlayerSlot {
  status: PlayerStatus;
  name: string | null;
  imageUrl: string | null;
  glbUrl: string | null;
  cryUrl: string | null;
  taskId: string | null;
  stats: {
    hp: number;
    attackMult: number;
    speed: number;
    element: string;
    moves: string[];
    flavorText: string;
  } | null;
}

export interface Session {
  code: string;
  p1: PlayerSlot;
  p2: PlayerSlot;
}

// Use globalThis so HMR in dev and serverless warm starts don't wipe the map.
declare global {
  // eslint-disable-next-line no-var
  var __tigerverseSessions: Map<string, Session> | undefined;
}
const sessions: Map<string, Session> =
  globalThis.__tigerverseSessions ?? (globalThis.__tigerverseSessions = new Map());

function emptySlot(): PlayerSlot {
  return {
    status: 'empty',
    name: null,
    imageUrl: null,
    glbUrl: null,
    cryUrl: null,
    taskId: null,
    stats: null,
  };
}

export function getSession(code: string): Session {
  const upper = code.toUpperCase();
  let s = sessions.get(upper);
  if (!s) {
    s = { code: upper, p1: emptySlot(), p2: emptySlot() };
    sessions.set(upper, s);
  }
  return s;
}

export function updateSlot(
  code: string,
  slot: 1 | 2,
  patch: Partial<PlayerSlot>,
): Session {
  const s = getSession(code);
  const key = slot === 1 ? 'p1' : 'p2';
  s[key] = { ...s[key], ...patch };
  return s;
}

// Find which slot (1 or 2) holds a given Meshy taskId across all sessions.
export function findSlotByTaskId(
  taskId: string,
): { code: string; slot: 1 | 2 } | null {
  for (const s of sessions.values()) {
    if (s.p1.taskId === taskId) return { code: s.code, slot: 1 };
    if (s.p2.taskId === taskId) return { code: s.code, slot: 2 };
  }
  return null;
}

// Hardcoded stat presets keyed by element. Cycles through to give each player
// distinct stats without an LLM call. Move pool matches Unity's MoveCatalog.
const PRESETS: PlayerSlot['stats'][] = [
  {
    hp: 110, attackMult: 1.05, speed: 1.2, element: 'electric',
    moves: ['Thunderbolt', 'Iceshard', 'Healingaura'],
    flavorText: 'A static-charged rodent.',
  },
  {
    hp: 120, attackMult: 1.0, speed: 1.0, element: 'fire',
    moves: ['Fireball', 'Watergun', 'Taunt'],
    flavorText: 'A blazing winged beast.',
  },
  {
    hp: 95, attackMult: 1.2, speed: 1.3, element: 'grass',
    moves: ['Leafblade', 'Healingaura', 'Dodge'],
    flavorText: 'Quick and verdant.',
  },
  {
    hp: 130, attackMult: 0.9, speed: 0.85, element: 'earth',
    moves: ['Rocksmash', 'Shadowbite', 'Taunt'],
    flavorText: 'Slow but unbreakable.',
  },
];

let _presetCursor = 0;
export function nextPreset(): PlayerSlot['stats'] {
  const p = PRESETS[_presetCursor % PRESETS.length];
  _presetCursor++;
  return p ? { ...p } : null;
}
