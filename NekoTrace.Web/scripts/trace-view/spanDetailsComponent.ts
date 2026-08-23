import { createCopyableValueElement } from "./copyValueComponent.ts";
import { formatAttributeValue, formatDuration, formatTime } from "./localValueFormatting.ts";
import { getSpanKindName, type AttributeValue, type SpanItem } from "./types.ts";
import { buildUrlWithAddedFilterValue, queryOptionNames } from "./urlState.ts";

const HIDE_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <path d="M10.7 5.1A10.9 10.9 0 0 1 12 5c6.5 0 10 7 10 7a19.9 19.9 0 0 1-3.1 4.2" />
    <path d="M6.2 6.7A19.7 19.7 0 0 0 2 12s3.5 7 10 7a10.6 10.6 0 0 0 4.5-1" />
    <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
    <path d="m2 2 20 20" />
</svg>`;

export interface SpanDetailsSettings {
    readonly hiddenAttributeNames: Set<string>;

    /** Where a span name links to, with {name} standing in for it. Nothing links when it is unset. */
    readonly spanNameHrefTemplate?: string;

    /** The longest each span name has taken anywhere, which is what the performance figure is a share of. */
    readonly maxSpanDurationMsByName?: Record<string, number>;
}

export class SpanDetailsComponent {

    private readonly root: HTMLElement;

    private span?: SpanItem;
    private traceStartMs = 0;
    private settings: SpanDetailsSettings = { hiddenAttributeNames: new Set() };

    public constructor(root: HTMLElement) {
        this.root = root;
    }

    public setSettings(settings: SpanDetailsSettings) {
        this.settings = settings;

        this.render();
    }

    public setTraceStartMs(value: number) {
        this.traceStartMs = value;
    }

    public setSpan(span?: SpanItem) {
        this.span = span;

        this.render();
    }

    private render() {
        this.root.replaceChildren();

        const span = this.span;
        if (span === undefined) {
            return;
        }

        this.root.append(this.createHideLinks(span), this.createSpanList(span));

        if (span.events.length > 0) {
            const heading = document.createElement("h2");
            heading.textContent = "Events";

            const events = document.createElement("div");
            events.className = "events";
            events.append(...span.events.map(e => this.createEventList(e)));

            this.root.append(heading, events);
        }
    }

    private createHideLinks(span: SpanItem): HTMLElement {
        const container = document.createElement("div");
        container.className = "span-links";

        container.append(
            createLink("Hide span", buildUrlWithAddedFilterValue(queryOptionNames.hiddenSpanIds, span.id)),
            createLink("Hide spans with name", buildUrlWithAddedFilterValue(queryOptionNames.hiddenSpanNames, span.name))
        );

        return container;
    }

    private createSpanList(span: SpanItem): HTMLElement {
        const list = document.createElement("dl");
        list.className = "span-info";

        const nameHref = this.settings.spanNameHrefTemplate?.replace("{name}", encodeURIComponent(span.name));

        list.append(
            ...createRow("Name", "name", span.name, nameHref === undefined ? undefined : createLink(span.name, nameHref)),
            ...createRow("Start", "duration", formatDuration(span.adjustedStartTimeMs - this.traceStartMs)),
            ...createRow("Duration", "duration", span.durationText),
            ...createRow("Performance", "performance", this.getPerformanceText(span)),
            ...createRow("Kind", "kind", getSpanKindName(span.kind)),
            ...createRow("Parent", "parent", span.parent?.name ?? "")
        );

        if (span.statusMessage !== undefined && span.statusMessage !== null && span.statusMessage !== "") {
            list.append(...createRow("Message", "status-message", span.statusMessage));
        }

        const attributeNames = Object.keys(span.attributes)
            .filter(k => !this.settings.hiddenAttributeNames.has(k))
            .sort((a, b) => a.localeCompare(b));

        for (const name of attributeNames) {
            list.append(...this.createAttributeRow(name, span.attributes[name]));
        }

        return list;
    }

    private createEventList(spanEvent: SpanItem["events"][number]): HTMLElement {
        const list = document.createElement("dl");
        list.className = "event span-info";

        list.append(
            ...createRow("Name", "name", spanEvent.name),
            ...createRow("Time", "time", formatTime(spanEvent.time))
        );

        // Deliberately not sorted: an event's attributes read best in the order the exporter wrote them.
        for (const [name, value] of Object.entries(spanEvent.attributes)) {
            if (this.settings.hiddenAttributeNames.has(name)) {
                continue;
            }

            list.append(...this.createAttributeRow(name, value));
        }

        return list;
    }

    private createAttributeRow(name: string, value: AttributeValue | undefined): [HTMLElement, HTMLElement] {
        const key = document.createElement("dt");
        key.className = "key";
        key.append(name, createHideAttributeButton(name));

        const cell = document.createElement("dd");
        cell.className = "value";
        cell.append(createCopyableValueElement(formatAttributeValue(value)));

        return [key, cell];
    }

    /**
     * How this span compares to the slowest one of its name the host has ever seen, as a percentage. Only
     * the host can know that, so the figure is simply absent when it did not supply the numbers.
     */
    private getPerformanceText(span: SpanItem): string {
        const maxDurationMs = this.settings.maxSpanDurationMsByName?.[span.name];

        if (maxDurationMs === undefined || maxDurationMs <= 0) {
            return "";
        }

        const percentage = ((span.endTimeMs - span.startTimeMs) / maxDurationMs) * 100;

        return "p" + Number(percentage.toPrecision(3)).toLocaleString();
    }
}

function createRow(label: string, valueClass: string, value: string, content?: Node): [HTMLElement, HTMLElement] {
    const key = document.createElement("dt");
    key.textContent = label;

    const cell = document.createElement("dd");
    cell.className = valueClass;
    cell.append(createCopyableValueElement(value, content));

    return [key, cell];
}

function createLink(text: string, href: string): HTMLAnchorElement {
    const link = document.createElement("a");
    link.href = href;
    link.textContent = text;

    return link;
}

function createHideAttributeButton(name: string): HTMLAnchorElement {
    const button = document.createElement("a");
    button.className = "hide-button";
    button.title = "Hide attribute";
    button.href = buildUrlWithAddedFilterValue(queryOptionNames.hiddenAttributeNames, name);
    button.innerHTML = HIDE_ICON;

    return button;
}
