import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
} from 'react';

export interface CanvasState {
  canUndo: boolean;
  canRedo: boolean;
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
  const activePointerIdRef = useRef<number | null>(null);
  const activePointerTypeRef = useRef<string | null>(null);

  useEffect(() => {
    brushSizeRef.current = brushSize;
  }, [brushSize]);

  useEffect(() => {
    brushColorRef.current = brushColor;
  }, [brushColor]);

  useEffect(() => {
    onStateChangeRef.current = onStateChange;
  }, [onStateChange]);

  const notifyState = () => {
    onStateChangeRef.current?.({
      canUndo: strokesRef.current.length > 0,
      canRedo: redoStackRef.current.length > 0,
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

  // Native event listeners — React 19 attaches touch listeners as passive,
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

      // Palm rejection: while a pen stroke is in flight, ignore touch.
      if (
        drawingRef.current &&
        activePointerTypeRef.current === 'pen' &&
        e.pointerType === 'touch'
      ) {
        return;
      }

      // Pen takes over from touch mid-stroke (Pencil picked up after finger).
      if (
        drawingRef.current &&
        e.pointerType === 'pen' &&
        activePointerTypeRef.current !== 'pen'
      ) {
        drawingRef.current = null;
      } else if (drawingRef.current) {
        return;
      }

      try {
        canvas.setPointerCapture(e.pointerId);
      } catch {
        // setPointerCapture can throw on some Safari versions; ignore.
      }
      activePointerIdRef.current = e.pointerId;
      activePointerTypeRef.current = e.pointerType;

      const dpr = window.devicePixelRatio || 1;
      drawingRef.current = {
        points: [getPoint(e.clientX, e.clientY)],
        size: brushSizeRef.current * dpr,
        color: brushColorRef.current,
      };
      redraw();
    };

    const onPointerMove = (e: PointerEvent) => {
      if (!drawingRef.current) return;
      if (e.pointerId !== activePointerIdRef.current) return;
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

    const onPointerUp = (e: PointerEvent) => {
      if (!drawingRef.current) return;
      if (e.pointerId !== activePointerIdRef.current) return;

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
      activePointerIdRef.current = null;
      activePointerTypeRef.current = null;
      redraw();
      notifyState();
    };

    // Pinch-zoom uses iOS-only gesture events that ignore touch-action: none.
    // Cancel these explicitly. We deliberately do NOT cancel touchstart/move/end
    // on iOS — pointer events are synthesized from those, and preventDefault'ing
    // them suppresses the pointer events we depend on.
    const blockGesture = (e: Event) => e.preventDefault();

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUp);
    canvas.addEventListener('pointercancel', onPointerUp);
    canvas.addEventListener('gesturestart', blockGesture);
    canvas.addEventListener('gesturechange', blockGesture);
    canvas.addEventListener('gestureend', blockGesture);

    return () => {
      canvas.removeEventListener('pointerdown', onPointerDown);
      canvas.removeEventListener('pointermove', onPointerMove);
      canvas.removeEventListener('pointerup', onPointerUp);
      canvas.removeEventListener('pointercancel', onPointerUp);
      canvas.removeEventListener('gesturestart', blockGesture);
      canvas.removeEventListener('gesturechange', blockGesture);
      canvas.removeEventListener('gestureend', blockGesture);
    };
  }, []);

  return (
    <div ref={wrapperRef} className="w-full h-full bg-white touch-none overflow-hidden">
      <canvas
        ref={canvasRef}
        onContextMenu={(e) => e.preventDefault()}
        className="block w-full h-full cursor-crosshair touch-none select-none"
        style={{ touchAction: 'none', WebkitUserSelect: 'none', WebkitTouchCallout: 'none' }}
      />
    </div>
  );
});
