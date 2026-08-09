namespace NekoTrace.Web.Analysis.Results;

using System.Collections.Immutable;

/// <summary>
/// Every span that reached the same place in the call tree, merged into one node. See <see cref="TraceProfile"/>.
/// </summary>
internal sealed record ProfileNode
{
    public required string Name { get; init; }

    public required DurationStatistics Durations { get; init; }

    /// <summary>Summed self time, which is what points at a bottleneck rather than at its callers.</summary>
    public required double SelfMs { get; init; }

    public required int ErrorCount { get; init; }

    /// <summary>The slowest span merged into this node, so the raw one can be fetched and read in full.</summary>
    public required string SlowestSpanId { get; init; }

    /// <summary>How many distinct child shapes were merged. Above one, the members are not all alike.</summary>
    public required int DistinctChildShapes { get; init; }

    /// <summary>Ordered by total duration, descending.</summary>
    public required ImmutableArray<ProfileNode> Children { get; init; }

    public int Count => this.Durations.Count;
}
