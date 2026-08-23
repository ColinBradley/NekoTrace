// Imported for the side effect of defining the element as much as for the types it brings over.
import { TraceViewElement, type TraceViewData } from "./trace-view/index.ts";

/*
 * The Blazor side of the trace view. Nothing under trace-view/ knows what .NET is, and nothing in there may
 * import from outside it - that is what keeps it liftable into a package of its own.
 */

interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<void>;
}

let reportUrlChanged: ((url: string) => void) | undefined;
let lastReportedUrl = location.href;

/*
 * Registered once for the page, not inside initialize, which runs again every time the trace gains spans.
 *
 * NavigationManager keeps its own idea of the current URL, and a stale one means every link the surrounding
 * page builds is built from the wrong place.
 */
window.addEventListener("locationchange", () => {
    // NavigateTo writes the entry again, which lands back here.
    if (location.href === lastReportedUrl) {
        return;
    }

    lastReportedUrl = location.href;

    reportUrlChanged?.(location.href);
});

export function initialize(
    element: TraceViewElement,
    trace: TraceViewData,
    navigateCallback: DotNetObjectReference,
    navigateMethodName: string
) {
    reportUrlChanged = url => void navigateCallback.invokeMethodAsync(navigateMethodName, url);
    lastReportedUrl = location.href;

    element.trace = trace;
}
