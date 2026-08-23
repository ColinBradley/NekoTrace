import { formatDuration } from "./localValueFormatting.ts";
import { type SpanData, type SpanItem, SpanKind, StatusCode } from "./types.ts";
import type { TraceViewOptions } from "./urlState.ts";

// It's a bit slow to fetch window.devicePixelRatio, so cache it
let devicePixelRatioCache = window.devicePixelRatio;

const DEFAULT_FONT_SIZE = 14;
const DEFAULT_FONT_FAMILY = "monospace";

// Most specific first: one service can be running as several instances, each with its own clock.
// These are the OpenTelemetry names for it, but what identifies a clock is up to whoever is exporting.
const DEFAULT_CLOCK_GROUP_ATTRIBUTE_NAMES = ["service.instance.id", "service.name"];

export class TraceRenderer {

    private readonly canvasElement: HTMLCanvasElement;
    private readonly canvasContext: CanvasRenderingContext2D;
    private readonly resizeObserver: ResizeObserver;

    /**
     * The canvas box in whole device pixels, from the last resize callback that reported one. Undefined
     * before the first callback, and on browsers that don't measure a device pixel content box.
     */
    private observedDevicePixelWidth?: number;
    private observedDevicePixelHeight?: number;

    private characterPixelWidth = 1;

    /* Pixel sizes derived from the font size and the device pixel ratio. updateMetrics redoes the lot. */
    private fontSize = DEFAULT_FONT_SIZE;
    private spanInnerPadding = 0;
    private spanHeightInner = 0;
    private spanBorderWidth = 0;
    private spanHeightTotal = 0;
    private spanRowOffset = 0;
    private timeLineHeight = 0;

    private fontFamily = DEFAULT_FONT_FAMILY;
    private baseFontSize = DEFAULT_FONT_SIZE;

    private spans: SpanItem[] = [];
    private filteredSpans: SpanItem[] = [];
    private spansByRow: SpanItem[][] = [];

    private startMs = 0;
    private durationMs = 0;

    private zoomRatio = 1;
    private top = 0;
    private left = 0;

    private isPanning = false;
    private pointerX = 0;
    private pointerY = 0;

    /**
     * Where the pointer was when the drag last moved the view. Kept apart from pointerX/pointerY, which
     * are reset when the pointer leaves - reading them here would turn the next move into a jump.
     */
    private panPointerX = 0;
    private panPointerY = 0;

    private readonly selectedSpansParents = new Set<SpanItem>();
    private readonly hotSpansParents = new Set<SpanItem>();
    private hotSpan?: SpanItem;
    private selectedSpan?: SpanItem;

    private activeSpanChangedCallback?: (span?: SpanItem) => void;
    private selectedSpanChangedCallback?: (span?: SpanItem) => void;
    private lastReportedActiveSpanId?: string;

    private spansById = new Map<string, SpanItem>();
    private pendingSelectedSpanId?: string;
    private devicePixelRatioQuery?: MediaQueryList;

    private spanColorSelector = "";
    private hiddenSpanNames = new Set<string>();
    private hiddenSpanIds = new Set<string>();
    private groupSpans = true;
    private adjustClockSkew = false;
    private clockGroupAttributeNames = DEFAULT_CLOCK_GROUP_ATTRIBUTE_NAMES;

    /** The shift applied to each clock, keyed by SpanItem.clockGroup. Undefined until it is solved for. */
    private clockOffsetsByGroup?: Map<string, number>;

    public constructor(canvasElement: HTMLCanvasElement) {
        this.canvasElement = canvasElement;
        this.canvasContext = canvasElement.getContext("2d")!;

        canvasElement.addEventListener("pointermove", this.canvasElement_pointermove);
        canvasElement.addEventListener("pointerdown", this.canvasElement_pointerdown);
        canvasElement.addEventListener("pointerup", this.canvasElement_pointerup);
        canvasElement.addEventListener("pointercancel", this.canvasElement_pointerup);
        canvasElement.addEventListener("dblclick", this.canvasElement_dblclick);
        canvasElement.addEventListener("pointerout", this.canvasElement_pointerout);
        canvasElement.addEventListener("wheel", this.canvasElement_wheel);

        this.resizeObserver = new ResizeObserver(this.canvasElement_resized);

        try {
            this.resizeObserver.observe(canvasElement, { box: "device-pixel-content-box" });
        } catch {
            // Observing a box it doesn't implement throws, and leaves the element unobserved.
            this.resizeObserver.observe(canvasElement);
        }

        this.reloadStyle();

        this.top = this.timeLineHeight;

        this.resizeCanvas();
        this.watchDevicePixelRatio();
    }

