// Server-side drawing preprocessor: adds a soft radial vignette inside the
// inked silhouette so Meshy's image-to-3D sees fake "rounded body" depth cues.
// Background stays pure white (so Meshy's silhouette detection isn't confused).
//
// Flow:
//   1. Decode the input data URI to raw RGBA pixels.
//   2. Build an "ink mask" + bounding box of non-white pixels.
//   3. For each ink pixel, compute its normalized distance from the silhouette
//      center and multiply its brightness by (1 - vignetteStrength * distance).
//      Edges of the silhouette get darker (suggests rounded edge falling away);
//      center stays full bright (suggests bulged-out belly).
//   4. Re-encode as PNG → return as data URI.

export interface VignetteOptions {
  vignetteStrength?: number; // 0..1, how much darkening at silhouette edge (default 0.55)
  inkThreshold?: number;     // 0..255, channel value below which a pixel counts as ink (default 240)
}

export async function applyDepthVignette(
  imageDataUri: string,
  opts: VignetteOptions = {},
): Promise<string> {
  const m = /^data:image\/[a-z]+;base64,(.+)$/i.exec(imageDataUri);
  if (!m) return imageDataUri;
  const inputBuf = Buffer.from(m[1], 'base64');

  let sharp: any;
  try {
    sharp = (await import('sharp')).default;
  } catch (e) {
    console.warn('[imagePreprocess] sharp not available, returning input unchanged:', e);
    return imageDataUri;
  }

  const strength = opts.vignetteStrength ?? 0.55;
  const inkT = opts.inkThreshold ?? 240;

  const { data, info } = await sharp(inputBuf)
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });

  const W = info.width;
  const H = info.height;
  const C = info.channels; // 4 (RGBA)

  // Pass 1: ink bounding box.
  let minX = W, minY = H, maxX = -1, maxY = -1;
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const i = (y * W + x) * C;
      const r = data[i] ?? 255;
      const g = data[i + 1] ?? 255;
      const b = data[i + 2] ?? 255;
      if (r < inkT || g < inkT || b < inkT) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }
  if (maxX < 0) {
    // No ink, return unchanged.
    return imageDataUri;
  }

  // Center + radius for the radial vignette (use silhouette bbox center,
  // not the canvas center, so off-center drawings are still vignetted around
  // their actual body).
  const cx = (minX + maxX) * 0.5;
  const cy = (minY + maxY) * 0.5;
  const radius = Math.max(maxX - cx, cy - minY, cx - minX, maxY - cy);
  const invR = 1 / Math.max(radius, 1);

  // Pass 2: darken ink pixels by vignette factor.
  const out = Buffer.from(data);
  for (let y = minY; y <= maxY; y++) {
    for (let x = minX; x <= maxX; x++) {
      const i = (y * W + x) * C;
      const r = out[i] ?? 255;
      const g = out[i + 1] ?? 255;
      const b = out[i + 2] ?? 255;
      // Skip white pixels (background).
      if (r >= inkT && g >= inkT && b >= inkT) continue;

      const dx = x - cx;
      const dy = y - cy;
      const d = Math.min(1, Math.sqrt(dx * dx + dy * dy) * invR);
      // Smooth falloff so it doesn't darken sharply.
      const t = d * d; // ease-in
      const factor = Math.max(0, 1 - strength * t);

      out[i] = Math.round(r * factor);
      out[i + 1] = Math.round(g * factor);
      out[i + 2] = Math.round(b * factor);
      // Alpha untouched.
    }
  }

  const png = await sharp(out, { raw: { width: W, height: H, channels: C } })
    .png()
    .toBuffer();

  return `data:image/png;base64,${png.toString('base64')}`;
}
