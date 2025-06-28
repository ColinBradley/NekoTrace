export interface SpanEvent {
    name: string;
    time: string; // ISO 8601 string (DateTimeOffset in C#)
    attributes: Record<string, unknown>;
}

export interface SpanData {
    id: string;
    parentSpanId: string | null;
    name: string;
    kind: SpanKind;
    attributes: Record<string, unknown>;
    startTime: string; // ISO 8601 string (DateTimeOffset in C#)
    endTime: string;   // ISO 8601 string (DateTimeOffset in C#)
    statusCode: unknown;
    statusMessage: string | null;
    traceState: string | null;
    events: SpanEvent[];
    flags: number;
    links: Record<string, unknown>[];
}

export type SpanKindUnspecified = 0;
export type SpanKindInternal = 1;
export type SpanKindServer = 2;
export type SpanKindClient = 3;
export type SpanKindProducer = 4;
export type SpanKindConsumer = 5;
export type SpanKind = SpanKindUnspecified | SpanKindInternal | SpanKindServer | SpanKindClient | SpanKindProducer | SpanKindConsumer;
