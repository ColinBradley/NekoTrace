namespace NekoTrace.Web.Analysis.Results;

using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

/// <summary>Errors sharing a cause, so that one repeated failure reads as one finding.</summary>
internal sealed record ErrorClass
{
    public required string SpanName { get; init; }

    public required string? ErrorType { get; init; }

    public required string? HttpStatusCode { get; init; }

    public required int Count { get; init; }

    public required string? Message { get; init; }

    /// <summary>Every member, so samples can be drawn from each class in turn.</summary>
    public required ImmutableArray<SpanData> Members { get; init; }
}
