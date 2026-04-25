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

  const getCanvasPointFromClient = (clientX: number, clientY: number): Point => {
    const canvas = canvasRef.current!;
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    return {
      x: (clientX - rect.left) * dpr,
      y: (clientY - rect.top) * dpr,
    };
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    e.preventDefault();
    try {
      canvasRef.current?.setPointerCapture(e.pointerId);
    } catch {
      // ignore — some browsers throw if the pointer can't be captured
    }
    const dpr = window.devicePixelRatio || 1;
    drawingRef.current = {
      points: [getCanvasPointFromClient(e.clientX, e.clientY)],
      size: brushSizeRef.current * dpr,
      color: brushColorRef.current,
    };
    redraw();
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawingRef.current) return;

    // Use coalesced events when available (iPad ProMotion / Apple Pencil
    // can fire 240Hz; without this we drop intermediate samples).
    const coalesced =
      typeof e.nativeEvent.getCoalescedEvents === 'function'
        ? e.nativeEvent.getCoalescedEvents()
        : null;

    if (coalesced && coalesced.length > 0) {
      for (const ce of coalesced) {
        drawingRef.current.points.push(getCanvasPointFromClient(ce.clientX, ce.clientY));
      }
    } else {
      drawingRef.current.points.push(getCanvasPointFromClient(e.clientX, e.clientY));
    }
    redraw();
  };

  const handlePointerUp = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawingRef.current) return;
    try {
      canvasRef.current?.releasePointerCapture(e.pointerId);
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

  return (
    <div ref={wrapperRef} className="w-full h-full bg-white touch-none overflow-hidden">
      <canvas
        ref={canvasRef}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerCancel={handlePointerUp}
        onContextMenu={(e) => e.preventDefault()}
        className="block w-full h-full cursor-crosshair touch-none select-none"
        style={{ touchAction: 'none', WebkitUserSelect: 'none', WebkitTouchCallout: 'none' }}
      />
    </div>
  );
});
