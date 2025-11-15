namespace NekoTrace.Web.Repositories.Metrics;

using OpenTelemetry.Proto.Metrics.V1;
using System.Collections.Immutable;

public sealed class MetricDatapoints : MetricItemBase
{
    private readonly Lock mLock = new();

    public ImmutableList<NumberDataPoint> DataPoints { get; private set; } = [];

    internal void Add(IEnumerable<NumberDataPoint> dataPoints)
    {
        lock (mLock)
        {
            this.DataPoints = this.DataPoints.AddRange(dataPoints);
        }

        this.RaiseUpdated();
    }
}