namespace NekoTrace.Web.Repositories.Traces;

using System.Collections.Immutable;

public sealed record TraceSerializableData
{
    public required string Id { get; init; }

    public required ImmutableArray<SpanData> Spans { get; init; }
}
