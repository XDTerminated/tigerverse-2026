import { MESHY_API_KEY } from 'astro:env/server';

const BASE = 'https://api.meshy.ai/openapi/v1';

export type MeshyStatus = 'PENDING' | 'IN_PROGRESS' | 'SUCCEEDED' | 'FAILED' | 'CANCELED' | 'EXPIRED';

export interface MeshyTask {
  id: string;
  status: MeshyStatus;
  progress: number;
  model_urls?: { glb?: string; fbx?: string; obj?: string; usdz?: string };
  thumbnail_url?: string;
  task_error?: { message: string };
}

export async function createImageTo3dTask(imageDataUri: string): Promise<string> {
  const res = await fetch(`${BASE}/image-to-3d`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${MESHY_API_KEY}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      image_url: imageDataUri,
      should_texture: false,
      target_formats: ['glb'],
      ai_model: 'meshy-5',
      should_remesh: false,
    }),
  });

  if (!res.ok) {
    throw new Error(`Meshy create task failed: ${res.status} ${await res.text()}`);
  }

  const data = (await res.json()) as { result: string };
  return data.result;
}

export async function getTask(taskId: string): Promise<MeshyTask> {
  const res = await fetch(`${BASE}/image-to-3d/${taskId}`, {
    headers: { Authorization: `Bearer ${MESHY_API_KEY}` },
  });

  if (!res.ok) {
    throw new Error(`Meshy get task failed: ${res.status} ${await res.text()}`);
  }

  return (await res.json()) as MeshyTask;
}
