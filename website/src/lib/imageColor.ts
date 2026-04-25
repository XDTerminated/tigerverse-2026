// Server-side dominant-ink-color extraction from a base64 PNG/JPEG data URI.
// Pure JS PNG/JPEG decoding is heavy, so we cheat: we decode just enough to
// sample pixels via the lightweight `sharp` package. If sharp isn't available
// (e.g. someone strips it from deps), we fall back to a constant.

export interface InkRgb { r: number; g: number; b: number }

/**
 * Walks the image, ignores near-white background pixels, and returns the
 * average RGB of the ink. Returns medium gray if no ink is detected.
 */
export async function dominantInkRgbFromDataUri(dataUri: string): Promise<InkRgb> {
  const m = /^data:image\/[a-z]+;base64,(.+)$/i.exec(dataUri);
  if (!m) return { r: 128, g: 128, b: 128 };
  const buf = Buffer.from(m[1], 'base64');

  let sharp: any;
  try {
    sharp = (await import('sharp')).default;
  } catch (e) {
    console.warn('[imageColor] sharp not available, returning gray fallback:', e);
    return { r: 128, g: 128, b: 128 };
  }

  // Resize down for speed (we only need a coarse average), strip alpha to RGB.
  const { data, info } = await sharp(buf)
    .resize(64, 64, { fit: 'inside' })
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });

  const w = info.width;
  const h = info.height;
  const channels = info.channels; // RGBA = 4
  const INK_THRESHOLD = 240; // any channel < this counts as ink
  let r = 0, g = 0, b = 0, count = 0;

  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * channels;
      const pr = data[i] ?? 255;
      const pg = data[i + 1] ?? 255;
      const pb = data[i + 2] ?? 255;
      const pa = channels >= 4 ? (data[i + 3] ?? 255) : 255;
      if (pa < 50) continue; // transparent
      if (pr < INK_THRESHOLD || pg < INK_THRESHOLD || pb < INK_THRESHOLD) {
        r += pr; g += pg; b += pb; count++;
      }
    }
  }

  if (count === 0) return { r: 128, g: 128, b: 128 };
  return {
    r: Math.round(r / count),
    g: Math.round(g / count),
    b: Math.round(b / count),
  };
}
