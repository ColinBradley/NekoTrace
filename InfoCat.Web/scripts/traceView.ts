import type { SpanData } from "./types.js";

export function initialize(
    targetCanvas: HTMLCanvasElement & { traceRenderer: TraceRenderer },
    spans: SpanData[],
    callbackObject: DotNetObjectReference,
    callbackName: string
) {
    const renderer = targetCanvas.traceRenderer ??= new TraceRenderer(targetCanvas);

    renderer.setSpans(spans);
    renderer.setSelectionChangedCallback(spanId => callbackObject.invokeMethodAsync(callbackName, spanId));
}

const FONT_SIZE = 16;
const SPAN_INNER_PADDING = FONT_SIZE * 0.2;
const SPAN_HEIGHT_INNER = FONT_SIZE + (SPAN_INNER_PADDING * 2);
const SPAN_BORDER_WIDTH = 1;
const SPAN_HEIGHT_TOTAL = SPAN_HEIGHT_INNER + (SPAN_BORDER_WIDTH * 2);
const SPAN_ROW_GAP = FONT_SIZE * 0.1;
const SPAN_ROW_OFFSET = SPAN_HEIGHT_TOTAL + SPAN_ROW_GAP;

class TraceRenderer {

    private readonly canvasContext: CanvasRenderingContext2D;
    private readonly resizeObserver: ResizeObserver;
    private spans: SpanItem[] = [];

    private startMs = 0;
    private durationMs = 0;

    private zoomRatio = 1;
    private top = 0;
    private left = 0;

    private isPanning = false;
    private pointerX = 0;
    private pointerY = 0;

    private hotSpan?: SpanItem;
    private selectedSpan?: SpanItem;

    private selectionChangedCallback?: (spanId?: string) => Promise<void>;

    public constructor(
        private readonly canvasElement: HTMLCanvasElement
    ) {
        this.canvasContext = canvasElement.getContext("2d")!;

        canvasElement.addEventListener("pointermove", this.canvasElement_pointermove);
        canvasElement.addEventListener("pointerdown", this.canvasElement_pointerdown);
        canvasElement.addEventListener("pointerup", this.canvasElement_pointerup);
        canvasElement.addEventListener("dblclick", this.canvasElement_dblclick);
        canvasElement.addEventListener("pointerout", this.canvasElement_pointerout);
        canvasElement.addEventListener("wheel", this.canvasElement_wheel);
        this.resizeObserver = new ResizeObserver(this.canvasElement_resized);
        this.resizeObserver.observe(canvasElement);
    }

    public setSpans(spans: SpanData[]) {
        this.spans = spans.map(s => (
            {
                ...s,
                children: [],
                rowIndex: 0,
                absolutePixelPositionX: 0,
                absolutePixelPositionY: 0,
                pixelWidth: 0
            }
        ));

        const spansById = new Map(this.spans.map(s => [s.id, s]));

        let startMs = Number.MAX_VALUE;
        let endMs = Number.MIN_VALUE;

        for (const span of this.spans) {
            if (span.startTimeMs < startMs) {
                startMs = span.startTimeMs;
            }

            if (span.endTimeMs > endMs) {
                endMs = span.endTimeMs;
            }

            if (span.parentSpanId === undefined) {
                continue;
            }

            span.parent = spansById.get(span.parentSpanId);

            if (span.parent !== undefined) {
                span.children.push(span);
            }
        }

        this.startMs = startMs;
        this.durationMs = endMs - startMs;

        const spansByRow: SpanData[][] = [];
        for (const span of this.spans) {
            let isInserted = false;
            for (let rowIndex = 0; rowIndex < spansByRow.length; rowIndex++) {
                const rowSpans = spansByRow[rowIndex];
                if (rowSpans.some(s => s.endTimeMs > span.startTimeMs)) {
                    continue;
                }

                span.rowIndex = rowIndex;
                rowSpans.push(span);
                isInserted = true;
                break;
            }

            if (!isInserted) {
                span.rowIndex = spansByRow.length;
                spansByRow.push([span]);
            }

            span.absolutePixelPositionY = SPAN_ROW_OFFSET * span.rowIndex;
        }

        this.updateSpanLocations();

        this.render();
    }

    public setSelectionChangedCallback(callback: (spanId?: string) => Promise<void>) {
        this.selectionChangedCallback = callback;
    }

    private readonly canvasElement_pointermove = (e: PointerEvent) => {
        this.pointerX = e.offsetX;
        this.pointerY = e.offsetY;

        if (this.isPanning) {
            this.left += e.movementX;
            this.top += e.movementY;
        }

        this.setHotSpan();

        this.render();
    }

