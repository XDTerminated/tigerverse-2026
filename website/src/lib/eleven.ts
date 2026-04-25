import { ELEVENLABS_API_KEY } from 'astro:env/server';

const DEFAULT_VOICE_ID = '21m00Tcm4TlvDq8ikWAM'; // Rachel — works on every account
const DEFAULT_MODEL_ID = 'eleven_turbo_v2_5';

async function callTts(text: string, voiceId = DEFAULT_VOICE_ID): Promise<Buffer> {
  if (!ELEVENLABS_API_KEY) throw new Error('ELEVENLABS_API_KEY not configured');

  const res = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${voiceId}`, {
    method: 'POST',
    headers: {
      'xi-api-key': ELEVENLABS_API_KEY,
      'Content-Type': 'application/json',
      Accept: 'audio/mpeg',
    },
    body: JSON.stringify({ text, model_id: DEFAULT_MODEL_ID }),
  });

  if (!res.ok) {
    throw new Error(`ElevenLabs TTS failed: ${res.status} ${await res.text()}`);
  }

  const arr = await res.arrayBuffer();
  return Buffer.from(arr);
}

/** Build a Pokemon-style "say your name" cry text from the monster name. */
function buildCryText(name: string): string {
  const trimmed = (name ?? '').trim();
  let firstWord = trimmed.split(/[\s,.!?]+/).filter(Boolean)[0] ?? 'Bweh';
  if (firstWord.length > 12) firstWord = firstWord.slice(0, 12);
  // Pick one of three patterns at random so successive monsters don't sound identical.
  const variants = [
    `${firstWord}! ${firstWord}!`,
    `${firstWord}, ${firstWord}-${firstWord}!`,
    `${firstWord}! ${firstWord}, ${firstWord}!`,
  ];
  return variants[Math.floor(Math.random() * variants.length)];
}

/**
 * Generate a Pokemon-style monster cry: the creature dramatically yells
 * its own name twice via ElevenLabs TTS. Returns raw mp3 bytes for upload.
 */
export async function generateMonsterCry(name: string, _element: string): Promise<Buffer> {
  return callTts(buildCryText(name));
}

/**
 * Same name-yelling cry, but kept as a separate function so the /api/voice
 * route can pass through unchanged while a future enhancement could shape
 * the voice based on personality characteristics (e.g. switch voice ID).
 */
export async function generateMonsterCryFromCharacteristics(
  name: string,
  _characteristics: string,
): Promise<Buffer> {
  return callTts(buildCryText(name));
}
