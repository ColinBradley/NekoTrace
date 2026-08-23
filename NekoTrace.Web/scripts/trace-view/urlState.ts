export interface TraceViewOptions {
    readonly groupSpans: boolean;
    readonly adjustClockSkew: boolean;
    readonly selectedSpanId?: string;
    readonly hiddenSpanNames: Set<string>;
    readonly hiddenSpanIds: Set<string>;
    readonly hiddenAttributeNames: Set<string>;
}

export const queryOptionNames = {
    groupSpans: "groupSpans",
    adjustClockSkew: "adjustClockSkew",
    selectedSpanId: "selectedSpanId",
    hiddenSpanNames: "hiddenSpanNames",
    hiddenSpanIds: "hiddenSpanIds",
    hiddenAttributeNames: "hiddenAttributeNames",
} as const;

export function readOptionsFromUrl(): TraceViewOptions {
    const searchParams = new URL(document.URL).searchParams;

    return {
        groupSpans: searchParams.get(queryOptionNames.groupSpans)?.toLowerCase() !== "false",
        adjustClockSkew: searchParams.get(queryOptionNames.adjustClockSkew)?.toLowerCase() === "true",
        selectedSpanId: searchParams.get(queryOptionNames.selectedSpanId) ?? undefined,
        hiddenSpanNames: new Set(splitFilterValue(searchParams.get(queryOptionNames.hiddenSpanNames))),
        hiddenSpanIds: new Set(splitFilterValue(searchParams.get(queryOptionNames.hiddenSpanIds))),
        hiddenAttributeNames: new Set(splitFilterValue(searchParams.get(queryOptionNames.hiddenAttributeNames))),
    };
}

export function splitFilterValue(value: string | null | undefined): string[] {
    return value === null || value === undefined || value === ""
        ? []
        : value.split("|").filter(v => v.length > 0);
}

/**
 * The one way view state is changed: put the parameter in the URL and let whatever is listening for the
 * URL to change do the rest. Nothing applies an option as well as writing it, so the two cannot disagree.
 */
export function writeUrlParameter(name: string, value: string | undefined): void {
    history.replaceState(null, "", buildUrlWithParameter(name, value))
}

/** A link to the current page with one parameter changed, or removed when the value is undefined. */
export function buildUrlWithParameter(name: string, value: string | undefined): string {
    const url = new URL(document.URL);

    if (value === undefined) {
        url.searchParams.delete(name);
    } else {
        url.searchParams.set(name, value);
    }

    return url.toString();
}

/** A link to the current page with one more value in a pipe separated parameter. */
export function buildUrlWithAddedFilterValue(name: string, added: string): string {
    const current = new URL(document.URL).searchParams.get(name);
    const values = splitFilterValue(current);

    if (!values.includes(added)) {
        values.push(added);
    }

    return buildUrlWithParameter(name, values.join("|"));
}

/*
 * Make history state changes observable.
 */
const originalPushState = history.pushState;
history.pushState = function pushState(...a) {
    originalPushState.apply(this, a);

    window.dispatchEvent(new Event("pushstate"));
    window.dispatchEvent(new Event("locationchange"));
};

const originalReplaceState = history.replaceState;
history.replaceState = function replaceState(...a) {
    originalReplaceState.apply(this, a);

    window.dispatchEvent(new Event("replacestate"));
    window.dispatchEvent(new Event("locationchange"));
};
