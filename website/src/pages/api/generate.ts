import type { APIRoute } from 'astro';
import { createImageTo3dTask } from '../../lib/meshy';
import { uploadImageFromDataUri } from '../../lib/uploadthing';
import { updateSlot } from '../../lib/sessions';

export const prerender = false;

export const POST: APIRoute = async ({ request }) => {
  let body: {
    imageDataUri?: string;
    name?: string;
    sessionCode?: string;
    playerSlot?: 1 | 2;
  };
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: 'Invalid JSON' }, { status: 400 });
  }

  const { imageDataUri, name, sessionCode, playerSlot } = body;
  if (!imageDataUri || !imageDataUri.startsWith('data:image/')) {
    return Response.json({ error: 'imageDataUri (data URI) required' }, { status: 400 });
  }
  if (!name || typeof name !== 'string') {
    return Response.json({ error: 'name required' }, { status: 400 });
  }

  try {
    const taskId = await createImageTo3dTask(imageDataUri);

    // If this submission is part of a Tigerverse session, record the taskId on
    // the right player slot so /api/session/[code] surfaces progress to the
    // Quest game and the /api/status/[taskId] route can flip the slot to
    // 'ready' once Meshy finishes.
    if (sessionCode && (playerSlot === 1 || playerSlot === 2)) {
      // Upload the raw drawing to UploadThing so the Quest can fetch it as a
      // standard https URL via UnityWebRequestTexture.GetTexture and apply it
      // via the DrawingProjection shader. Failure here is non-fatal — we still
      // return the Meshy taskId so the model pipeline progresses.
      let imageUrl: string | null = null;
      try {
        imageUrl = await uploadImageFromDataUri(imageDataUri, `${name}_drawing`);
      } catch (e) {
        console.warn('[generate] drawing upload failed, continuing without image URL:', e);
      }
      updateSlot(sessionCode, playerSlot, {
        status: 'generating',
        name,
        taskId,
        imageUrl,
      });
    }

    return Response.json({ taskId, name, sessionCode, playerSlot });
  } catch (err) {
    return Response.json({ error: (err as Error).message }, { status: 502 });
  }
};
