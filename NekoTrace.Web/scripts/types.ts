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

/*
 * Objects rather than enums, because an enum is the one TypeScript construct here that has to be
 * compiled rather than merely stripped, and Node runs these files as they are to test them.
 * `erasableSyntaxOnly` in tsconfig.json is what keeps it that way. Using them reads the same.
 */

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
