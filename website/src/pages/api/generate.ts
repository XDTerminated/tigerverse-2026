import type { APIRoute } from 'astro';
import { createImageTo3dTask } from '../../lib/meshy';
import { uploadImageFromDataUri, uploadAudioBuffer } from '../../lib/uploadthing';
import { updateSlot, presetForElement, elementFromRgb } from '../../lib/sessions';
import { dominantInkRgbFromDataUri } from '../../lib/imageColor';
import { applyDepthVignette } from '../../lib/imagePreprocess';
import { generateMonsterCry } from '../../lib/eleven';

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
    // Pre-process: add a soft radial vignette inside the inked silhouette so
    // Meshy's image-to-3D infers depth/volume from the fake shading instead of
    // returning a flat extrusion of the outline.
    let imageForMeshy = imageDataUri;
    try {
      imageForMeshy = await applyDepthVignette(imageDataUri, { vignetteStrength: 0.55 });
    } catch (e) {
      console.warn('[generate] vignette preprocess failed, using raw drawing:', e);
    }

    const taskId = await createImageTo3dTask(imageForMeshy);

    // If this submission is part of a Tigerverse session, record the taskId on
    // the right player slot so /api/session/[code] surfaces progress to the
    // Quest game and the /api/status/[taskId] route can flip the slot to
    // 'ready' once Meshy finishes.
    if (sessionCode && (playerSlot === 1 || playerSlot === 2)) {
      // Upload drawing to UploadThing for Unity to fetch.
      let imageUrl: string | null = null;
      try {
        imageUrl = await uploadImageFromDataUri(imageDataUri, `${name}_drawing`);
      } catch (e) {
        console.warn('[generate] drawing upload failed, continuing without image URL:', e);
      }

      // Derive element + stats from the drawing's dominant ink color.
      let preset = presetForElement('neutral');
      try {
        const ink = await dominantInkRgbFromDataUri(imageDataUri);
        const element = elementFromRgb(ink.r, ink.g, ink.b);
        preset = presetForElement(element);
        console.log(`[generate] ink=rgb(${ink.r},${ink.g},${ink.b}) → element=${element} → moves=${preset.moves.join(',')}`);
      } catch (e) {
        console.warn('[generate] color analysis failed, using neutral:', e);
      }

      // Inject the player's chosen name into the flavor text for personality.
      preset.flavorText = `${name}: ${preset.flavorText}`;

      // Fire-and-forget: ElevenLabs Sound Effects → mp3 → UploadThing → write cryUrl onto slot.
      // The Quest polls every 3s; whenever this resolves, the next poll picks it up.
      (async () => {
        try {
          const audio = await generateMonsterCry(name, preset.element);
          const cryUrl = await uploadAudioBuffer(audio, `${name}_cry`);
          updateSlot(sessionCode, playerSlot, { cryUrl });
          console.log(`[generate] cry uploaded for ${name}: ${cryUrl}`);
        } catch (e) {
          console.warn('[generate] cry generation/upload failed:', e);
        }
      })();

      updateSlot(sessionCode, playerSlot, {
        status: 'generating',
        name,
        taskId,
        imageUrl,
        stats: preset, // available immediately so Unity can announce moves before Meshy finishes
      });
    }

    return Response.json({ taskId, name, sessionCode, playerSlot });
  } catch (err) {
    return Response.json({ error: (err as Error).message }, { status: 502 });
  }
};
