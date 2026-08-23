const numberFormatters = new Map<number, Intl.NumberFormat>();

function formatNumber(value: number, maximumFractionDigits: number): string {
    let formatter = numberFormatters.get(maximumFractionDigits);

    if (formatter === undefined) {
        formatter = new Intl.NumberFormat(undefined, { maximumFractionDigits });
        numberFormatters.set(maximumFractionDigits, formatter);
    }

    return formatter.format(value);
}

/** Picks a unit that keeps the number readable, the way a duration column wants rather than a sort does. */
export function formatDuration(durationMs: number): string {
    if (durationMs < 0) {
        return "-" + formatDuration(-durationMs);
    }

    if (durationMs < 1) {
        return formatNumber(durationMs * 1000, 1) + "\u00B5s";
    }

    if (durationMs >= 1000) {
        return formatNumber(durationMs / 1000, 2) + "s";
    }

    return formatNumber(durationMs, 1) + "ms";
}

let dateTimeFormatter: Intl.DateTimeFormat | undefined;

/** A wall clock time in the viewer's zone, kept to milliseconds because span events land that close together. */
export function formatTime(isoTime: string): string {
    const time = new Date(isoTime);

    if (Number.isNaN(time.getTime())) {
        return isoTime;
    }

    // Spelt out rather than dateStyle/timeStyle, which Intl refuses to combine with a fractional second.
    dateTimeFormatter ??= new Intl.DateTimeFormat(
        undefined,
        {
            year: "numeric",
            month: "numeric",
            day: "numeric",
            hour: "numeric",
            minute: "2-digit",
            second: "2-digit",
            fractionalSecondDigits: 3,
        }
    );

    return dateTimeFormatter.format(time);
}

export function formatAttributeValue(value: unknown): string {
    if (value === null || value === undefined) {
        return "";
    }

    if (typeof value === "number") {
        return formatNumber(value, 20);
    }

    if (typeof value === "object") {
        return JSON.stringify(value);
    }

    return String(value);
}
