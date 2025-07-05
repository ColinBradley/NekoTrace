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

const FONT_SIZE = 14;
const SPAN_INNER_PADDING = Math.round(FONT_SIZE * 0.2);
const SPAN_HEIGHT_INNER = FONT_SIZE + (SPAN_INNER_PADDING * 2);
const SPAN_BORDER_WIDTH = 2;
const SPAN_HEIGHT_TOTAL = SPAN_HEIGHT_INNER + (SPAN_BORDER_WIDTH * 2);
const SPAN_ROW_OFFSET = SPAN_HEIGHT_TOTAL;

class TraceRenderer {

    private readonly canvasContext: CanvasRenderingContext2D;
    private readonly resizeObserver: ResizeObserver;
    private readonly characterPixelWidth: number;

    private spans: SpanItem[] = [];

    private startMs = 0;
    private durationMs = 0;

    private zoomRatio = 1;
    private top = 0;
    private left = 0;

    private isPanning = false;
    private pointerX = 0;
    private pointerY = 0;

    private readonly selectedSpansParents = new Set<SpanItem>();
    private readonly hotSpansParents = new Set<SpanItem>();
    private hotSpan?: SpanItem;
    private selectedSpan?: SpanItem;

    private selectionChangedCallback?: (spanId?: string) => Promise<void>;
    private lastSentSelectedSpanId?: string;

    private groupSpans = true;

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

        this.canvasContext.font = `${FONT_SIZE}px monospace`;
        this.canvasContext.textBaseline = "middle";

        this.characterPixelWidth = this.canvasContext.measureText('L').width;

        this.loadOptions();

