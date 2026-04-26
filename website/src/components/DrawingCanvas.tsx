import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
} from 'react';

export interface CanvasState {
  canUndo: boolean;
  canRedo: boolean;
  hasInk: boolean;
}

export interface DrawingCanvasHandle {
  toPngDataUri: () => string;
  isEmpty: () => boolean;
  clear: () => void;
  undo: () => void;
  redo: () => void;
}

interface Props {
  brushSize: number;
  brushColor: string;
  onStateChange?: (state: CanvasState) => void;
}

type Point = { x: number; y: number };
type Stroke = { points: Point[]; size: number; color: string };

const EXPORT_SIZE = 1024;

export const DrawingCanvas = forwardRef<DrawingCanvasHandle, Props>(function DrawingCanvas(
  { brushSize, brushColor, onStateChange },
  ref,
) {
  const wrapperRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const strokesRef = useRef<Stroke[]>([]);
  const redoStackRef = useRef<Stroke[]>([]);
  const drawingRef = useRef<Stroke | null>(null);
  const brushSizeRef = useRef(brushSize);
  const brushColorRef = useRef(brushColor);
  const onStateChangeRef = useRef(onStateChange);
  const [previewPos, setPreviewPos] = useState<{ x: number; y: number } | null>(null);

  useEffect(() => {
    brushSizeRef.current = brushSize;
  }, [brushSize]);

  useEffect(() => {
    brushColorRef.current = brushColor;
  }, [brushColor]);

  useEffect(() => {
    onStateChangeRef.current = onStateChange;
  }, [onStateChange]);

  const checkHasInk = (): boolean => {
    const canvas = canvasRef.current;
    if (!canvas || canvas.width === 0 || canvas.height === 0) return false;
    // Downsample to a 64x64 buffer and check for any non-white pixel.
    // Cheap enough to run after each stroke; correct under arbitrary erase
    // sequences that might leave the canvas visually blank despite strokes
    // still being in the array.
    const SAMPLE = 64;
    const off = document.createElement('canvas');
    off.width = SAMPLE;
    off.height = SAMPLE;
    const ctx = off.getContext('2d');
    if (!ctx) return false;
    ctx.drawImage(canvas, 0, 0, SAMPLE, SAMPLE);
    const data = ctx.getImageData(0, 0, SAMPLE, SAMPLE).data;
    for (let i = 0; i < data.length; i += 4) {
      if (data[i] < 240 || data[i + 1] < 240 || data[i + 2] < 240) return true;
    }
    return false;
  };

  const notifyState = () => {
    onStateChangeRef.current?.({
      canUndo: strokesRef.current.length > 0,
      canRedo: redoStackRef.current.length > 0,
      hasInk: checkHasInk(),
    });
  };

  const redraw = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    const all = drawingRef.current
      ? [...strokesRef.current, drawingRef.current]
      : strokesRef.current;

    for (const stroke of all) {
      if (stroke.points.length === 0) continue;
      ctx.strokeStyle = stroke.color;
      ctx.lineWidth = stroke.size;
      ctx.beginPath();
      const [first, ...rest] = stroke.points;
      ctx.moveTo(first.x, first.y);
      if (rest.length === 0) {
        ctx.lineTo(first.x + 0.01, first.y + 0.01);
      } else {
        for (const p of rest) ctx.lineTo(p.x, p.y);
      }
      ctx.stroke();
    }
  };

  useLayoutEffect(() => {
    const wrapper = wrapperRef.current;
    const canvas = canvasRef.current;
    if (!wrapper || !canvas) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.floor(wrapper.clientWidth * dpr));
    canvas.height = Math.max(1, Math.floor(wrapper.clientHeight * dpr));
    redraw();
    notifyState();
  }, []);

  useImperativeHandle(
    ref,
    () => ({
      toPngDataUri: () => {
        const source = canvasRef.current;
        if (!source) return '';
        const off = document.createElement('canvas');
        off.width = EXPORT_SIZE;
        off.height = EXPORT_SIZE;
        const ctx = off.getContext('2d');
        if (!ctx) return '';
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, EXPORT_SIZE, EXPORT_SIZE);
        const aspect = source.width / source.height;
        let dw: number;
        let dh: number;
        if (aspect > 1) {
          dw = EXPORT_SIZE;
          dh = EXPORT_SIZE / aspect;
        } else {
          dh = EXPORT_SIZE;
          dw = EXPORT_SIZE * aspect;
        }
        const dx = (EXPORT_SIZE - dw) / 2;
        const dy = (EXPORT_SIZE - dh) / 2;
        ctx.drawImage(source, dx, dy, dw, dh);
        return off.toDataURL('image/png');
      },
      isEmpty: () => strokesRef.current.length === 0,
      clear: () => {
        strokesRef.current = [];
        redoStackRef.current = [];
        drawingRef.current = null;
        redraw();
        notifyState();
      },
      undo: () => {
        const popped = strokesRef.current.pop();
        if (popped) redoStackRef.current.push(popped);
        redraw();
        notifyState();
      },
      redo: () => {
        const popped = redoStackRef.current.pop();
        if (popped) strokesRef.current.push(popped);
        redraw();
        notifyState();
      },
    }),
    [],
  );

  // Native event listeners, React 19 attaches touch listeners as passive,
  // which makes preventDefault() a silent no-op on iOS. Attaching directly
  // with { passive: false } is the only reliable way to capture Apple
  // Pencil and finger input on iPad without the browser hijacking it.
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const getPoint = (clientX: number, clientY: number): Point => {
      const rect = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      return {
        x: (clientX - rect.left) * dpr,
        y: (clientY - rect.top) * dpr,
      };
    };

    const onPointerDown = (e: PointerEvent) => {
      e.preventDefault();
      try {
        canvas.setPointerCapture(e.pointerId);
      } catch {
        // setPointerCapture can throw on some Safari versions; ignore.
      }
      const dpr = window.devicePixelRatio || 1;
      drawingRef.current = {
        points: [getPoint(e.clientX, e.clientY)],
        size: brushSizeRef.current * dpr,
        color: brushColorRef.current,
      };
      redraw();
    };

    const onPointerMove = (e: PointerEvent) => {
      // Track preview position in CSS pixels (relative to canvas) for the
      // hover indicator. Updates whether or not we're mid-stroke.
      const rect = canvas.getBoundingClientRect();
      setPreviewPos({ x: e.clientX - rect.left, y: e.clientY - rect.top });

      if (!drawingRef.current) return;
      e.preventDefault();

      const coalesced =
        typeof e.getCoalescedEvents === 'function' ? e.getCoalescedEvents() : null;

      if (coalesced && coalesced.length > 0) {
        for (const ce of coalesced) {
          drawingRef.current.points.push(getPoint(ce.clientX, ce.clientY));
        }
      } else {
        drawingRef.current.points.push(getPoint(e.clientX, e.clientY));
      }
      redraw();
    };

    const onPointerLeave = () => setPreviewPos(null);

    const onPointerUp = (e: PointerEvent) => {
      if (!drawingRef.current) return;
      try {
        canvas.releasePointerCapture(e.pointerId);
      } catch {
        // ignore
      }
      if (drawingRef.current.points.length > 0) {
        strokesRef.current.push(drawingRef.current);
        redoStackRef.current = [];
      }
      drawingRef.current = null;
      redraw();
      notifyState();
    };

    // Pinch-zoom uses iOS-only gesture events that ignore touch-action: none.
    // Cancel these explicitly. We deliberately do NOT cancel touchstart/move/end
    // on iOS, pointer events are synthesized from those, and preventDefault'ing
    // them suppresses the pointer events we depend on.
    const blockGesture = (e: Event) => e.preventDefault();

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUp);
    canvas.addEventListener('pointercancel', onPointerUp);
    canvas.addEventListener('pointerleave', onPointerLeave);
    canvas.addEventListener('gesturestart', blockGesture);
    canvas.addEventListener('gesturechange', blockGesture);
    canvas.addEventListener('gestureend', blockGesture);

    return () => {
      canvas.removeEventListener('pointerdown', onPointerDown);
      canvas.removeEventListener('pointermove', onPointerMove);
      canvas.removeEventListener('pointerup', onPointerUp);
      canvas.removeEventListener('pointercancel', onPointerUp);
      canvas.removeEventListener('pointerleave', onPointerLeave);
      canvas.removeEventListener('gesturestart', blockGesture);
      canvas.removeEventListener('gesturechange', blockGesture);
      canvas.removeEventListener('gestureend', blockGesture);
    };
  }, []);

  // Brush preview circle. brushColor is white when erasing (App passes
  // '#ffffff' in that case) so we get a white-fill / black-outline indicator
  // that reads as "eraser" without needing a separate flag here.
  const isErasingPreview = brushColor === '#ffffff' || brushColor.toLowerCase() === '#fff';

  return (
    <div ref={wrapperRef} className="w-full h-full bg-white touch-none overflow-hidden relative">
      <canvas
        ref={canvasRef}
        onContextMenu={(e) => e.preventDefault()}
        className="block w-full h-full cursor-crosshair touch-none select-none"
        style={{ touchAction: 'none', WebkitUserSelect: 'none', WebkitTouchCallout: 'none' }}
      />
      {previewPos && (
        <div
          aria-hidden="true"
          className="absolute pointer-events-none rounded-full border-2 border-black"
          style={{
            left: previewPos.x - brushSize / 2,
            top: previewPos.y - brushSize / 2,
            width: brushSize,
            height: brushSize,
            backgroundColor: isErasingPreview
              ? 'rgba(255,255,255,0.7)'
              : `${brushColor}55`,
          }}
        />
      )}
    </div>
  );
});