    public spanErrorOverlayColor = "rgba(255, 0, 0, .7)";
    public spanParentOverlayColor = "rgba(0, 0, 0, .3)";
    public spanActiveBorderColor = "#dd8451";
    public spanHotBorderColor = "#FF8644";
    public spanTransitionBorderColor = "#CCC9";
    public spanTextColor = "#FFF";
    public timeOffsetTextColor = "#FFF";
    public timeLineColor = "#FFF6";
    public hoverTextBackgroundColor = "#000C";
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
        "#5917BC",
        "#8F7842",
        "#9B5DE5",
        "#457B9D",
        "#8F2C9E",
        "#963F3F"
    ];

    /** A canvas takes no CSS, so the colours and the font are read off it as custom properties. */
    public reloadStyle() {
        const computed = getComputedStyle(this.canvasElement);

        const read = (name: string, fallback: string) => {
            const value = computed.getPropertyValue(name).trim();
            return value.length > 0 ? value : fallback;
        };

        this.spanErrorOverlayColor = read("--neko-trace-span-error-overlay-color", this.spanErrorOverlayColor);
        this.spanParentOverlayColor = read("--neko-trace-span-parent-overlay-color", this.spanParentOverlayColor);
        this.spanActiveBorderColor = read("--neko-trace-span-active-border-color", this.spanActiveBorderColor);
        this.spanHotBorderColor = read("--neko-trace-span-hot-border-color", this.spanHotBorderColor);
        this.spanTransitionBorderColor = read("--neko-trace-span-transition-border-color", this.spanTransitionBorderColor);
        this.spanTextColor = read("--neko-trace-span-text-color", this.spanTextColor);
        this.timeOffsetTextColor = read("--neko-trace-time-offset-text-color", this.timeOffsetTextColor);
        this.timeLineColor = read("--neko-trace-time-line-color", this.timeLineColor);
        this.hoverTextBackgroundColor = read("--neko-trace-hover-text-background-color", this.hoverTextBackgroundColor);
        this.hoverTextColor = read("--neko-trace-hover-text-color", this.hoverTextColor);

        const palette = splitList(read("--neko-trace-span-colors", ""));

        if (palette.length > 0) {
            this.spanBackgroundColors = palette;
        }

        this.fontFamily = read("--neko-trace-font-family", DEFAULT_FONT_FAMILY);

        const fontSize = Number.parseFloat(read("--neko-trace-font-size", ""));
        this.baseFontSize = Number.isFinite(fontSize) && fontSize > 0 ? fontSize : DEFAULT_FONT_SIZE;

        this.restyle();
    }

    /**
     * Re-derives everything downstream of the font size and the palette. Row heights come off the font
     * size, so this is a relayout and not only a repaint.
     */
    private restyle() {
        this.updateMetrics();
        this.updateSpanColors();
        this.arrangeSpans();
        this.updateSpanLocations();
        this.render();
    }

    /** Re-derives every pixel size, and the character width the text fitting is measured in. */
    private updateMetrics() {
        this.fontSize = Math.round(this.baseFontSize * devicePixelRatioCache);
        this.spanInnerPadding = Math.round(this.fontSize * 0.3);
        this.spanHeightInner = Math.round(this.fontSize + (this.spanInnerPadding * 2));
        this.spanBorderWidth = Math.round(2 * devicePixelRatioCache);
        this.spanHeightTotal = this.spanHeightInner + (this.spanBorderWidth * 2);
        this.spanRowOffset = this.spanHeightTotal;
        this.timeLineHeight = this.fontSize + this.spanInnerPadding;

        this.canvasContext.font = `${this.fontSize}px ${this.fontFamily}`;
        this.characterPixelWidth = this.canvasContext.measureText("L").width || 1;
    }

    /**
     * Sizes the backing store to the box CSS gave the canvas. Never writes the CSS size, or it loops.
     *
     * A backing store that isn't exactly the device pixels the box covers gets scaled to fit, and the whole
     * view goes soft. Only the device pixel content box measures that; clientWidth is CSS pixels already
     * rounded, and a grid track of `1fr` lands on a fraction most of the time. The rounding below is the
     * fallback for browsers that don't report one, and for the calls made outside a resize callback.
     */
    private resizeCanvas() {
        const width = Math.max(1, this.observedDevicePixelWidth
            ?? Math.round(this.canvasElement.clientWidth * devicePixelRatioCache));
        const height = Math.max(1, this.observedDevicePixelHeight
            ?? Math.round(this.canvasElement.clientHeight * devicePixelRatioCache));

        if (this.canvasElement.width === width && this.canvasElement.height === height) {
            return false;
        }

        this.canvasElement.width = width;
        this.canvasElement.height = height;

        return true;
    }

    /** Where the spans start on the timeline, in the same milliseconds the spans carry. */
    public get traceStartMs(): number {
        return this.startMs;
    }

    public setSpanColorSelector(value: string) {
        if (this.spanColorSelector === value) {
            return;
        }

        this.spanColorSelector = value;

        this.updateSpanColors();
        this.render();
    }

    public setSpans(spans: SpanData[]) {
        // A page created hidden gets no frames and so no resize callback; this is the other point at
        // which the canvas is known to be laid out.
        this.resizeCanvas();

        this.spans = spans.map((s, index) => (
            {
                ...s,
                children: [],
                rowIndex: 0,
                childrenDepth: 0,
                absolutePixelPositionX: 0,
                absolutePixelPositionY: 0,
                pixelWidth: 0,
                sourceIndex: index,
                clockGroup: "",
                adjustedStartTimeMs: s.startTimeMs,
                adjustedEndTimeMs: s.endTimeMs,
                durationText: formatDuration(s.endTimeMs - s.startTimeMs),
                color: "red",
            }
        ));

        this.spansById = new Map(this.spans.map(s => [s.id, s]));

        for (const span of this.spans) {
            if (span.parentSpanId === undefined) {
                continue;
            }

            span.parent = this.spansById.get(span.parentSpanId);
        }

        this.updateClockGroups();
        this.updateSpanTimes();
        this.arrangeSpans();
        this.updateSpanLocations();
        this.updateSpanColors();

        this.applySelectedSpanId(this.pendingSelectedSpanId);

        this.render();
    }

    /** Raised on hover as well as on click - it drives what the detail panel is showing. */
    public setActiveSpanChangedCallback(callback: (span?: SpanItem) => void) {
        this.activeSpanChangedCallback = callback;
    }

    /** Raised only when a span is actually picked, which is the one worth putting in the URL. */
    public setSelectedSpanChangedCallback(callback: (span?: SpanItem) => void) {
        this.selectedSpanChangedCallback = callback;
    }

    private readonly canvasElement_pointermove = (e: PointerEvent) => {
        this.pointerX = e.offsetX * devicePixelRatioCache;
        this.pointerY = e.offsetY * devicePixelRatioCache;

        if (this.isPanning) {
            // Measured off the pointer rather than taken from movementX, so the view keeps pace with the
            // cursor: these are device pixels, which is what left and top are in, and movementX is not.
            this.left += this.pointerX - this.panPointerX;
            this.top += this.pointerY - this.panPointerY;

            this.panPointerX = this.pointerX;
            this.panPointerY = this.pointerY;
        }

        this.setHotSpan();

        this.render();
    };

    private readonly canvasElement_pointerdown = (e: PointerEvent) => {
        this.canvasElement.setPointerCapture(e.pointerId);
        this.canvasElement.classList.add("panning");

        this.panPointerX = e.offsetX * devicePixelRatioCache;
        this.panPointerY = e.offsetY * devicePixelRatioCache;
        this.isPanning = true;

        if (this.hotSpan !== undefined) {
            this.setSelectedSpan(this.hotSpan);
        }
    };

    private readonly canvasElement_pointerup = (e: PointerEvent) => {
        if (this.isPanning) {
            this.canvasElement.releasePointerCapture(e.pointerId);
            this.canvasElement.classList.remove("panning");
            this.isPanning = false;
        }
    };

    private readonly canvasElement_dblclick = () => {
        // Reset
        this.zoomRatio = 1;
        this.top = this.timeLineHeight;
        this.left = 0;

        this.updateSpanLocations();

        this.setSelectedSpan(undefined);

        this.setHotSpan();

        this.render();
    };

    private readonly canvasElement_pointerout = (e: PointerEvent) => {
        this.hotSpan = undefined;
        this.hotSpansParents.clear();
        this.pointerX = -1;
        this.pointerY = -1;

        this.reportActiveSpan();

        this.render();
    };

    private readonly canvasElement_wheel = (e: WheelEvent) => {
        e.preventDefault();

        // Be a bit less speedy on trackpads (or mice with high resolution scrolling)
        const isTrackpad = e.deltaMode === WheelEvent.DOM_DELTA_PIXEL;
        const scrollFactor = isTrackpad ? 0.2 : 1;

        const isHorizontalScroll = Math.abs(e.deltaX) > Math.abs(e.deltaY);

        if (e.altKey) {
            // Pan via vertical scrolling with shift+alt
            const deltaX = e.shiftKey ? e.deltaY : e.deltaX;
            const deltaY = e.shiftKey ? e.deltaX : e.deltaY;

            this.left -= Math.round(deltaX * scrollFactor);
            this.top -= Math.round(deltaY * scrollFactor);
        } else if (isHorizontalScroll) {
            // Pan via horizontal scrolling
            this.left -= Math.round(e.deltaX * scrollFactor);
        } else {
            // Zoom
            const scrolledContentPosition = (this.pointerX - this.left) / (this.canvasElement.width * this.zoomRatio);

            const zoomIntensity = isTrackpad ? 0.05 : 0.3;
            const scale = e.deltaY > 0 ? (1 - zoomIntensity) : (1 + zoomIntensity);

            this.zoomRatio *= scale;

            this.left = this.pointerX - (scrolledContentPosition * (this.canvasElement.width * this.zoomRatio));

            this.updateSpanLocations();
        }

        this.render();
    };

    private readonly canvasElement_resized = (entries: ResizeObserverEntry[]) => {
        const devicePixelBox = entries[entries.length - 1]?.devicePixelContentBoxSize?.[0];

        // Zero is a box that isn't being painted rather than a measurement of one, and taking it at its
        // word collapses the canvas to a pixel. Fall back to the CSS size until it is painted again.
        this.observedDevicePixelWidth = devicePixelBox?.inlineSize || undefined;
        this.observedDevicePixelHeight = devicePixelBox?.blockSize || undefined;

        if (this.resizeCanvas()) {
            this.updateSpanLocations();
        }

        this.render();
    };

    private readonly devicePixelRatio_changed = () => {
        devicePixelRatioCache = window.devicePixelRatio;

        this.observedDevicePixelWidth = undefined;
        this.observedDevicePixelHeight = undefined;

        this.watchDevicePixelRatio();
        this.resizeCanvas();
        this.restyle();
    };

    /**
     * There is no devicePixelRatio event. A media query pinned to the current value stops matching when it
     * changes, and reports only once, so it is replaced each time.
     */
    private watchDevicePixelRatio() {
        this.devicePixelRatioQuery = matchMedia(`(resolution: ${devicePixelRatioCache}dppx)`);
        this.devicePixelRatioQuery.addEventListener("change", this.devicePixelRatio_changed, { once: true });
    }

    /**
     * The view holds no option of its own - the host owns them and pushes them in, which is what lets the
     * same renderer sit behind a query string, a settings panel, or nothing at all.
     */
    public setOptions(options: TraceViewOptions) {
        const isTimingChanged = options.adjustClockSkew !== this.adjustClockSkew;
        const isArrangeNeeded =
            isTimingChanged
            || options.groupSpans !== this.groupSpans
            || !isSameSet(options.hiddenSpanNames, this.hiddenSpanNames)
            || !isSameSet(options.hiddenSpanIds, this.hiddenSpanIds);

        this.groupSpans = options.groupSpans;
        this.adjustClockSkew = options.adjustClockSkew;
        this.hiddenSpanNames = options.hiddenSpanNames;
        this.hiddenSpanIds = options.hiddenSpanIds;

        this.applySelectedSpanId(options.selectedSpanId);

        if (!isArrangeNeeded) {
            return;
        }

        if (isTimingChanged) {
            this.updateSpanTimes();
        }

        this.arrangeSpans();
        this.updateSpanLocations();
        this.render();
    }

    /** A selection can arrive before the spans it names, so the id is kept and reapplied once they land. */
    private applySelectedSpanId(spanId?: string) {
        this.pendingSelectedSpanId = spanId;

        if (spanId === this.selectedSpan?.id) {
            return;
        }

        const span = spanId === undefined ? undefined : this.spansById.get(spanId);

        if (spanId !== undefined && span === undefined) {
            return;
        }

        this.applySelection(span);
        this.reportActiveSpan();
        this.render();
    }

    private setSelectedSpan(span?: SpanItem) {
        this.pendingSelectedSpanId = span?.id;

        this.applySelection(span);

        this.selectedSpanChangedCallback?.(span);

        this.reportActiveSpan();
        this.render();
    }

    private applySelection(span?: SpanItem) {
        this.selectedSpan = span;

        if (span === undefined) {
            this.selectedSpansParents.clear();
        } else {
            populateParents(this.selectedSpansParents, span);
        }
    }

    /** What the detail panel shows: whatever is under the pointer, or the pick when nothing is. */
    private reportActiveSpan() {
        const activeSpan = this.hotSpan ?? this.selectedSpan;

        if (this.lastReportedActiveSpanId === activeSpan?.id) {
            return;
        }

        this.lastReportedActiveSpanId = activeSpan?.id;
        this.activeSpanChangedCallback?.(activeSpan);
    }

    private setHotSpan() {
        // The inverse of the absolutePixelPositionY a row is drawn at.
        const hotRowIndex = Math.floor((this.pointerY - this.top) / this.spanRowOffset);

        this.hotSpan =
            (this.spansByRow[hotRowIndex] ?? [])
                .find(s =>
                    (this.left + s.absolutePixelPositionX) < this.pointerX
                    && (this.left + s.absolutePixelPositionX + s.pixelWidth) > this.pointerX
                );

        if (this.hotSpan === undefined) {
            this.hotSpansParents.clear();
        } else {
            populateParents(this.hotSpansParents, this.hotSpan);
        }

        this.reportActiveSpan();
    }

    private arrangeSpans() {
        this.spansByRow = [];

        // reset for fresh arrange
        for (const span of this.spans) {
            span.rowIndex = 0;
            span.childrenDepth = 0;
        }

        let spansToRemove = (this.hiddenSpanNames.size > 0 || this.hiddenSpanIds.size > 0)
            ? new Set(
                this.spans
                    .filter(s => this.hiddenSpanNames.has(s.name) || this.hiddenSpanIds.has(s.id))
                    .flatMap(s => [...getSpansDepthFirst(s)])
            )
            : undefined;

        this.filteredSpans =
            spansToRemove === undefined
                ? this.spans
                : this.spans.filter(s => !spansToRemove.has(s));

        if (this.groupSpans) {
            let spansDepthFirst = this.filteredSpans
                .filter(s => s.parent === undefined)
                .flatMap(s => [...getSpansDepthFirst(s)]);

            if (spansToRemove !== undefined) {
                spansDepthFirst = spansDepthFirst.filter(s => !spansToRemove.has(s));
            }

            for (const span of spansDepthFirst) {
                let isInserted = false;
                const isInSiblingSpan = (span.earlierSibling?.adjustedEndTimeMs ?? 0) > span.adjustedStartTimeMs;

                let rowIndex = isInSiblingSpan
                    ? span.earlierSibling!.rowIndex + span.earlierSibling!.childrenDepth + 1
                    : (span.parent?.rowIndex ?? -1) + 1;

                for (; rowIndex < this.spansByRow.length; rowIndex++) {
                    const rowSpans = this.spansByRow[rowIndex];
                    if (rowSpans[rowSpans.length - 1].adjustedEndTimeMs > span.adjustedStartTimeMs) {
                        continue;
                    }

                    span.rowIndex = rowIndex;
                    rowSpans.push(span);
                    isInserted = true;
                    break;
                }

                if (!isInserted) {
                    span.rowIndex = this.spansByRow.length;
                    this.spansByRow.push([span]);
                }

                span.absolutePixelPositionY = this.spanRowOffset * span.rowIndex;

                for (let depth = 1, parent = span.parent; parent !== undefined; depth++, parent = parent.parent) {
                    if (parent.childrenDepth < depth) {
                        parent.childrenDepth = depth;
                    }
                }
            }
        } else {
            const placedSpans = new Set<SpanItem>();

            // A child can start before its parent, so start order is not parents first. Placing the
            // ancestors on demand is what keeps a span below the one it descends from. Hiding a span
            // hides its descendants too, so an ancestor of anything filtered in is also filtered in.
            const placeSpan = (span: SpanItem) => {
                if (placedSpans.has(span)) {
                    return;
                }

                placedSpans.add(span);

                if (span.parent !== undefined) {
                    placeSpan(span.parent);
                }

                let isInserted = false;
                for (let rowIndex = (span.parent?.rowIndex ?? -1) + 1; rowIndex < this.spansByRow.length; rowIndex++) {
                    const rowSpans = this.spansByRow[rowIndex];
                    if (rowSpans[rowSpans.length - 1].adjustedEndTimeMs > span.adjustedStartTimeMs) {
                        continue;
                    }

                    span.rowIndex = rowIndex;
                    rowSpans.push(span);
                    isInserted = true;
                    break;
                }

                if (!isInserted) {
                    span.rowIndex = this.spansByRow.length;
                    this.spansByRow.push([span]);
                }

                span.absolutePixelPositionY = this.spanRowOffset * span.rowIndex;
            };

            for (const span of this.filteredSpans) {
                placeSpan(span);
            }
        }
    }

    private updateSpanColors() {
        let spanColorIndex = 0;
        const spanColorValues = new Map<unknown, string>();
        for (const span of this.spans) {
            const spanColorValue = span.attributes[this.spanColorSelector];
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

        // This gets unset with resizes etc, so just make sure it's always applied
        this.canvasContext.font = `${this.fontSize}px monospace`;
        this.canvasContext.textBaseline = "middle";

        for (const span of this.filteredSpans) {
            if (
                // Off screen to the left
                span.absolutePixelPositionX + span.pixelWidth + this.left < 0
                // Off screen to the right
                || span.absolutePixelPositionX + this.left > this.canvasElement.width
                // Off screen to the top
                || span.absolutePixelPositionY + this.spanHeightTotal + this.top < 0
                // Off screen to the bottom
                || span.absolutePixelPositionY + this.top > this.canvasElement.height) {
                continue;
            }

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

            switch (span.kind) {
                case SpanKind.Client:
                case SpanKind.Producer:
                    {
                        const left = this.getSpanLeft(span);
                        const bottom = this.getSpanTop(span) + this.spanHeightInner + this.spanBorderWidth;

                        this.canvasContext.beginPath();
                        this.canvasContext.moveTo(left, bottom)
                        this.canvasContext.lineTo(left + span.pixelWidth - this.spanBorderWidth, bottom);

                        this.canvasContext.strokeStyle = this.spanTransitionBorderColor;
                        this.canvasContext.lineWidth = this.spanBorderWidth;
                        this.canvasContext.stroke();

                        break;
                    }
                case SpanKind.Server:
                case SpanKind.Consumer:
                    {
                        const left = this.getSpanLeft(span);
                        const top = this.getSpanTop(span);

                        this.canvasContext.beginPath();
                        this.canvasContext.moveTo(left, top)
                        this.canvasContext.lineTo(left + span.pixelWidth - this.spanBorderWidth, top);

                        this.canvasContext.strokeStyle = this.spanTransitionBorderColor;
                        this.canvasContext.lineWidth = this.spanBorderWidth;
                        this.canvasContext.stroke();

                        break;
                    }
            }

            if (isHot || isSelected) {
                this.canvasContext.strokeStyle =
                    isHot
                        ? this.spanHotBorderColor
                        : this.spanActiveBorderColor;

                this.canvasContext.lineWidth = this.spanBorderWidth;
                this.canvasContext.strokeRect(
                    this.left + span.absolutePixelPositionX,
                    this.top + span.absolutePixelPositionY,
                    span.pixelWidth,
                    this.spanHeightTotal
                );
            }

            if (span.pixelWidth > this.characterPixelWidth) {
                const absoluteTextLeft = this.left + Math.round(span.absolutePixelPositionX + this.spanInnerPadding + this.spanBorderWidth);
                const absoluteTextWidth = span.pixelWidth - (this.spanBorderWidth * 2) - (this.spanInnerPadding * 2);
                const effectiveTextLeft = Math.max(0, absoluteTextLeft);
                const effectiveTextWidth = Math.min(this.canvasElement.width, this.canvasElement.width - absoluteTextLeft, absoluteTextWidth - (effectiveTextLeft - absoluteTextLeft), absoluteTextWidth);

                this.canvasContext.fillStyle = this.spanTextColor;
                this.canvasContext.fillText(
                    this.fitString(span.name, effectiveTextWidth),
                    effectiveTextLeft,
                    this.top + Math.round(span.absolutePixelPositionY + (this.spanHeightTotal / 2)) + 2,
                    effectiveTextWidth
                );

                const durationTextWidth = (this.characterPixelWidth * span.durationText.length);
                if ((this.characterPixelWidth * span.name.length) + durationTextWidth + 1 < effectiveTextWidth) {
                    this.canvasContext.fillText(
                        span.durationText,
                        (effectiveTextLeft + effectiveTextWidth) - durationTextWidth,
                        this.top + Math.round(span.absolutePixelPositionY + (this.spanHeightTotal / 2)) + 2,
                        durationTextWidth
                    );
                }
            }
        }

        this.canvasContext.clearRect(0, 0, this.canvasElement.width, this.timeLineHeight);
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
            this.canvasContext.fillRect(left, 0, 1, this.timeLineHeight);

            this.canvasContext.fillText(getTimeText(timeMs), segmentIndex * segmentWidth + 3, this.timeLineHeight / 2);
        }

        if (this.pointerX >= 0) {
            // Time line indicator
            this.canvasContext.fillStyle = this.timeLineColor;
            this.canvasContext.fillRect(this.pointerX, 0, 1, this.canvasElement.height);

            // Time text
            const timeText = getTimeText((this.pointerX - this.left) / msToPixels);

            this.canvasContext.fillStyle = this.hoverTextBackgroundColor;
            this.canvasContext.textBaseline = "top";
            this.canvasContext.fillRect(this.pointerX + 1, 0, (timeText.length + 2) * this.characterPixelWidth, this.fontSize);

            this.canvasContext.fillStyle = this.hoverTextColor;
            this.canvasContext.fillText(
                timeText,
                this.pointerX + this.characterPixelWidth,
                0
            );

            if (this.hotSpan !== undefined) {
                const padding = this.spanInnerPadding * 2;
                const itemHeight = this.fontSize + padding;

                this.canvasContext.fillStyle = this.hoverTextBackgroundColor;
                this.canvasContext.fillRect(this.pointerX + 1, this.pointerY + (itemHeight * 2), ((this.hotSpan.name.length + 2) * this.characterPixelWidth) + padding, itemHeight);
                this.canvasContext.fillRect(this.pointerX + 1, this.pointerY + (itemHeight * 3), (this.hotSpan.durationText.length + 2) * this.characterPixelWidth + padding, itemHeight);

                this.canvasContext.fillStyle = this.hoverTextColor;
                this.canvasContext.textBaseline = "top";
                this.canvasContext.fillText(this.hotSpan.name, this.pointerX + this.characterPixelWidth, this.pointerY + (itemHeight * 2) + this.spanInnerPadding);
                this.canvasContext.fillText(this.hotSpan.durationText, this.pointerX + this.characterPixelWidth, this.pointerY + (itemHeight * 3) + this.spanInnerPadding);
            }
        }
    }

    private renderSpanBackground(span: SpanItem) {
        this.canvasContext.fillRect(
            this.getSpanLeft(span),
            this.getSpanTop(span),
            span.pixelWidth - (this.spanBorderWidth * 2) + 2,
            this.spanHeightInner + 2
        );
    }

    private getSpanTop(span: SpanItem): number {
        return this.top + span.absolutePixelPositionY + this.spanBorderWidth - 1;
    }

    private getSpanLeft(span: SpanItem): number {
        return this.left + span.absolutePixelPositionX + this.spanBorderWidth - 1;
    }

    /**
     * Works out which clock each span was timed against. Redo this whenever the attributes naming a
     * clock change, since the offsets are then answering a different question.
     */
    private updateClockGroups() {
        for (const span of this.spans) {
            // The attribute name is part of the key, so a fallback can't collide with a real instance id.
            let clockGroup = "";

            for (const attributeName of this.clockGroupAttributeNames) {
                const value = span.attributes[attributeName];

                if (value !== undefined) {
                    clockGroup = attributeName + "=" + value;
                    break;
                }
            }

            span.clockGroup = clockGroup;
        }

        this.clockOffsetsByGroup = undefined;
    }

    /**
     * Applies the clock offsets, or takes them back off, and re-derives everything keyed off span times.
     */
    private updateSpanTimes() {
        // Solved on demand, so a trace nobody adjusts never pays for it.
        const offsets = this.adjustClockSkew
            ? this.clockOffsetsByGroup ??= solveClockOffsets(this.spans)
            : undefined;

        for (const span of this.spans) {
            const offsetMs = offsets?.get(span.clockGroup) ?? 0;

            span.adjustedStartTimeMs = span.startTimeMs + offsetMs;
            span.adjustedEndTimeMs = span.endTimeMs + offsetMs;
        }

        // Spans do share a start time, and a sort is only stable against the order already there, so
        // without the tiebreak a resort would arrange those ties differently to a first load.
        this.spans.sort((a, b) =>
            (a.adjustedStartTimeMs - b.adjustedStartTimeMs) || (a.sourceIndex - b.sourceIndex)
        );

        // Children are held in start order so that earlierSibling means what it says, and an offset
        // that moves one sibling past another changes that order.
        for (const span of this.spans) {
            span.children.length = 0;
            span.earlierSibling = undefined;
        }

        let startMs = Number.MAX_VALUE;
        let endMs = Number.MIN_VALUE;

        for (const span of this.spans) {
            if (span.adjustedStartTimeMs < startMs) {
                startMs = span.adjustedStartTimeMs;
            }

            if (span.adjustedEndTimeMs > endMs) {
                endMs = span.adjustedEndTimeMs;
            }

            if (span.parent === undefined) {
                continue;
            }

            span.earlierSibling = span.parent.children[span.parent.children.length - 1];
            span.parent.children.push(span);
        }

        this.startMs = startMs;
        this.durationMs = endMs - startMs;
    }

    private updateSpanLocations() {
        const msToPixels = (this.canvasElement.width / this.durationMs) * this.zoomRatio;

        for (const span of this.filteredSpans) {
            span.absolutePixelPositionX = (span.adjustedStartTimeMs - this.startMs) * msToPixels;
            span.pixelWidth = (span.adjustedEndTimeMs - span.adjustedStartTimeMs) * msToPixels;
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

    public dispose() {
        this.canvasElement.removeEventListener("pointermove", this.canvasElement_pointermove);
        this.canvasElement.removeEventListener("pointerdown", this.canvasElement_pointerdown);
        this.canvasElement.removeEventListener("pointerup", this.canvasElement_pointerup);
        this.canvasElement.removeEventListener("pointercancel", this.canvasElement_pointerup);
        this.canvasElement.removeEventListener("dblclick", this.canvasElement_dblclick);
        this.canvasElement.removeEventListener("pointerout", this.canvasElement_pointerout);
        this.canvasElement.removeEventListener("wheel", this.canvasElement_wheel);

        this.resizeObserver.disconnect();
        this.devicePixelRatioQuery?.removeEventListener("change", this.devicePixelRatio_changed);
    }
}

function* getSpansDepthFirst(span: SpanItem): Generator<SpanItem, void, unknown> {
    yield span;

    for (const child of span.children) {
        yield* getSpansDepthFirst(child);
    }
}

/**
 * Separate processes don't share a clock, so a child's recorded start can land before its parent's.
 * Taking each clock to be off by a constant turns that into one constraint per parent-child edge
 * crossing two clocks - offset[child] - offset[parent] >= parentStart - childStart - and relaxing them
 * to a fixpoint is Bellman-Ford. Constraints that contradict each other run out of passes still
 * unsatisfied, which is why row placement can't assume this worked.
 */
function solveClockOffsets(spans: SpanItem[]): Map<string, number> {
    const offsets = new Map<string, number>();

    for (const span of spans) {
        if (!offsets.has(span.clockGroup)) {
            offsets.set(span.clockGroup, 0);
        }
    }

    if (offsets.size < 2) {
        return offsets;
    }

    // Keyed parent group, then child group; a single string key would need a separator that no
    // attribute value can contain.
    const constraintsByParentGroup = new Map<string, Map<string, number>>();

    for (const span of spans) {
        if (span.parent === undefined) {
            continue;
        }

        const parentGroup = span.parent.clockGroup;
        const childGroup = span.clockGroup;

        if (parentGroup === childGroup) {
            continue;
        }

        let constraints = constraintsByParentGroup.get(parentGroup);

        if (constraints === undefined) {
            constraints = new Map<string, number>();
            constraintsByParentGroup.set(parentGroup, constraints);
        }

        // The tightest edge between two clocks is the one that has to be satisfied.
        const minimumOffsetMs = span.parent.startTimeMs - span.startTimeMs;
        const existingMs = constraints.get(childGroup);

        if (existingMs === undefined || minimumOffsetMs > existingMs) {
            constraints.set(childGroup, minimumOffsetMs);
        }
    }

    for (let pass = 0; pass < offsets.size; pass++) {
        let isChanged = false;

        for (const [parentGroup, constraints] of constraintsByParentGroup) {
            for (const [childGroup, minimumOffsetMs] of constraints) {
                const requiredMs = offsets.get(parentGroup)! + minimumOffsetMs;

                if (requiredMs > offsets.get(childGroup)!) {
                    offsets.set(childGroup, requiredMs);
                    isChanged = true;
                }
            }
        }

        if (!isChanged) {
            break;
        }
    }

    // Only the differences carry meaning, so rebase them to keep every span at or after where it was.
    // Spread into Math.min instead of this and a trace with a lot of clocks in it overflows the stack.
    let smallestOffsetMs = Number.MAX_VALUE;

    for (const offsetMs of offsets.values()) {
        if (offsetMs < smallestOffsetMs) {
            smallestOffsetMs = offsetMs;
        }
    }

    for (const [group, offsetMs] of offsets) {
        offsets.set(group, offsetMs - smallestOffsetMs);
    }

    return offsets;
}

/** Commas, but not the ones inside rgb() and friends. */
function splitList(value: string): string[] {
    const items: string[] = [];
    let depth = 0;
    let start = 0;

    for (let index = 0; index < value.length; index++) {
        const character = value[index];

        if (character === "(") {
            depth++;
        } else if (character === ")") {
            depth--;
        } else if (character === "," && depth === 0) {
            items.push(value.slice(start, index));
            start = index + 1;
        }
    }

    items.push(value.slice(start));

    return items.map(i => i.trim()).filter(i => i.length > 0);
}

function isSameSet(a: Set<string>, b: Set<string>): boolean {
    if (a.size !== b.size) {
        return false;
    }

    for (const value of a) {
        if (!b.has(value)) {
            return false;
        }
    }

    return true;
}

function populateParents(parents: Set<SpanItem>, span: SpanItem): void {
    parents.clear();

    let current = span.parent;
    while (current != undefined) {
        parents.add(current);
        current = current.parent;
    }
}
