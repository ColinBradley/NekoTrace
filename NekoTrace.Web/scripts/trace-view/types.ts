export type AttributeValue = string | number | boolean | null;

export interface SpanEvent {
    readonly name: string;
    readonly time: string;
    readonly attributes: Record<string, AttributeValue>;
}

export interface SpanData {
    readonly id: string;
    readonly parentSpanId?: string;
    readonly name: string;
    readonly kind: SpanKind;
    readonly attributes: Record<string, AttributeValue>;
    readonly startTimeMs: number;
    readonly endTimeMs: number;
    readonly statusCode: StatusCode;
    readonly statusMessage?: string;
    readonly events: SpanEvent[];
}

export interface SpanItem extends SpanData {
    parent?: SpanItem;
    readonly children: SpanItem[];
    color: string;

    /** The duration as it is drawn and listed, worked out once when the span arrives. */
    readonly durationText: string;

    rowIndex: number;
    childrenDepth: number;
    earlierSibling?: SpanItem;

    /** Position in the list as it arrived, so that ordering stays put across a resort. */
    readonly sourceIndex: number;

    /** Which clock this span's times came off, as worked out by updateClockGroups. */
    clockGroup: string;

    /** The recorded time, plus this span's clock offset when that adjustment is switched on. */
    adjustedStartTimeMs: number;
    adjustedEndTimeMs: number;

    absolutePixelPositionX: number;
    absolutePixelPositionY: number;
    pixelWidth: number;
}

export interface TraceViewData {
    readonly spans: SpanData[];

    /** The longest a duration of each span across every trace, by span name. */
    readonly maxSpanDurationMsByName?: Record<string, number>;
}

export const SpanKind = {
    Unspecified: 0,
    Internal: 1,
    Server: 2,
    Client: 3,
    Producer: 4,
    Consumer: 5,
} as const;

export type SpanKind = typeof SpanKind[keyof typeof SpanKind];

export const StatusCode = {
    Unset: 0,
    Ok: 1,
    Error: 2,
} as const;

export type StatusCode = typeof StatusCode[keyof typeof StatusCode];

const SPAN_KIND_NAMES = Object.fromEntries(
    Object.entries(SpanKind).map(([name, value]) => [value, name])
) as Record<number, string>;

export function getSpanKindName(kind: SpanKind): string {
    return SPAN_KIND_NAMES[kind] ?? SPAN_KIND_NAMES[SpanKind.Unspecified]!;
}
