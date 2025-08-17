import { type SpanData, StatusCode } from "./types.js";

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

const TIME_LINE_HEIGHT = FONT_SIZE + SPAN_INNER_PADDING;
const RESIZE_GRAB_WIDTH = 10;

const SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME = "data-span-color-selector";

class TraceRenderer {

    private readonly canvasContext: CanvasRenderingContext2D;
    private readonly resizeObserver: ResizeObserver;
    private readonly mutationObserver: MutationObserver;
    private readonly characterPixelWidth: number;

    private readonly sizeClass: "small" | "large";

    private spans: SpanItem[] = [];

    private startMs = 0;
    private durationMs = 0;

    private zoomRatio = 1;
    private top = TIME_LINE_HEIGHT;
    private left = 0;

    private isResizingWidth = false;
    private isResizingHeight = false;
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

        this.mutationObserver = new MutationObserver(this.canvasElement_mutated);
        this.mutationObserver.observe(canvasElement, { attributeFilter: [SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME] });

        this.canvasContext.font = `${FONT_SIZE}px monospace`;
        
        this.characterPixelWidth = this.canvasContext.measureText('L').width;

        this.loadOptions();

        document.addEventListener("change", this.document_change);

        this.sizeClass = this.canvasElement.parentElement?.classList.contains("small") ? "small" : "large";

        const storedTraceViewWidth = localStorage.getItem("traceview.width." + this.sizeClass);
        if (storedTraceViewWidth !== null) {
            this.canvasElement.width = Number.parseInt(storedTraceViewWidth);
        }

