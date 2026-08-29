import { TraceOptionsComponent } from "./traceOptionsComponent.ts";
import { SpanDetailsComponent, type SpanDetailsSettings } from "./spanDetailsComponent.ts";
import { installStyles } from "./styles.ts";
import { TraceRenderer } from "./traceView.ts";
import type { TraceViewData } from "./types.ts";
import {
    queryOptionNames,
    readOptionsFromUrl,
    writeUrlParameter,
    type TraceViewOptions,
} from "./urlState.ts";

installStyles();

export const ELEMENT_NAME = "neko-trace-view";

export const DEFAULT_SPAN_COLOR_SELECTOR = "otel.library.name";

const SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME = "data-span-color-selector";
const SPAN_NAME_HREF_ATTRIBUTE_NAME = "data-span-name-href";

/**
 * The whole viewer as one element: control bar, flame graph and the detail panel.
 * Light DOM on purpose, avoiding shadow root to make styling easier.
 */
export class TraceViewElement extends HTMLElement {

    static observedAttributes = [SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME, SPAN_NAME_HREF_ATTRIBUTE_NAME];

    private controlsElement?: HTMLElement;
    private canvasElement?: HTMLCanvasElement;
    private detailsElement?: HTMLElement;

    private renderer?: TraceRenderer;
    private details?: SpanDetailsComponent;
    private controls?: TraceOptionsComponent;

    private traceData?: TraceViewData;
    private options: TraceViewOptions = readOptionsFromUrl();

    public connectedCallback() {
        this.build();

        window.addEventListener("locationchange", this.window_urlChanged);
        window.addEventListener("popstate", this.window_urlChanged);
    }

    public disconnectedCallback() {
        window.removeEventListener("locationchange", this.window_urlChanged);
        window.removeEventListener("popstate", this.window_urlChanged);

        this.renderer?.dispose();

        this.renderer = undefined;
        this.details = undefined;
        this.controls = undefined;
    }

    public attributeChangedCallback() {
        this.renderer?.setSpanColorSelector(this.spanColorSelector);
        this.details?.setSettings(this.getDetailsSettings());
    }

    /** The trace to show. Everything the view knows arrives this way; it fetches nothing for itself. */
    public set trace(value: TraceViewData) {
        this.traceData = value;

        this.build();

        this.renderer!.setSpans(value.spans);
        this.details!.setTraceStartMs(this.renderer!.traceStartMs);
        this.details!.setSettings(this.getDetailsSettings());
    }

    public get trace(): TraceViewData | undefined {
        return this.traceData;
    }

    /** Re-reads the colours and font from CSS. Call it after a theme change; nothing else needs to. */
    public reloadStyle() {
        this.renderer?.reloadStyle();
    }

    private get spanColorSelector(): string {
        return this.getAttribute(SPAN_COLOR_SELECTOR_ATTRIBUTE_NAME) ?? DEFAULT_SPAN_COLOR_SELECTOR;
    }

    private build() {
        if (this.renderer !== undefined) {
            return;
        }

        if (this.controlsElement === undefined) {
            this.controlsElement = document.createElement("div");
            this.controlsElement.className = "trace-view-controls";

            this.canvasElement = document.createElement("canvas");
            this.canvasElement.className = "trace-view-canvas";

            this.detailsElement = document.createElement("div");
            this.detailsElement.className = "trace-view-span-details";

            // The grid sits one level in because a container query styles descendants, not the container.
            const layout = document.createElement("div");
            layout.className = "trace-view-layout";
            layout.append(this.controlsElement, this.canvasElement, this.detailsElement);

            this.append(layout);
        } else {
            this.controlsElement.replaceChildren();
        }

        this.renderer = new TraceRenderer(this.canvasElement!);
        this.renderer.setSpanColorSelector(this.spanColorSelector);
        this.renderer.setActiveSpanChangedCallback(span => this.details!.setSpan(span));
        this.renderer.setSelectedSpanChangedCallback(
            span => writeUrlParameter(queryOptionNames.selectedSpanId, span?.id)
        );

        this.details = new SpanDetailsComponent(this.detailsElement!);
        this.controls = new TraceOptionsComponent(
            this.controlsElement
        );

        if (this.traceData !== undefined) {
            this.renderer.setSpans(this.traceData.spans);
        }

        this.applyOptions(readOptionsFromUrl());
    }

    private readonly window_urlChanged = () => {
        this.applyOptions(readOptionsFromUrl());
    };

    private applyOptions(options: TraceViewOptions) {
        this.options = options;

        this.renderer?.setOptions(options);
        this.controls?.setOptions(options);
        this.details?.setTraceStartMs(this.renderer?.traceStartMs ?? 0);
        this.details?.setSettings(this.getDetailsSettings());
    }

    private getDetailsSettings(): SpanDetailsSettings {
        return {
            hiddenAttributeNames: this.options.hiddenAttributeNames,
            expandValues: this.options.expandSpanValues,
            spanNameHrefTemplate: this.getAttribute(SPAN_NAME_HREF_ATTRIBUTE_NAME) ?? undefined,
            maxSpanDurationMsByName: this.traceData?.maxSpanDurationMsByName,
        };
    }
}

if (customElements.get(ELEMENT_NAME) === undefined) {
    customElements.define(ELEMENT_NAME, TraceViewElement);
}

export { TraceRenderer } from "./traceView.ts";
export type { AttributeValue, SpanData, SpanEvent, TraceViewData } from "./types.ts";
