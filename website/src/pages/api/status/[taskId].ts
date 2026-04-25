import type { APIRoute } from 'astro';
import { getTask } from '../../../lib/meshy';
import { uploadGlbFromUrl } from '../../../lib/uploadthing';

export const prerender = false;

export const GET: APIRoute = async ({ params, url }) => {
  const taskId = params.taskId;
  if (!taskId) {
    return Response.json({ error: 'taskId required' }, { status: 400 });
  }

  const name = url.searchParams.get('name') ?? 'model';

  try {
    const task = await getTask(taskId);

    if (task.status === 'SUCCEEDED') {
      const glbUrl = task.model_urls?.glb;
      if (!glbUrl) {
        return Response.json(
          { status: 'FAILED', error: 'Meshy returned no GLB URL' },
          { status: 502 },
        );
      }
      const permanentUrl = await uploadGlbFromUrl(glbUrl, name);
      return Response.json({
        status: 'SUCCEEDED',
        progress: 100,
        modelUrl: permanentUrl,
        thumbnailUrl: task.thumbnail_url,
      });
    }

    if (task.status === 'FAILED' || task.status === 'CANCELED' || task.status === 'EXPIRED') {
      return Response.json({
        status: task.status,
        error: task.task_error?.message ?? task.status,
      });
    }

    return Response.json({
      status: task.status,
      progress: task.progress ?? 0,
    });
  } catch (err) {
    return Response.json({ error: (err as Error).message }, { status: 502 });
  }
};