        const storedTraceViewHeight = localStorage.getItem("traceview.height." + this.sizeClass);
        if (storedTraceViewHeight !== null) {
            this.canvasElement.height = Number.parseInt(storedTraceViewHeight);
        }
    }

    public spanErrorOverlayColor = "rgba(255, 0, 0, .8)";
    public spanParentOverlayColor = "rgba(0, 0, 0, .3)";
    public spanActiveBorderColor = "#dd8451";
    public spanTextColor = "#FFF";
    public timeOffsetTextColor = "#FFF";
    public timeLineColor = "#FFF6";
    public hoverTextBackgroundColor = "#0008";
    public hoverTextColor = "#FFF";

    public spanBackgroundColors = [
        "#3A4B33",
        "#61594F",
        "#3F4F44",
        "#8E5E37",
        "#004487",
        "#2A9D8F",
        "#00BFFF",
        "#4C4C9D",
        "#A8D676",
        "#5917bc",
        "#E9C46A",
        "#9B5DE5",
        "#457B9D",
        "#D946EF",
        "#FF6B6B"
    ];

    public setSpans(spans: SpanData[]) {
        this.spans = spans.map(s => (
            {
                ...s,
                children: [],
                rowIndex: 0,
                absolutePixelPositionX: 0,
                absolutePixelPositionY: 0,
                pixelWidth: 0,
                color: "red"
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
        this.updateSpanColors();

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
        } else if (this.isResizingWidth || this.isResizingHeight) {
            if (this.isResizingWidth) {
                if (this.sizeClass === "small") {
                    this.canvasElement.width -= e.movementX;
                } else {
                    this.canvasElement.width += e.movementX;
                }

                localStorage.setItem("traceview.width." + this.sizeClass, this.canvasElement.width.toString());
            }
            if (this.isResizingHeight) {
                this.canvasElement.height += e.movementY;

                localStorage.setItem("traceview.height." + this.sizeClass, this.canvasElement.height.toString());
            }
        } else {
            this.canvasElement.style.cursor = this.getCursor();
        }

        this.setHotSpan();

        this.render();
    }

    private readonly canvasElement_pointerdown = (e: PointerEvent) => {
        this.canvasElement.setPointerCapture(e.pointerId);

        switch (this.canvasElement.style.cursor) {
            case "nesw-resize":
            case "nwse-resize":
                this.isResizingWidth = true;
                this.isResizingHeight = true;
                break;
            case "ew-resize":
                this.isResizingWidth = true;
                break;
            case "ns-resize":
                this.isResizingHeight = true;
                break;
            default:
                this.isPanning = true;
                break;
        }
        
        if (this.hotSpan !== undefined) {
            this.selectedSpan = this.hotSpan;
            populateParents(this.selectedSpansParents, this.selectedSpan);

            this.render();
        }
    }

    private readonly canvasElement_pointerup = (e: PointerEvent) => {
        if (this.isPanning || this.isResizingWidth || this.isResizingHeight) {
            this.canvasElement.releasePointerCapture(e.pointerId);
            this.isPanning = false;
            this.isResizingWidth = false;
            this.isResizingHeight = false;
        }
    }

    private readonly canvasElement_dblclick = () => {
        // Reset
        this.zoomRatio = 1;
        this.top = TIME_LINE_HEIGHT;
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
        // Weirdly, this gets unset with resizes
        this.canvasContext.font = `${FONT_SIZE}px monospace`;

        this.updateSpanLocations();
        this.render();
    }

    private readonly canvasElement_mutated = (mutations: MutationRecord[]) => {
        if (mutations.some(m => m.attributeName === SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME)) {
            this.updateSpanColors();
            this.render();
        }
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
    }

    private updateSpanColors() {
        let spanColorIndex = 0;
        const spanColorValues = new Map<unknown, string>();
        const spanColorSelector = this.canvasElement.getAttribute(SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME) ?? "";

        for (const span of this.spans) {
            const spanColorValue = span.attributes[spanColorSelector];
            let spanColor = spanColorValues.get(spanColorValue);
            if (spanColor === undefined) {
                spanColor = this.spanBackgroundColors[spanColorIndex] ?? "black";
                spanColorValues.set(spanColorValue, spanColor);
                spanColorIndex++;
            }

            span.color = spanColor;
        }
    }

    private render() {
        this.canvasContext.clearRect(0, 0, this.canvasElement.width, this.canvasElement.height);

        this.canvasContext.textBaseline = "middle";

        for (const span of this.spans) {
            const isHot = span === this.hotSpan;
            const isSelected = span === this.selectedSpan;
            const isParent =
                this.hotSpan === undefined
                    ? this.selectedSpansParents.has(span)
                    : this.hotSpansParents.has(span);

            this.canvasContext.fillStyle = span.color;
            this.renderSpanBackground(span);

            if (span.statusCode === StatusCode.Error) {
                this.canvasContext.fillStyle = this.spanErrorOverlayColor;
                this.renderSpanBackground(span);
            }

            if (isParent) {
                this.canvasContext.fillStyle = this.spanParentOverlayColor;
                this.renderSpanBackground(span);
            }

            if (isHot || isSelected) {
                this.canvasContext.strokeStyle = this.spanActiveBorderColor;

                this.canvasContext.lineWidth = SPAN_BORDER_WIDTH;
                this.canvasContext.strokeRect(
                    this.left + span.absolutePixelPositionX,
                    this.top + span.absolutePixelPositionY,
                    span.pixelWidth,
                    SPAN_HEIGHT_TOTAL
                );
            }

            if (span.pixelWidth > this.characterPixelWidth) {
                const absoluteTextLeft = this.left + Math.round(span.absolutePixelPositionX + SPAN_INNER_PADDING + SPAN_BORDER_WIDTH);
                const absoluteTextWidth = span.pixelWidth - (SPAN_BORDER_WIDTH * 2) - (SPAN_INNER_PADDING * 2);
                const effectiveTextLeft = Math.max(0, absoluteTextLeft);
                const effectiveTextWidth = Math.min(this.canvasElement.width, this.canvasElement.width - absoluteTextLeft, absoluteTextWidth - (effectiveTextLeft - absoluteTextLeft), absoluteTextWidth);

                this.canvasContext.fillStyle = this.spanTextColor;
                this.canvasContext.fillText(
                    this.fitString(span.name, effectiveTextWidth),
                    effectiveTextLeft,
                    this.top + Math.round(span.absolutePixelPositionY + (SPAN_HEIGHT_TOTAL / 2)) + 2,
                    effectiveTextWidth
                );

                const durationTextWidth = (this.characterPixelWidth * span.durationText.length);
                if ((this.characterPixelWidth * span.name.length) + durationTextWidth + 1 < effectiveTextWidth) {
                    this.canvasContext.fillText(
                        span.durationText,
                        (effectiveTextLeft + effectiveTextWidth) - durationTextWidth,
                        this.top + Math.round(span.absolutePixelPositionY + (SPAN_HEIGHT_TOTAL / 2)) + 2,
                        durationTextWidth
                    );
                }
            }
        }

        this.canvasContext.clearRect(0, 0, this.canvasElement.width, TIME_LINE_HEIGHT);
        this.canvasContext.fillStyle = this.timeOffsetTextColor;

        const segmentWidth = this.characterPixelWidth * 20;
        const timeSegments = this.canvasElement.width / segmentWidth;
        const msToPixels = (this.canvasElement.width / this.durationMs) * this.zoomRatio;
        const timeWindowMs = this.canvasElement.width / msToPixels;

        const getTimeText = (timeMs: number) =>
            timeMs === 0
                ? "0"
                : timeWindowMs < 0.0001
                    ? +(timeMs * 1000000).toFixed(3) + "ns"
                    : timeWindowMs < 1
                        ? +(timeMs * 1000).toFixed(3) + "µs"
                        : timeWindowMs <= 1000
                            ? +timeMs.toFixed(3) + "ms"
                            : +(timeMs / 1000).toFixed(3) + "s";

        for (let segmentIndex = 0; segmentIndex < timeSegments; segmentIndex++) {
            const left = Math.round(segmentIndex * segmentWidth);
            const timeMs = (left - this.left) / msToPixels;
            this.canvasContext.fillRect(left, 0, 1, TIME_LINE_HEIGHT);

            this.canvasContext.fillText(getTimeText(timeMs), segmentIndex * segmentWidth + 3, TIME_LINE_HEIGHT / 2);
        }

        if (this.pointerX >= 0) {
            // Time line indicator
            this.canvasContext.fillStyle = this.timeLineColor;
            this.canvasContext.fillRect(this.pointerX, 0, 1, this.canvasElement.height);

            // Time text
            const timeText = getTimeText((this.pointerX - this.left) / msToPixels);

            this.canvasContext.fillStyle = this.hoverTextBackgroundColor;
            this.canvasContext.textBaseline = "top";
            this.canvasContext.fillRect(this.pointerX + 1, 0, (timeText.length + 2) * this.characterPixelWidth, FONT_SIZE);

            this.canvasContext.fillStyle = this.hoverTextColor;
            this.canvasContext.fillText(
                timeText,
                this.pointerX + this.characterPixelWidth,
                0
            );

            if (this.hotSpan !== undefined) {
                this.canvasContext.fillStyle = this.hoverTextBackgroundColor;
                this.canvasContext.fillRect(this.pointerX + 1, this.pointerY + (FONT_SIZE * 2), (this.hotSpan.name.length + 2) * this.characterPixelWidth, FONT_SIZE);
                this.canvasContext.fillRect(this.pointerX + 1, this.pointerY + (FONT_SIZE * 3), (this.hotSpan.durationText.length + 2) * this.characterPixelWidth, FONT_SIZE);

                this.canvasContext.fillStyle = this.hoverTextColor;
                this.canvasContext.textBaseline = "top";
                this.canvasContext.fillText(this.hotSpan.name, this.pointerX + this.characterPixelWidth, this.pointerY + (FONT_SIZE * 2));
                this.canvasContext.fillText(this.hotSpan.durationText, this.pointerX + this.characterPixelWidth, this.pointerY + (FONT_SIZE * 3));
            }
        }
    }

    private renderSpanBackground(span: SpanItem) {
        this.canvasContext.fillRect(
            this.left + span.absolutePixelPositionX + SPAN_BORDER_WIDTH - 1,
            this.top + span.absolutePixelPositionY + SPAN_BORDER_WIDTH - 1,
            span.pixelWidth - (SPAN_BORDER_WIDTH * 2) + 2,
            SPAN_HEIGHT_INNER + 2
        );
    }

    private updateSpanLocations() {
        const msToPixels = (this.canvasElement.width / this.durationMs) * this.zoomRatio;

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

    private getCursor() {
        const locations: string[] = [];

        if (this.pointerY >= (this.canvasElement.height - RESIZE_GRAB_WIDTH)) {
            locations.push("bottom");
        }
        if (this.pointerX <= RESIZE_GRAB_WIDTH) {
            locations.push("left");
        } else if (this.pointerX >= (this.canvasElement.width - RESIZE_GRAB_WIDTH)) {
            locations.push("right");
        }

        switch (locations.join("-")) {
            case "left":
            case "right":
                return "ew-resize";
            case "bottom":
                return "ns-resize";
            case "bottom-left":
                return "nesw-resize";
            case "bottom-right":
                return "nwse-resize";
            default:
                return "auto";
        }
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
    color: string;
    rowIndex: number;

    absolutePixelPositionX: number;
    absolutePixelPositionY: number;
    pixelWidth: number;
}

interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<void>;
}
