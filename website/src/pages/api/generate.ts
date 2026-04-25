import type { APIRoute } from 'astro';
import { createImageTo3dTask } from '../../lib/meshy';

export const prerender = false;

export const POST: APIRoute = async ({ request }) => {
  let body: { imageDataUri?: string; name?: string };
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: 'Invalid JSON' }, { status: 400 });
  }

  const { imageDataUri, name } = body;
  if (!imageDataUri || !imageDataUri.startsWith('data:image/')) {
    return Response.json({ error: 'imageDataUri (data URI) required' }, { status: 400 });
  }
  if (!name || typeof name !== 'string') {
    return Response.json({ error: 'name required' }, { status: 400 });
  }

  try {
    const taskId = await createImageTo3dTask(imageDataUri);
    return Response.json({ taskId, name });
  } catch (err) {
    return Response.json({ error: (err as Error).message }, { status: 502 });
  }
};
