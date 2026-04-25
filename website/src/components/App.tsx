import { useEffect, useRef, useState } from 'react';
import { DrawingCanvas, type DrawingCanvasHandle, type CanvasState } from './DrawingCanvas';
import { Icon } from './Icon';

const COLORS = [
  { name: 'black', value: '#000000' },
  { name: 'red', value: '#DC2626' },
  { name: 'blue', value: '#2563EB' },
  { name: 'green', value: '#16A34A' },
];

type Phase =
  | { kind: 'drawing' }
  | {
      kind: 'generating';
      taskId: string;
      name: string;
      progress: number;
      status: string;
    }
  | { kind: 'done'; modelUrl: string; name: string }
  | { kind: 'error'; message: string };

export default function App() {
  const [phase, setPhase] = useState<Phase>({ kind: 'drawing' });
  const [name, setName] = useState('');
  const [brushSize, setBrushSize] = useState(12);
  const [brushColor, setBrushColor] = useState(COLORS[0].value);
  const [isErasing, setIsErasing] = useState(false);
  const [canvasState, setCanvasState] = useState<CanvasState>({
    canUndo: false,
    canRedo: false,
    hasInk: false,
  });
  const [showClearConfirm, setShowClearConfirm] = useState(false);
  const [previewDataUri, setPreviewDataUri] = useState<string | null>(null);
  const [transparentDataUri, setTransparentDataUri] = useState<string | null>(null);
  const canvasRef = useRef<DrawingCanvasHandle>(null);

  const handleFinish = () => {
    if (!canvasRef.current || canvasRef.current.isEmpty()) return;
    const dataUri = canvasRef.current.toPngDataUri();
    setPreviewDataUri(dataUri);
  };

  const closePreview = () => {
    setPreviewDataUri(null);
    setTransparentDataUri(null);
  };

  useEffect(() => {
    if (!previewDataUri) {
      setTransparentDataUri(null);
      return;
    }
    let cancelled = false;
    const img = new window.Image();
    img.onload = () => {
      if (cancelled) return;

      // Step 1: render source to a working canvas so we can read pixels.
      const work = document.createElement('canvas');
      work.width = img.width;
      work.height = img.height;
      const wctx = work.getContext('2d');
      if (!wctx) return;
      wctx.drawImage(img, 0, 0);
      const sourceData = wctx.getImageData(0, 0, work.width, work.height);
      const sp = sourceData.data;

      // Step 2: find the bounding box of non-white ink so we can trim the
      // surrounding margin from the 3D preview. Pixels with any channel
      // significantly below 255 count as ink.
      const W = work.width;
      const H = work.height;
      let minX = W;
      let minY = H;
      let maxX = -1;
      let maxY = -1;
      const INK_THRESHOLD = 240;
      for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
          const i = (y * W + x) * 4;
          if (sp[i] < INK_THRESHOLD || sp[i + 1] < INK_THRESHOLD || sp[i + 2] < INK_THRESHOLD) {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
          }
        }
      }

      // Empty canvas — nothing to show.
      if (maxX < 0 || maxY < 0) {
        setTransparentDataUri(null);
        return;
      }

      // Step 3: pad slightly so strokes don't sit flush against the edges.
      const pad = Math.round(Math.max(W, H) * 0.02);
      minX = Math.max(0, minX - pad);
      minY = Math.max(0, minY - pad);
      maxX = Math.min(W - 1, maxX + pad);
      maxY = Math.min(H - 1, maxY + pad);

      const cropW = maxX - minX + 1;
      const cropH = maxY - minY + 1;

      // Step 4: copy the cropped region into a square canvas, centered with
      // letterbox so the rotation in the 3D preview spins around the doodle's
      // visual center. Square also matches what Meshy expects.
      const SIZE = Math.max(cropW, cropH);
      const out = document.createElement('canvas');
      out.width = SIZE;
      out.height = SIZE;
      const octx = out.getContext('2d');
      if (!octx) return;
      octx.fillStyle = '#ffffff';
      octx.fillRect(0, 0, SIZE, SIZE);
      const dx = Math.round((SIZE - cropW) / 2);
      const dy = Math.round((SIZE - cropH) / 2);
      octx.drawImage(work, minX, minY, cropW, cropH, dx, dy, cropW, cropH);

      // Step 5: convert white → transparent, preserving stroke color.
      const data = octx.getImageData(0, 0, SIZE, SIZE);
      const px = data.data;
      for (let i = 0; i < px.length; i += 4) {
        px[i + 3] = Math.max(255 - px[i], 255 - px[i + 1], 255 - px[i + 2]);
      }
      octx.putImageData(data, 0, 0);
      setTransparentDataUri(out.toDataURL('image/png'));
    };
    img.src = previewDataUri;
    return () => {
      cancelled = true;
    };
  }, [previewDataUri]);

  const handleSubmit = async () => {
    const trimmed = name.trim();
    if (!previewDataUri || !trimmed) return;

    const dataUri = previewDataUri;
    closePreview();

    try {
      const res = await fetch('/api/generate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ imageDataUri: dataUri, name: trimmed }),
      });
      const data = await res.json();
      if (!res.ok) {
        setPhase({ kind: 'error', message: data.error ?? `HTTP ${res.status}` });
        return;
      }
      setPhase({
        kind: 'generating',
        taskId: data.taskId,
        name: trimmed,
        progress: 0,
        status: 'PENDING',
      });
    } catch (err) {
      setPhase({ kind: 'error', message: (err as Error).message });
    }
  };

  useEffect(() => {
    if (phase.kind !== 'generating') return;

    let cancelled = false;
    const poll = async () => {
      try {
        const res = await fetch(
          `/api/status/${phase.taskId}?name=${encodeURIComponent(phase.name)}`,
        );
        const data = await res.json();
        if (cancelled) return;

        if (data.status === 'SUCCEEDED' && data.modelUrl) {
          setPhase({ kind: 'done', modelUrl: data.modelUrl, name: phase.name });
          return;
        }
        if (data.status === 'FAILED' || data.status === 'CANCELED' || data.status === 'EXPIRED') {
          setPhase({ kind: 'error', message: data.error ?? data.status });
          return;
        }
        setPhase({
          kind: 'generating',
          taskId: phase.taskId,
          name: phase.name,
          progress: data.progress ?? 0,
          status: data.status ?? 'IN_PROGRESS',
        });
      } catch (err) {
        if (!cancelled) setPhase({ kind: 'error', message: (err as Error).message });
      }
    };

    const id = setInterval(poll, 5000);
    void poll();
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [phase.kind === 'generating' ? phase.taskId : null]);

  const reset = () => {
    canvasRef.current?.clear();
    setName('');
    setPhase({ kind: 'drawing' });
  };

  return (
    <>
      {phase.kind === 'drawing' && (
        <div className="fixed inset-0 flex flex-row bg-white text-black">
          {/* Left sidebar — same thickness as the bottom bar (80px) */}
          <div className="shrink-0 w-20 flex flex-col items-center gap-3 py-4 border-r-2 border-black">
            {/* Colors container — fixed height (4 swatches × 44px + 3 gaps × 12px = 212)
                so the rest of the sidebar doesn't shift when colors slide away. */}
            <div className="relative" style={{ width: 44, height: 212 }}>
              {COLORS.map((c, i) => (
                <button
                  key={c.value}
                  onClick={() => {
                    setBrushColor(c.value);
                    setIsErasing(false);
                  }}
                  aria-label={c.name}
                  title={c.name}
                  className={`absolute w-11 h-11 rounded-full border-2 border-black transition-all duration-300 ease-out ${
                    brushColor === c.value && !isErasing
                      ? 'ring-2 ring-black ring-offset-2 scale-110'
                      : ''
                  }`}
                  style={{
                    top: i * 56,
                    backgroundColor: c.value,
                    /* When erasing, every swatch slides down to land at the
                       pencil button's position. Pencil's bg-white + higher
                       z-index masks them as they tuck in. */
                    transform: isErasing ? `translateY(${(4 - i) * 56}px)` : 'translateY(0)',
                    pointerEvents: isErasing ? 'none' : 'auto',
                  }}
                />
              ))}
            </div>

            <button
              onClick={() => setIsErasing(false)}
              aria-label="Pencil"
              title="Pencil"
              className={`relative z-10 w-11 h-11 flex items-center justify-center border-2 border-black rounded-sm active:scale-90 transition-all duration-100 ${
                !isErasing
                  ? 'ring-2 ring-black ring-offset-2 scale-110 bg-black text-white'
                  : 'bg-white hover:bg-black hover:text-white'
              }`}
            >
              <Icon name="pencil" className="w-6 h-6" />
            </button>
            <button
              onClick={() => setIsErasing(true)}
              aria-label="Eraser"
              title="Eraser"
              className={`w-11 h-11 flex items-center justify-center border-2 border-black rounded-sm active:scale-90 transition-all duration-100 ${
                isErasing
                  ? 'ring-2 ring-black ring-offset-2 scale-110 bg-black text-white'
                  : 'hover:bg-black hover:text-white'
              }`}
            >
              <Icon name="eraser" className="w-6 h-6" />
            </button>

            <div className="relative mt-2 h-48 w-full">
              <div className="brush-slider-wrap absolute top-0" style={{ left: '0.75rem' }}>
                <input
                  type="range"
                  min={1}
                  max={48}
                  value={brushSize}
                  onChange={(e) => setBrushSize(Number(e.target.value))}
                  className="accent-black"
                  aria-label="Brush size"
                />
              </div>
              {/* Tick numbers — anchored just to the right of the slider wrapper
                  (slider wrapper is 32px wide, sits at left:0.75rem, so ticks
                  start at left:0.75rem + 32px + 4px gap). Heights match
                  exactly so the calc resolves to the same pixel positions as
                  the thumb. ratio=0 → bottom, ratio=1 → top. */}
              <div
                className="absolute top-0 h-48 pointer-events-none text-sm leading-none"
                style={{ left: 'calc(0.75rem + 32px + 4px)' }}
              >
                {[1, 12, 24, 36, 48].map((v) => {
                  const ratio = (v - 1) / 47;
                  return (
                    <span
                      key={v}
                      className="absolute -translate-y-1/2"
                      style={{ top: `calc(14px + (12rem - 28px) * ${1 - ratio})` }}
                    >
                      {v}
                    </span>
                  );
                })}
              </div>
            </div>

            <span className="text-2xl leading-none">{brushSize}</span>
          </div>

          {/* Main column: canvas + bottom bar */}
          <div className="flex-1 flex flex-col min-w-0">
            <div className="flex-1 min-h-0">
              <DrawingCanvas
                ref={canvasRef}
                brushSize={brushSize}
                brushColor={isErasing ? '#ffffff' : brushColor}
                onStateChange={setCanvasState}
              />
            </div>

            <div className="shrink-0 h-20 px-5 flex items-center gap-3 border-t-2 border-black bg-white">
              <button
                onClick={() => canvasRef.current?.undo()}
                className="h-12 w-12 flex items-center justify-center border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100"
                aria-label="Undo"
                title="Undo"
              >
                <Icon name="arrow-left" className="w-7 h-7" />
              </button>
              <button
                onClick={() => canvasRef.current?.redo()}
                className="h-12 w-12 flex items-center justify-center border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100"
                aria-label="Redo"
                title="Redo"
              >
                <Icon name="arrow-right" className="w-7 h-7" />
              </button>
              <button
                onClick={() => {
                  if (canvasState.canUndo) setShowClearConfirm(true);
                }}
                className="h-12 w-12 flex items-center justify-center border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100"
                aria-label="Clear"
                title="Clear"
              >
                <Icon name="delete" className="w-7 h-7" />
              </button>

              <div className="flex-1" />

              {canvasState.hasInk && (
                <button
                  onClick={handleFinish}
                  className="h-12 px-6 bg-black text-white text-xl rounded-sm hover:bg-neutral-800 active:scale-95 transition-all duration-100 flex items-center gap-2"
                >
                  <Icon name="tick" className="w-6 h-6" />
                  finish
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {phase.kind !== 'drawing' && (
        <div className="min-h-screen flex flex-col items-center justify-center px-6 py-12 bg-white text-black">
          <h1 className="text-5xl mb-10 tracking-tight">doodle to 3d</h1>

          {phase.kind === 'generating' && (
            <div className="w-full max-w-md flex flex-col items-center gap-5">
              <Icon name="sync" spin className="w-12 h-12" />
              <div className="text-2xl text-center">generating "{phase.name}"…</div>
              <div className="text-xl">
                {phase.status} · {phase.progress}%
              </div>
              <div className="w-full h-3 border-2 border-black overflow-hidden rounded-sm">
                <div
                  className="h-full bg-black transition-all duration-300"
                  style={{ width: `${phase.progress}%` }}
                />
              </div>
              <div className="text-sm opacity-60">this usually takes 1–3 minutes</div>
            </div>
          )}

          {phase.kind === 'done' && (
            <div className="w-full max-w-md flex flex-col items-center gap-6">
              <Icon name="tick" className="w-12 h-12" />
              <div className="text-3xl">{phase.name}</div>
              <div className="flex gap-3 flex-wrap justify-center">
                <a
                  href={phase.modelUrl}
                  download={`${phase.name}.glb`}
                  className="h-12 px-6 bg-black text-white rounded-sm hover:bg-neutral-800 active:scale-95 transition-all duration-100 flex items-center gap-2 text-lg"
                >
                  <Icon name="download" className="w-6 h-6" />
                  download .glb
                </a>
                <button
                  onClick={reset}
                  className="h-12 px-6 border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100 flex items-center gap-2 text-lg"
                >
                  <Icon name="pencil" className="w-6 h-6" />
                  make another
                </button>
              </div>
            </div>
          )}

          {phase.kind === 'error' && (
            <div className="w-full max-w-md flex flex-col items-center gap-5">
              <div className="border-2 border-black rounded-sm p-5 w-full">
                <div className="text-sm uppercase tracking-wide mb-2 opacity-60">error</div>
                <div className="text-base">{phase.message}</div>
              </div>
              <button
                onClick={reset}
                className="h-12 px-6 border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100 flex items-center gap-2 text-lg"
              >
                <Icon name="backward" className="w-6 h-6" />
                try again
              </button>
            </div>
          )}
        </div>
      )}

      {previewDataUri && (
        <div
          className="fixed inset-0 bg-black/50 flex items-center justify-center px-4 z-50"
          onClick={closePreview}
        >
          <div
            className="bg-white border-2 border-black rounded-sm p-7 w-full max-w-3xl flex flex-col gap-5"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="text-3xl text-center">how it looks</div>

            <div
              className="mx-auto"
              style={{
                perspective: '1400px',
                width: 'min(56vh, 70vw, 520px)',
                height: 'min(56vh, 70vw, 520px)',
              }}
            >
              <div
                className="relative w-full h-full animate-spin-3d"
                style={{ transformStyle: 'preserve-3d' }}
              >
                {transparentDataUri && (
                  <>
                    <img
                      src={transparentDataUri}
                      alt="Your doodle"
                      className="absolute inset-0 w-full h-full object-contain"
                      style={{ backfaceVisibility: 'hidden' }}
                    />
                    <img
                      src={transparentDataUri}
                      alt=""
                      aria-hidden="true"
                      className="absolute inset-0 w-full h-full object-contain"
                      style={{
                        backfaceVisibility: 'hidden',
                        transform: 'rotateY(180deg) scaleX(-1)',
                      }}
                    />
                  </>
                )}
              </div>
            </div>

            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && name.trim()) {
                  e.preventDefault();
                  void handleSubmit();
                }
              }}
              placeholder="name your doodle"
              className="w-full h-12 px-4 border-2 border-black rounded-sm focus:outline-none text-lg bg-white"
            />

            <div className="flex gap-3 items-center w-full">
              <button
                onClick={closePreview}
                className="flex-1 h-12 px-5 border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100 text-lg"
              >
                back
              </button>
              <button
                onClick={handleSubmit}
                disabled={!name.trim()}
                className="flex-1 h-12 px-5 bg-black text-white rounded-sm hover:bg-neutral-800 disabled:opacity-30 disabled:cursor-not-allowed active:scale-95 transition-all duration-100 flex items-center justify-center gap-2 text-lg"
              >
                <Icon name="magic-wand" className="w-6 h-6" />
                bring my doodle to life
              </button>
            </div>
          </div>
        </div>
      )}

      {showClearConfirm && (
        <div
          className="fixed inset-0 bg-black/50 flex items-center justify-center px-4 z-50"
          onClick={() => setShowClearConfirm(false)}
        >
          <div
            className="bg-white border-2 border-black rounded-sm p-7 w-full max-w-sm"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="text-3xl mb-3">hold up</div>
            <div className="text-lg mb-6 opacity-80">
              are u sure u wanna delete your doodle?
            </div>
            <div className="flex gap-3 justify-end items-center">
              <button
                onClick={() => setShowClearConfirm(false)}
                className="h-12 px-5 border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100 text-lg"
              >
                nvm
              </button>
              <button
                onClick={() => {
                  canvasRef.current?.clear();
                  setShowClearConfirm(false);
                }}
                className="h-12 px-5 bg-black text-white rounded-sm hover:bg-neutral-800 active:scale-95 transition-all duration-100 flex items-center gap-2 text-lg"
              >
                <Icon name="delete" className="w-6 h-6" />
                delete
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
