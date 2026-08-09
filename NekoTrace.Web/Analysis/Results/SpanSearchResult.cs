namespace NekoTrace.Web.Analysis.Results;

using NekoTrace.Web.Repositories.Traces;

/// <summary>A span found by a search, carrying the trace it belongs to so it can be fetched again.</summary>
internal sealed record SpanSearchResult
{
    public required string TraceId { get; init; }

    public required SpanData Span { get; init; }
}
