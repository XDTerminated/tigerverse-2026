import { ELEVENLABS_API_KEY } from 'astro:env/server';

async function callElevenLabs(prompt: string): Promise<Buffer> {
  if (!ELEVENLABS_API_KEY) throw new Error('ELEVENLABS_API_KEY not configured');

  const res = await fetch('https://api.elevenlabs.io/v1/sound-generation', {
    method: 'POST',
    headers: {
      'xi-api-key': ELEVENLABS_API_KEY,
      'Content-Type': 'application/json',
      Accept: 'audio/mpeg',
    },
    body: JSON.stringify({
      text: prompt,
      duration_seconds: 2.0,
      prompt_influence: 0.6,
    }),
  });

  if (!res.ok) {
    throw new Error(`ElevenLabs sound-gen failed: ${res.status} ${await res.text()}`);
  }

  const arr = await res.arrayBuffer();
  return Buffer.from(arr);
}

/**
 * Generate a Pokemon-style monster cry via ElevenLabs Sound Effects.
 * Used as the auto-generated baseline cry derived from the doodle's color.
 * Returns the raw audio buffer (mp3) so the caller can upload it.
 */
export async function generateMonsterCry(name: string, element: string): Promise<Buffer> {
  const prompt =
    `A short non-human cartoon creature roar in the style of a Pokemon cry, ` +
    `vaguely yelling its own name "${name}" with a ${element} elemental texture. ` +
    `Mouthy, energetic, ~2 seconds, no music, no lyrics.`;
  return callElevenLabs(prompt);
}

/**
 * Generate a Pokemon-style monster cry whose tone is shaped by player-supplied
 * personality characteristics (e.g. "ferocious, deep growl, scary"). Used by
 * /api/voice when the user explicitly customizes the creature in the
 * generating UI.
 */
export async function generateMonsterCryFromCharacteristics(
  name: string,
  characteristics: string,
): Promise<Buffer> {
  const sanitized = characteristics.trim().replace(/\s+/g, ' ').slice(0, 400);
  const prompt =
    `A short non-human cartoon creature roar in the style of a Pokemon cry, ` +
    `vaguely yelling its own name "${name}". ` +
    `The creature is described as: ${sanitized}. ` +
    `Mouthy, energetic, ~2 seconds, no music, no lyrics.`;
  return callElevenLabs(prompt);
}
