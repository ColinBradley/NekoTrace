namespace NekoTrace.Web.Repositories;

using System.Collections.Immutable;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed record SpanData
{
    private readonly Lazy<TimeSpan> mDuration;

    public SpanData()
    {
        mDuration = new(() => this.EndTime - this.StartTime, LazyThreadSafetyMode.None);
    }

    public required string Id { get; init; }

    public required string? ParentSpanId { get; init; }

    public required string Name { get; init; }

    public required SpanKind Kind { get; init; }

    public required Dictionary<string, object?> Attributes { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required double StartTimeMs { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    public required double EndTimeMs { get; init; }

    public required StatusCode StatusCode { get; init; }

    public required string? StatusMessage { get; init; }

    public required string? TraceState { get; init; }

    public required ImmutableArray<SpanEvent> Events { get; init; }

    public required ImmutableArray<Dictionary<string, object?>> Links { get; init; }

    public TimeSpan Duration => mDuration.Value;

    public string DurationText
    {
        get
        {
            var durationMs = this.Duration.TotalMilliseconds;
            return durationMs switch
            {
                < 1 => Math.Round(durationMs * 1000, 1) + "µs",
                >= 1000 => Math.Round(durationMs / 1000, 2) + "s",
                _ => Math.Round(durationMs, 1) + "ms",
            };
        }
    }
}
