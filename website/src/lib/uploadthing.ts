import { UTApi, UTFile } from 'uploadthing/server';
import { UPLOADTHING_TOKEN } from 'astro:env/server';

const utapi = new UTApi({ token: UPLOADTHING_TOKEN });

export async function uploadGlbFromUrl(sourceUrl: string, name: string): Promise<string> {
  const sourceRes = await fetch(sourceUrl);
  if (!sourceRes.ok) {
    throw new Error(`Failed to fetch source GLB: ${sourceRes.status}`);
  }
  const buffer = await sourceRes.arrayBuffer();
  const safeName = `${name.replace(/[^a-z0-9-_]+/gi, '_').slice(0, 64) || 'model'}.glb`;

  const file = new UTFile([buffer], safeName, { type: 'model/gltf-binary' });
  const result = await utapi.uploadFiles(file);

  if (result.error) {
    throw new Error(`UploadThing failed: ${result.error.message}`);
  }
  return result.data.ufsUrl;
}
