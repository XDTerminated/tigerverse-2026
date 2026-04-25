import type { APIRoute } from 'astro';
import { generateMonsterCryFromCharacteristics } from '../../lib/eleven';
import { uploadAudioBuffer } from '../../lib/uploadthing';
import { updateSlot } from '../../lib/sessions';

export const prerender = false;

export const POST: APIRoute = async ({ request }) => {
  let body: {
    name?: string;
    characteristics?: string;
    sessionCode?: string;
    playerSlot?: 1 | 2;
  };
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: 'Invalid JSON' }, { status: 400 });
  }

  const { name, characteristics, sessionCode, playerSlot } = body;
  if (!name || typeof name !== 'string') {
    return Response.json({ error: 'name required' }, { status: 400 });
  }
  if (!characteristics || typeof characteristics !== 'string' || !characteristics.trim()) {
    return Response.json({ error: 'characteristics required' }, { status: 400 });
  }

  try {
    const audio = await generateMonsterCryFromCharacteristics(name, characteristics);
    const cryUrl = await uploadAudioBuffer(audio, `${name}_cry`);

    // If the request is part of a Tigerverse session, overwrite the slot's
    // cryUrl so the Quest game picks up the new voice on its next poll.
    if (sessionCode && (playerSlot === 1 || playerSlot === 2)) {
      updateSlot(sessionCode, playerSlot, { cryUrl });
    }

    return Response.json({ cryUrl });
  } catch (err) {
    return Response.json({ error: (err as Error).message }, { status: 502 });
  }
};
