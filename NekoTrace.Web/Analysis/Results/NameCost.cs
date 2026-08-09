namespace NekoTrace.Web.Analysis.Results;

/// <summary>What every span sharing a name cost, wherever in the trace they were called from.</summary>
internal sealed record NameCost
{
    public required string Name { get; init; }

    public required DurationStatistics Durations { get; init; }

    /// <summary>
    /// The same spread over self time rather than total duration. What outliers are judged on: a span that
    /// merely <em>contains</em> a slow one is not itself slow, and in a recursive trace the outermost instance
    /// of a name always looks like a thousandfold outlier when measured on duration.
    /// </summary>
    public required DurationStatistics SelfDurations { get; init; }

    public required double SelfMs { get; init; }

    /// <summary>Self time against the trace's wall clock, so parallel work does not add up past 100%.</summary>
    public required double SelfPercent { get; init; }

    public required int ErrorCount { get; init; }

    public required string SlowestSpanId { get; init; }
}
