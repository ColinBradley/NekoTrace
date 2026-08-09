namespace NekoTrace.Web.Analysis.Queries;

/// <summary>Knobs for <see cref="TraceSummary.Build"/>. The defaults are what an unparameterised call gets.</summary>
internal sealed record TraceSummaryOptions
{
    public static readonly TraceSummaryOptions Default = new();

    /// <summary>How many error spans are rendered in full, spread across the error classes.</summary>
    public int ErrorLimit { get; init; } = 10;

    /// <summary>How many entries each of the ranked lists carries.</summary>
    public int TopCount { get; init; } = 10;

    /// <summary>
    /// Spans matching any of these pairs are not counted as errors. For the 404s that ASP.NET reports as
    /// failures — <c>http.response.status_code=404</c> — which are noise in most traces and the whole point
    /// in a few, so it is the caller's call rather than a built in rule.
    /// </summary>
    public AttributeMatcher ErrorExclusions { get; init; } = AttributeMatcher.Empty;

    /// <summary>Below this many samples a slow tail is one slow span, not a pattern worth reporting.</summary>
    public int OutlierMinimumSamples { get; init; } = 5;

    /// <summary>How far the slowest instance must sit above the median before the name is called an outlier.</summary>
    public double OutlierTailRatio { get; init; } = 4;

    /// <summary>Gaps shorter than this are scheduling jitter rather than something to explain.</summary>
    public double MinimumGapMs { get; init; } = 10;
}
