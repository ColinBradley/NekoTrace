namespace NekoTrace.Web.Repositories.Metrics;

using OpenTelemetry.Proto.Metrics.V1;
using System.Collections.Immutable;

public sealed class MetricHistograms : MetricItemBase
{
    private readonly Lock mLock = new();

    public ImmutableList<HistogramDataPoint> Histograms { get; private set; } = [];

    internal void Add(IEnumerable<HistogramDataPoint> histograms)
    {
        lock (mLock)
        {
            this.Histograms = this.Histograms.AddRange(histograms);
        }
    }
}