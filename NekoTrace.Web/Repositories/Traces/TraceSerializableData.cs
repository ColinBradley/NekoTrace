namespace NekoTrace.Web.Repositories.Traces;

using System.Collections.Immutable;

public sealed record TraceSerializableData
{
    // Written before the format carried a version. Base64 ids throughout, and old enough that some files
    // predate SpanData.TraceId existing at all, which makes them unreadable.
    public const int LEGACY_VERSION = 0;

    // Lowercase hex ids.
    public const int CURRENT_VERSION = 1;

    // Deliberately not `required`: legacy files have no version property and must still deserialise.
    // Bump CURRENT_VERSION whenever the shape changes so readers don't have to guess from the data.
    public int Version { get; init; } = LEGACY_VERSION;

    public required string Id { get; init; }

    public required ImmutableArray<SpanData> Spans { get; init; }
}
