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
    readonly startTimeMs: number;
    readonly endTimeMs: number;
    readonly statusCode: StatusCode;
    readonly events: SpanEvent[];
    readonly durationText: string;
}

export enum SpanKind {
    Unspecified = 0,
    Internal = 1,
    Server = 2,
    Client = 3,
    Producer = 4,
    Consumer = 5,
}

export enum StatusCode {
    Unset = 0,
    Ok = 1,
    Error = 2,
}