        document.addEventListener("change", this.document_change);
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
                span.parent.children.push(span);
            }
        }

        this.startMs = startMs;
        this.durationMs = endMs - startMs;

        this.arrangeSpans();
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
        this.canvasElement.setPointerCapture(e.pointerId);
        this.isPanning = true;

        if (this.hotSpan !== undefined) {
            this.selectedSpan = this.hotSpan;
            populateParents(this.selectedSpansParents, this.selectedSpan);

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
        this.selectedSpansParents.clear();

        this.updateSpanLocations();

        this.setHotSpan();

        this.render();
    }

    private readonly canvasElement_pointerout = (e: PointerEvent) => {
        this.hotSpan = undefined;
        this.hotSpansParents.clear();
        this.pointerX = -1;
        this.pointerY = -1;

        this.render();
    }

    private readonly canvasElement_wheel = (e: WheelEvent) => {
        e.preventDefault();

        if (e.altKey) {
            // Pan
            const deltaX = e.shiftKey ? e.deltaY : e.deltaX;
            const deltaY = e.shiftKey ? e.deltaX : e.deltaY;

            this.left += Math.round(deltaX);
            this.top -= Math.round(deltaY);

        } else {
            // Zoom
            const scrolledContentPosition = (this.pointerX - this.left) / (this.canvasElement.width * this.zoomRatio);

            if (e.deltaY > 0) {
                this.zoomRatio /= 1.2;
            } else if (e.deltaY < 0) {
                this.zoomRatio *= 1.2;
            }

            this.left = this.pointerX - (scrolledContentPosition * (this.canvasElement.width * this.zoomRatio));

            this.updateSpanLocations();
        }

        this.render();
    }

    private readonly canvasElement_resized = () => {
        this.updateSpanLocations();
        this.render();
    }

    private readonly document_change = () => {
        // Wait for the URL to update
        setTimeout(() => {
            this.loadOptions();
            this.arrangeSpans();
            this.render();
        }, 10);
    }

    private loadOptions() {
        const searchParams = new URL(document.URL).searchParams;

        this.groupSpans = searchParams.get("GroupSpans")?.toLowerCase() !== "false";
    }

    private setHotSpan() {
        this.hotSpan = this.spans.find(s => (this.left + s.absolutePixelPositionX) < this.pointerX
            && (this.left + s.absolutePixelPositionX + s.pixelWidth) > this.pointerX
            && (this.top + s.absolutePixelPositionY) < this.pointerY
            && (this.top + s.absolutePixelPositionY + SPAN_HEIGHT_TOTAL) > this.pointerY
        );

        if (this.hotSpan === undefined) {
            this.hotSpansParents.clear();
        } else {
            populateParents(this.hotSpansParents, this.hotSpan);
        }

        const newSpanId = this.hotSpan?.id ?? this.selectedSpan?.id;

        if (this.lastSentSelectedSpanId != newSpanId && this.selectionChangedCallback !== undefined) {
            this.lastSentSelectedSpanId = newSpanId;
            void this.selectionChangedCallback(newSpanId);
        }
    }

    private arrangeSpans() {
        this.spans.sort((a, b) => a.startTimeMs - b.startTimeMs);

        if (this.groupSpans) {
            // TODO: This could be better - preventing siblings entering each other's children space?
            const newSpans = this.spans
                .filter(s => s.parent === undefined)
                .map(s => [...getSpansDepthFirst(s)])
                .flat();

            this.spans = newSpans;
        }

        const spansByRow: SpanItem[][] = [];
        for (const span of this.spans) {
            let isInserted = false;
            for (let rowIndex = (span.parent?.rowIndex ?? -1) + 1; rowIndex < spansByRow.length; rowIndex++) {
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

    private render() {
        this.canvasContext.clearRect(0, 0, this.canvasElement.width, this.canvasElement.height);

        for (const span of this.spans) {
            const isHot = span === this.hotSpan;
            const isSelected = span === this.selectedSpan;
            const isParent =
                this.hotSpan === undefined
                    ? this.selectedSpansParents.has(span)
                    : this.hotSpansParents.has(span);

            this.canvasContext.fillStyle =
                isSelected
                    ? "#3399cc"
                    : isParent
                        ? "#11374b"
                        : "#1f5f7f";

            this.canvasContext.fillRect(
                this.left + span.absolutePixelPositionX + SPAN_BORDER_WIDTH - 1,
                this.top + span.absolutePixelPositionY + SPAN_BORDER_WIDTH - 1,
                span.pixelWidth - (SPAN_BORDER_WIDTH * 2) + 2,
                SPAN_HEIGHT_INNER + 2
            );

            if (isHot) {
                this.canvasContext.strokeStyle = isHot ? "#dd8451" : "#aa653e";
                this.canvasContext.lineWidth = SPAN_BORDER_WIDTH;
                this.canvasContext.strokeRect(
                    this.left + span.absolutePixelPositionX,
                    this.top + span.absolutePixelPositionY,
                    span.pixelWidth,
                    SPAN_HEIGHT_TOTAL
                );
            }

            const textWidth = span.pixelWidth - (SPAN_BORDER_WIDTH * 2) - (SPAN_INNER_PADDING * 2);
            this.canvasContext.fillStyle = "white";
            this.canvasContext.fillText(
                this.fitString(span.name, textWidth),
                this.left + Math.round(span.absolutePixelPositionX + SPAN_INNER_PADDING + SPAN_BORDER_WIDTH),
                this.top + Math.round(span.absolutePixelPositionY + (SPAN_HEIGHT_TOTAL / 2)) + 2,
                textWidth
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

    private fitString(value: string, maxPixelWidth: number) {
        const maxCharacters = Math.round(maxPixelWidth / this.characterPixelWidth);

        if (maxCharacters <= 1) {
            return '';
        }

        if (value.length > maxCharacters) {
            return value.substring(0, maxCharacters) + '…';
        }

        return value;
    }
}

function* getSpansDepthFirst(span: SpanItem): Generator<SpanItem, void, unknown> {
    yield span;

    for (const child of span.children) {
        yield* getSpansDepthFirst(child);
    }
}

function populateParents(parents: Set<SpanItem>, span: SpanItem): void {
    parents.clear();

    let current = span.parent;
    while (current != undefined) {
        parents.add(current);
        current = current.parent;
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