    private readonly canvasElement_pointerdown = (e: PointerEvent) => {
        if (this.hotSpan === undefined) {
            this.canvasElement.setPointerCapture(e.pointerId);
            this.isPanning = true;
        } else {
            this.selectedSpan = this.hotSpan;
            this.render();
        }
    }

    private readonly canvasElement_pointerup = (e: PointerEvent) => {
        if (this.isPanning) {
            this.canvasElement.releasePointerCapture(e.pointerId);
            this.isPanning = false;
        }
    }

    private readonly canvasElement_dblclick = () => {
        // Reset
        this.zoomRatio = 1;
        this.top = 0;
        this.left = 0;
        this.selectedSpan = undefined;

        this.updateSpanLocations();

        this.setHotSpan();

        this.render();
    }

    private readonly canvasElement_pointerout = (e: PointerEvent) => {
        this.hotSpan = undefined;
        this.pointerX = -1;
        this.pointerY = -1;

        this.render();
    }

    private readonly canvasElement_wheel = (e: WheelEvent) => {
        e.preventDefault();

        if (e.altKey) {
            // Zoom
            if (e.deltaY > 0) {
                this.zoomRatio /= 1.2;
            } else if (e.deltaY < 0) {
                this.zoomRatio *= 1.2;
            }

            this.updateSpanLocations();
        } else {
            // Pan
            const deltaX = e.shiftKey ? e.deltaY : e.deltaX;
            const deltaY = e.shiftKey ? e.deltaX : e.deltaY;

            if (deltaY > 0) {
                this.top -= Math.round(SPAN_HEIGHT_TOTAL / 2);
            } else if (deltaY < 0) {
                this.top += Math.round(SPAN_HEIGHT_TOTAL / 2);
            }

            this.left += Math.round(deltaX / 2);
        }

        this.render();
    }

    private readonly canvasElement_resized = () => {
        this.updateSpanLocations();
        this.render();
    }

    private lastSentSelectedSpanId?: string;

    private setHotSpan() {
        this.hotSpan = this.spans.find(s => (this.left + s.absolutePixelPositionX) < this.pointerX
            && (this.left + s.absolutePixelPositionX + s.pixelWidth) > this.pointerX
            && (this.top + s.absolutePixelPositionY) < this.pointerY
            && (this.top + s.absolutePixelPositionY + SPAN_HEIGHT_TOTAL) > this.pointerY
        );

        const newSpanId = this.hotSpan?.id ?? this.selectedSpan?.id;

        if (this.lastSentSelectedSpanId != newSpanId && this.selectionChangedCallback !== undefined) {
            this.lastSentSelectedSpanId = newSpanId;
            void this.selectionChangedCallback(newSpanId);
        }
    }

    private render() {
        this.canvasContext.clearRect(0, 0, this.canvasElement.width, this.canvasElement.height);

        this.canvasContext.font = `${FONT_SIZE}px sans-serif`;
        this.canvasContext.textBaseline = "middle";

        for (const span of this.spans) {
            const isHot = span === this.hotSpan;
            const isSelected = span === this.selectedSpan;

            this.canvasContext.fillStyle = isHot
                ? isSelected
                    ? "purple"
                    : "red"
                : isSelected
                    ? "blue"
                    : "black";
            this.canvasContext.fillRect(
                this.left + span.absolutePixelPositionX,
                this.top + span.absolutePixelPositionY,
                span.pixelWidth,
                SPAN_HEIGHT_INNER
            );

            this.canvasContext.fillStyle = "white";
            this.canvasContext.fillText(
                span.name,
                this.left + Math.round(span.absolutePixelPositionX + SPAN_INNER_PADDING),
                this.top + Math.round(span.absolutePixelPositionY + (SPAN_HEIGHT_TOTAL / 2)),
                span.pixelWidth
            );
        }
    }

    private updateSpanLocations() {
        const elementWidth = this.canvasElement.width;
        const msToPixels = (elementWidth / this.durationMs) * this.zoomRatio;

        for (const span of this.spans) {
            span.absolutePixelPositionX = (span.startTimeMs - this.startMs) * msToPixels;
            span.pixelWidth = (span.endTimeMs - span.startTimeMs) * msToPixels;
        }
    }
}

interface SpanItem extends SpanData {
    parent?: SpanItem;
    readonly children: SpanItem[];
    rowIndex: number;

    absolutePixelPositionX: number;
    absolutePixelPositionY: number;
    pixelWidth: number;
}

interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<void>;
}
