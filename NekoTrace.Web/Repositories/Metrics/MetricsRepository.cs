namespace NekoTrace.Web.Repositories.Metrics;

using NekoTrace.Web.Utilities;
using System;
using System.Collections.Immutable;

public sealed class MetricsRepository : IDisposable
{
    private readonly BetterReaderWriterLock mResourcesLock = new();
    private readonly BetterReaderWriterLock mSumsLock = new();
    private readonly BetterReaderWriterLock mGaugesLock = new();
    private readonly BetterReaderWriterLock mHistogramsLock = new();

    public ImmutableList<MetricResource> Resources { get; private set; } = [];

    public ImmutableList<MetricDatapoints> Sums { get; private set; } = [];

    public ImmutableList<MetricDatapoints> Gauges { get; private set; } = [];

    public ImmutableList<MetricHistograms> Histograms { get; private set; } = [];

    internal MetricResource GetResource(ImmutableDictionary<string, string> attributes)
    {
        return mResourcesLock.GetOrCreate(
            () =>
                this.Resources.FirstOrDefault(r => attributes.SequenceEqual(r.Attributes)),
            () =>
            {
                var resource = new MetricResource()
                {
                    Attributes = attributes,
                };

                this.Resources = this.Resources.Add(resource);

                return resource;
            }
        );
    }

    internal MetricDatapoints GetSum(MetricResource resource, string scopeName, string metricName, string metricDescription)
    {
        return mSumsLock.GetOrCreate(
            () =>
                this.Sums.FirstOrDefault(s =>
                    object.ReferenceEquals(s.Resource, resource)
                    && string.Equals(s.ScopeName, scopeName, StringComparison.Ordinal)
                    && string.Equals(s.Name, metricName, StringComparison.Ordinal)
                ),
            () =>
            {
                var sum = new MetricDatapoints()
                {
                    Resource = resource,
                    ScopeName = scopeName,
                    Name = metricName,
                    Description = metricDescription,
                };

                this.Sums = this.Sums.Add(sum);

                return sum;
            }
        );
    }

    internal MetricDatapoints GetGauge(MetricResource resource, string scopeName, string metricName, string metricDescription)
    {
        return mGaugesLock.GetOrCreate(
            () =>
                this.Gauges.FirstOrDefault(s =>
                    object.ReferenceEquals(s.Resource, resource)
                    && string.Equals(s.ScopeName, scopeName, StringComparison.Ordinal)
                    && string.Equals(s.Name, metricName, StringComparison.Ordinal)
                ),
            () =>
            {
                var gauge = new MetricDatapoints()
                {
                    Resource = resource,
                    ScopeName = scopeName,
                    Name = metricName,
                    Description = metricDescription,
                };

                this.Gauges = this.Gauges.Add(gauge);

                return gauge;
            }
        );
    }

    internal MetricHistograms GetHistograms(MetricResource resource, string scopeName, string metricName, string metricDescription)
    {
        return mHistogramsLock.GetOrCreate(
            () =>
                this.Histograms.FirstOrDefault(s =>
                    object.ReferenceEquals(s.Resource, resource)
                    && string.Equals(s.ScopeName, scopeName, StringComparison.Ordinal)
                    && string.Equals(s.Name, metricName, StringComparison.Ordinal)
                ),
            () =>
            {
                var histograms = new MetricHistograms()
                {
                    Resource = resource,
                    ScopeName = scopeName,
                    Name = metricName,
                    Description = metricDescription,
                };

                this.Histograms = this.Histograms.Add(histograms);

                return histograms;
            }
        );
    }

    public void Dispose()
    {
        mResourcesLock.Dispose();
        mSumsLock.Dispose();
        mGaugesLock.Dispose();
        mHistogramsLock.Dispose();
    }
}