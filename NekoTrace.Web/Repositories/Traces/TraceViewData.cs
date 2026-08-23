namespace NekoTrace.Web.Repositories.Traces;

/// <summary>
/// Everything the trace view is given.
/// </summary>
public sealed record TraceViewData
{
    public required IEnumerable<SpanDataSlim> Spans { get; init; }

    public required Dictionary<string, double> MaxSpanDurationMsByName { get; init; }
}
