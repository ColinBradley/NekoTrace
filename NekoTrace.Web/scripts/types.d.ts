export interface SpanEvent {
    readonly name: string;
    readonly time: string;
    readonly attributes: Record<string, string | number | boolean>;
}

export interface SpanData {
    readonly id: string;
    readonly parentSpanId?: string;
    readonly name: string;
    readonly kind: SpanKind;
    readonly attributes: Record<string, string | number | boolean>;
    readonly startTime: string;
    readonly startTimeMs: number;
    readonly endTime: string;
    readonly endTimeMs: number;
    readonly statusCode: unknown;
    readonly statusMessage?: string;
    readonly traceState?: string;
    readonly events: SpanEvent[];
    readonly flags: number;
    readonly links: Record<string, string | number | boolean>[];
}

export type SpanKindUnspecified = 0;
export type SpanKindInternal = 1;
export type SpanKindServer = 2;
export type SpanKindClient = 3;
export type SpanKindProducer = 4;
export type SpanKindConsumer = 5;
export type SpanKind = SpanKindUnspecified | SpanKindInternal | SpanKindServer | SpanKindClient | SpanKindProducer | SpanKindConsumer;
