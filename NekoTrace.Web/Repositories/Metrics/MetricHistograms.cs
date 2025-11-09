namespace NekoTrace.Web.Repositories.Metrics;

using Microsoft.AspNetCore.Routing;
using OpenTelemetry.Proto.Metrics.V1;
using System.Collections.Immutable;

public sealed class MetricHistograms : MetricItemBase
{
    private readonly Lock mLock = new();

    public ImmutableDictionary<string, ImmutableDictionary<ulong, HistogramDataPoint>> Histograms { get; private set; } =
        ImmutableDictionary.Create<string, ImmutableDictionary<ulong, HistogramDataPoint>>();

    internal void Add(IEnumerable<HistogramDataPoint> histograms)
    {
        lock (mLock)
        {
            var histogramsBuilder = this.Histograms.ToBuilder();

            foreach (var newHistogram in histograms)
            {
                var key = string.Join(
                    ';',
                    newHistogram.Attributes
                        .OrderBy(a => a.Key, StringComparer.Ordinal)
                        .Select(a => $"{a.Key}:{a.Value.StringValue}")
                );

                histogramsBuilder[key] =
                    this.Histograms.TryGetValue(key, out var histogramsByStartTime)
                        ? histogramsByStartTime.SetItem(newHistogram.StartTimeUnixNano, newHistogram)
                        : ImmutableDictionary.CreateRange([KeyValuePair.Create(newHistogram.StartTimeUnixNano, newHistogram)]);
            }

            this.Histograms = histogramsBuilder.ToImmutable();
        }
    }
}