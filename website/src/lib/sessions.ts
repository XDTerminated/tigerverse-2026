// Session store. Uses Upstash Redis (compatible with Vercel KV's REST envs)
// when KV_REST_API_URL + KV_REST_API_TOKEN are set, otherwise falls back to
// an in-memory Map for local dev. Vercel runs each API route as a separate
// serverless function with isolated memory, so the in-memory fallback only
// works when running `astro dev` locally, production must have the Redis
// envs configured.

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

// ─── Storage backend selection ───────────────────────────────────────────────
// Upstash Redis (REST) when env vars are present; in-memory Map otherwise.

import { Redis } from '@upstash/redis';

const KV_URL = process.env.KV_REST_API_URL ?? process.env.UPSTASH_REDIS_REST_URL;
const KV_TOKEN = process.env.KV_REST_API_TOKEN ?? process.env.UPSTASH_REDIS_REST_TOKEN;
const useRedis = !!(KV_URL && KV_TOKEN);
const redis = useRedis ? new Redis({ url: KV_URL!, token: KV_TOKEN! }) : null;

declare global {
  // eslint-disable-next-line no-var
  var __tigerverseSessions: Map<string, Session> | undefined;
  // Keep an index of all known taskIds so findSlotByTaskId can resolve owners
  // via Redis without scanning the whole keyspace.
  // Format in Redis: hash 'tigerverse:taskMap' { [taskId]: 'CODE:1' or 'CODE:2' }
}
const memSessions: Map<string, Session> =
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

const SESSION_KEY = (code: string) => `tigerverse:session:${code.toUpperCase()}`;
const TASKMAP_KEY = 'tigerverse:taskMap';
const SESSION_TTL_SECONDS = 60 * 60 * 6; // 6h, sessions auto-expire

export async function getSession(code: string): Promise<Session> {
  const upper = code.toUpperCase();

  if (useRedis && redis) {
    const raw = await redis.get<Session>(SESSION_KEY(upper));
    if (raw) return raw;
    const fresh: Session = { code: upper, p1: emptySlot(), p2: emptySlot() };
    await redis.set(SESSION_KEY(upper), fresh, { ex: SESSION_TTL_SECONDS });
    return fresh;
  }

  let s = memSessions.get(upper);
  if (!s) {
    s = { code: upper, p1: emptySlot(), p2: emptySlot() };
    memSessions.set(upper, s);
  }
  return s;
}

export async function updateSlot(
  code: string,
  slot: 1 | 2,
  patch: Partial<PlayerSlot>,
): Promise<Session> {
  const upper = code.toUpperCase();
  const s = await getSession(upper);
  const key = slot === 1 ? 'p1' : 'p2';
  s[key] = { ...s[key], ...patch };

  if (useRedis && redis) {
    await redis.set(SESSION_KEY(upper), s, { ex: SESSION_TTL_SECONDS });
    if (patch.taskId) {
      // Index this taskId → "CODE:slot" so we can look it up later.
      await redis.hset(TASKMAP_KEY, { [patch.taskId]: `${upper}:${slot}` });
    }
  } else {
    memSessions.set(upper, s);
  }
  return s;
}

// Find which slot (1 or 2) holds a given Meshy taskId across all sessions.
export async function findSlotByTaskId(
  taskId: string,
): Promise<{ code: string; slot: 1 | 2 } | null> {
  if (useRedis && redis) {
    const v = await redis.hget<string>(TASKMAP_KEY, taskId);
    if (!v) return null;
    const [code, slotStr] = v.split(':');
    const slot = slotStr === '2' ? 2 : 1;
    return { code, slot };
  }

  for (const s of memSessions.values()) {
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
