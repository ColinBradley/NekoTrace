namespace NekoTrace.Web.Repositories.Traces;

using System.Collections.Immutable;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed record SpanDataSlim
{
    private readonly Lazy<double> mDuration;

    public SpanDataSlim()
    {
        mDuration = new(() => this.EndTimeMs - this.StartTimeMs, LazyThreadSafetyMode.None);
    }

    public required string Id { get; init; }

    public required string? ParentSpanId { get; init; }

    public required string Name { get; init; }

    public required SpanKind Kind { get; init; }

    public required Dictionary<string, object?> Attributes { get; init; }

    public required double StartTimeMs { get; init; }

    public required double EndTimeMs { get; init; }

    public required StatusCode StatusCode { get; init; }

    public required ImmutableArray<SpanEvent> Events { get; init; }

    public string DurationText
    {
        get
        {
            var durationMs = mDuration.Value;
            return durationMs switch
            {
                < 1 => Math.Round(durationMs * 1000, 1) + "µs",
                >= 1000 => Math.Round(durationMs / 1000, 2) + "s",
                _ => Math.Round(durationMs, 1) + "ms",
            };
        }
    }
}
