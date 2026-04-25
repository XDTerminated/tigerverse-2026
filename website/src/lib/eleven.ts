import { ELEVENLABS_API_KEY } from 'astro:env/server';

/**
 * Generate a Pokemon-style monster cry via ElevenLabs Sound Effects.
 * Returns the raw audio buffer (mp3) so the caller can upload it.
 */
export async function generateMonsterCry(name: string, element: string): Promise<Buffer> {
  if (!ELEVENLABS_API_KEY) throw new Error('ELEVENLABS_API_KEY not configured');

  const prompt =
    `A short non-human cartoon creature roar in the style of a Pokemon cry, ` +
    `vaguely yelling its own name "${name}" with a ${element} elemental texture. ` +
    `Mouthy, energetic, ~2 seconds, no music, no lyrics.`;

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
