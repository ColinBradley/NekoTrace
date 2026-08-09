namespace NekoTrace.Web.Analysis.Results;

/// <summary>Duration spread over a group of spans, in milliseconds.</summary>
internal readonly record struct DurationStatistics
{
    public required int Count { get; init; }

    public required double TotalMs { get; init; }

    public required double MinMs { get; init; }

    public required double MaxMs { get; init; }

    public required double MedianMs { get; init; }

    public required double P95Ms { get; init; }

    public required double P99Ms { get; init; }

    public double MeanMs => this.Count is 0 ? 0 : this.TotalMs / this.Count;

    /// <summary>
    /// How far the worst case sits above the typical one. The number that says whether a span name is
    /// uniformly slow or usually fine with a tail worth looking at.
    /// </summary>
    public double TailRatio => this.MedianMs > 0 ? this.MaxMs / this.MedianMs : 0;

    /// <summary>Sorts <paramref name="durations"/> in place.</summary>
    public static DurationStatistics From(List<double> durations)
    {
        if (durations.Count is 0)
        {
            return new DurationStatistics()
            {
                Count = 0,
                TotalMs = 0,
                MinMs = 0,
                MaxMs = 0,
                MedianMs = 0,
                P95Ms = 0,
                P99Ms = 0,
            };
        }

        durations.Sort();

        var total = 0d;
        foreach (var duration in durations)
        {
            total += duration;
        }

        return new DurationStatistics()
        {
            Count = durations.Count,
            TotalMs = total,
            MinMs = durations[0],
            MaxMs = durations[^1],
            MedianMs = Percentile(durations, 0.5),
            P95Ms = Percentile(durations, 0.95),
            P99Ms = Percentile(durations, 0.99),
        };
    }

    /// <summary>
    /// Nearest rank, so every value reported is one a span actually took rather than an interpolation between
    /// two of them. With the counts these traces carry — 2,394 spans sharing a name is ordinary — the
    /// difference from a linear interpolation is noise, and an observed value is easier to go and find.
    /// </summary>
    private static double Percentile(List<double> sortedDurations, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sortedDurations.Count) - 1;

        return sortedDurations[Math.Clamp(rank, 0, sortedDurations.Count - 1)];
    }
}
