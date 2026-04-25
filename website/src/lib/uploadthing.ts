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

// Upload a raw audio buffer (mp3) to UploadThing and return its public URL.
export async function uploadAudioBuffer(buffer: Buffer, name: string, ext = 'mp3'): Promise<string> {
  const safeName = `${name.replace(/[^a-z0-9-_]+/gi, '_').slice(0, 64) || 'audio'}.${ext}`;
  const file = new UTFile([buffer], safeName, { type: ext === 'mp3' ? 'audio/mpeg' : 'audio/wav' });
  const result = await utapi.uploadFiles(file);
  if (result.error) throw new Error(`UploadThing audio failed: ${result.error.message}`);
  return result.data.ufsUrl;
}

// Upload a base64 data URI (data:image/png;base64,...) to UploadThing and
// return the public URL. Used to host the drawing image so the Quest can
// fetch it via UnityWebRequestTexture.
export async function uploadImageFromDataUri(dataUri: string, name: string): Promise<string> {
  const m = /^data:(image\/[a-z]+);base64,(.+)$/i.exec(dataUri);
  if (!m) throw new Error('Invalid image data URI');
  const mime = m[1];
  const ext = mime.split('/')[1] ?? 'png';
  const buffer = Buffer.from(m[2], 'base64');
  const safeName = `${name.replace(/[^a-z0-9-_]+/gi, '_').slice(0, 64) || 'drawing'}.${ext}`;
  const file = new UTFile([buffer], safeName, { type: mime });
  const result = await utapi.uploadFiles(file);
  if (result.error) {
    throw new Error(`UploadThing image failed: ${result.error.message}`);
  }
  return result.data.ufsUrl;
}
