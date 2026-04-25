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

// Element-themed stat presets. Each player gets stats matched to the element
// derived from their drawing's dominant ink color (computed in /api/generate).
// Move pool matches Unity's MoveCatalog exactly.
const PRESETS_BY_ELEMENT: Record<string, NonNullable<PlayerSlot['stats']>> = {
  fire: {
    hp: 110, attackMult: 1.15, speed: 1.05, element: 'fire',
    moves: ['Fireball', 'Shadowbite', 'Taunt'],
    flavorText: 'Born of flame and fury.',
  },
  water: {
    hp: 120, attackMult: 1.0, speed: 0.95, element: 'water',
    moves: ['Watergun', 'Iceshard', 'Healingaura'],
    flavorText: 'Tides answer its call.',
  },
  electric: {
    hp: 95, attackMult: 1.05, speed: 1.3, element: 'electric',
    moves: ['Thunderbolt', 'Shadowbite', 'Dodge'],
    flavorText: 'A static-charged rodent.',
  },
  earth: {
    hp: 140, attackMult: 1.1, speed: 0.8, element: 'earth',
    moves: ['Rocksmash', 'Leafblade', 'Taunt'],
    flavorText: 'Slow but unbreakable.',
  },
  grass: {
    hp: 105, attackMult: 1.0, speed: 1.15, element: 'grass',
    moves: ['Leafblade', 'Healingaura', 'Watergun'],
    flavorText: 'Quick and verdant.',
  },
  ice: {
    hp: 100, attackMult: 1.1, speed: 1.0, element: 'ice',
    moves: ['Iceshard', 'Watergun', 'Dodge'],
    flavorText: 'Frostbitten and sharp.',
  },
  dark: {
    hp: 115, attackMult: 1.2, speed: 1.05, element: 'dark',
    moves: ['Shadowbite', 'Taunt', 'Dodge'],
    flavorText: 'Strikes from shadow.',
  },
  neutral: {
    hp: 110, attackMult: 1.0, speed: 1.0, element: 'neutral',
    moves: ['Rocksmash', 'Healingaura', 'Dodge'],
    flavorText: 'Balanced and adaptable.',
  },
};

export function presetForElement(element: string): NonNullable<PlayerSlot['stats']> {
  const key = (element ?? 'neutral').toLowerCase();
  return { ...(PRESETS_BY_ELEMENT[key] ?? PRESETS_BY_ELEMENT.neutral) };
}

// Map an RGB color (0-255) to one of our 8 element keys based on hue + saturation.
export function elementFromRgb(r: number, g: number, b: number): string {
  // Greyscale-ish → earth (brown) or neutral
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const sat = max === 0 ? 0 : (max - min) / max;
  if (sat < 0.18) {
    // Very low saturation. Dark → dark, mid → earth, light → ice.
    if (max < 70) return 'dark';
    if (max > 200) return 'ice';
    return 'earth';
  }
  // Hue calc (0-360)
  let h = 0;
  if (max === r) h = ((g - b) / (max - min)) * 60;
  else if (max === g) h = ((b - r) / (max - min)) * 60 + 120;
  else h = ((r - g) / (max - min)) * 60 + 240;
  if (h < 0) h += 360;

  if (h < 18 || h >= 340) return 'fire';     // red
  if (h < 45)  return 'fire';                 // orange (still fire)
  if (h < 70)  return 'electric';             // yellow
  if (h < 170) return 'grass';                // green / teal-green
  if (h < 200) return 'ice';                  // cyan
  if (h < 260) return 'water';                // blue
  if (h < 320) return 'dark';                 // purple / magenta
  return 'fire';                              // pinks → fire
}
