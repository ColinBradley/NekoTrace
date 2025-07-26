namespace NekoTrace.Web.Repositories;

using System.Collections.Immutable;

public sealed record TraceData
{
    public required string Id { get; init; }

    public required ImmutableArray<SpanData> Spans { get; init; }
}
