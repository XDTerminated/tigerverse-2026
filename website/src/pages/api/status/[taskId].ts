import type { APIRoute } from 'astro';
import { getTask } from '../../../lib/meshy';
import { uploadGlbFromUrl } from '../../../lib/uploadthing';
import { findSlotByTaskId, updateSlot, nextPreset } from '../../../lib/sessions';

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

      // If this taskId is bound to a Tigerverse session, mark the slot ready.
      const owner = findSlotByTaskId(taskId);
      if (owner) {
        updateSlot(owner.code, owner.slot, {
          status: 'ready',
          glbUrl: permanentUrl,
          stats: nextPreset(),
        });
      }

      return Response.json({
        status: 'SUCCEEDED',
        progress: 100,
        modelUrl: permanentUrl,
        thumbnailUrl: task.thumbnail_url,
      });
    }

    if (task.status === 'FAILED' || task.status === 'CANCELED' || task.status === 'EXPIRED') {
      const owner = findSlotByTaskId(taskId);
      if (owner) {
        updateSlot(owner.code, owner.slot, { status: 'error' });
      }
      return Response.json({
        status: task.status,
        error: task.task_error?.message ?? task.status,
      });
    }

    // In-progress: bubble the Meshy phase into our session as a coarse status.
    const owner = findSlotByTaskId(taskId);
    if (owner) {
      const meshyToInternal: Record<string, 'generating' | 'rigging'> = {
        PENDING: 'generating',
        IN_PROGRESS: 'generating',
        QUEUED: 'generating',
      };
      const internal = meshyToInternal[task.status] ?? 'generating';
      updateSlot(owner.code, owner.slot, { status: internal });
    }

    return Response.json({
      status: task.status,
      progress: task.progress ?? 0,
    });
  } catch (err) {
    return Response.json({ error: (err as Error).message }, { status: 502 });
  }
};
