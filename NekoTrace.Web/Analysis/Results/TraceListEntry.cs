namespace NekoTrace.Web.Analysis.Results;

/// <summary>One row of the trace list.</summary>
internal sealed record TraceListEntry
{
    public required string Id { get; init; }

    /// <summary>Null until the root span arrives, which it may do last or not at all.</summary>
    public required string? RootSpanName { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required double DurationMs { get; init; }

    public required int SpanCount { get; init; }

    public required bool HasError { get; init; }
}
