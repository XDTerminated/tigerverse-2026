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
      const c = document.createElement('canvas');
      c.width = img.width;
      c.height = img.height;
      const ctx = c.getContext('2d');
      if (!ctx) return;
      ctx.drawImage(img, 0, 0);
      const data = ctx.getImageData(0, 0, c.width, c.height);
      const px = data.data;
      for (let i = 0; i < px.length; i += 4) {
        const r = px[i];
        const g = px[i + 1];
        const b = px[i + 2];
        // Alpha = how far this pixel is from white (max channel deviation).
        // Pure white => alpha 0 (transparent). Saturated colors => alpha 255.
        // Anti-aliased edges blend smoothly. RGB values stay so colors render.
        px[i + 3] = Math.max(255 - r, 255 - g, 255 - b);
      }
      ctx.putImageData(data, 0, 0);
      setTransparentDataUri(c.toDataURL('image/png'));
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
          {/* Left sidebar */}
          <div className="shrink-0 w-40 flex flex-col items-center gap-4 py-5 border-r-2 border-black">
            {COLORS.map((c) => (
              <button
                key={c.value}
                onClick={() => {
                  setBrushColor(c.value);
                  setIsErasing(false);
                }}
                aria-label={c.name}
                title={c.name}
                className={`relative w-11 h-11 rounded-full border-2 border-black active:scale-90 transition-all duration-100 ${
                  brushColor === c.value && !isErasing
                    ? 'ring-2 ring-black ring-offset-2 scale-110'
                    : 'hover:scale-105'
                }`}
                style={{ backgroundColor: c.value }}
              >
                {isErasing && (
                  <span
                    aria-hidden="true"
                    className="pointer-events-none absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[140%] h-0.75 bg-black rotate-45 origin-center"
                  />
                )}
              </button>
            ))}

            <button
              onClick={() => setIsErasing((v) => !v)}
              aria-label="Eraser"
              title="Eraser"
              className={`w-11 h-11 flex items-center justify-center border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-90 transition-all duration-100 ${
                isErasing ? 'ring-2 ring-black ring-offset-2 scale-110 bg-black text-white' : ''
              }`}
            >
              <Icon name="eraser" className="w-6 h-6" />
            </button>

            <div className="w-full px-3 mt-2 flex flex-col items-center gap-2">
              <Icon name="pencil" className="w-6 h-6" />
              <div className="relative w-full pt-5">
                <div className="absolute top-0 left-0 right-0 h-3 pointer-events-none text-sm leading-none">
                  {[1, 12, 24, 36, 48].map((v) => {
                    const ratio = (v - 1) / 47;
                    return (
                      <span
                        key={v}
                        className="absolute top-0 -translate-x-1/2"
                        style={{ left: `calc(14px + (100% - 28px) * ${ratio})` }}
                      >
                        {v}
                      </span>
                    );
                  })}
                </div>
                <input
                  type="range"
                  min={1}
                  max={48}
                  value={brushSize}
                  onChange={(e) => setBrushSize(Number(e.target.value))}
                  className="w-full accent-black block"
                  aria-label="Brush size"
                />
              </div>
              <span className="text-2xl leading-none">{brushSize}</span>
            </div>
          </div>

          {/* Main column: canvas + bottom bar */}
          <div className="flex-1 flex flex-col min-w-0">
            <div className="flex-1 min-h-0 relative">
              <DrawingCanvas
                ref={canvasRef}
                brushSize={brushSize}
                brushColor={isErasing ? '#ffffff' : brushColor}
                onStateChange={setCanvasState}
              />

              {canvasState.canUndo && (
                <button
                  onClick={handleFinish}
                  className="absolute bottom-4 left-1/2 -translate-x-1/2 h-12 px-6 bg-black text-white text-xl rounded-sm hover:bg-neutral-800 active:scale-95 transition-all duration-100 flex items-center gap-2 shadow-lg"
                >
                  <Icon name="tick" className="w-6 h-6" />
                  finish
                </button>
              )}
            </div>

            <div className="shrink-0 h-20 px-5 flex items-center gap-3 border-t-2 border-black bg-white justify-end">
              <button
                onClick={() => canvasRef.current?.undo()}
                className="h-12 w-12 flex items-center justify-center border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100"
                aria-label="Undo"
                title="Undo"
              >
                <Icon name="backward" className="w-7 h-7" />
              </button>
              <button
                onClick={() => canvasRef.current?.redo()}
                className="h-12 w-12 flex items-center justify-center border-2 border-black rounded-sm hover:bg-black hover:text-white active:scale-95 transition-all duration-100"
                aria-label="Redo"
                title="Redo"
              >
                <Icon name="forward" className="w-7 h-7" />
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
