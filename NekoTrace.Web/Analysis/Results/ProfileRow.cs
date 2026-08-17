namespace NekoTrace.Web.Analysis.Results;

/// <summary>
/// One node of a <see cref="ProfileNode"/> tree with its path spelled out, so the profile can be handed over
/// as a list rather than as nesting.
/// </summary>
/// <remarks>
/// The JSON model for the profile endpoint, and what the flat rendering walks. A nested tree cannot be
/// serialised safely: depth is whatever was collected, two JSON levels per node, against System.Text.Json's
/// limit of 32. Raising that limit moves the failure from an error to a stack overflow. A caller wanting the
/// tree back rebuilds it from <see cref="Path"/> and <see cref="Depth"/>, which are ordered depth first.
/// </remarks>
internal sealed record ProfileRow
{
    /// <summary>The names from the root down to this node, joined by <c>;</c>. Unique within a profile.</summary>
    public required string Path { get; init; }

    /// <summary>Zero for a root. Rows arrive depth first, so a parent always precedes its children.</summary>
    public required int Depth { get; init; }

    public required string Name { get; init; }

    public required int Count { get; init; }

    public required double TotalMs { get; init; }

    public required double SelfMs { get; init; }

    public required double MedianMs { get; init; }

    public required double P95Ms { get; init; }

    public required double MaxMs { get; init; }

    public required int ErrorCount { get; init; }

    /// <summary>How many distinct child subtree shapes were merged into this node.</summary>
    public required int DistinctChildShapes { get; init; }

    public required string SlowestSpanId { get; init; }
}
