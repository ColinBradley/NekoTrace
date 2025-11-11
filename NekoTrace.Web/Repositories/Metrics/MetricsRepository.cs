namespace NekoTrace.Web.Repositories.Metrics;

using NekoTrace.Web.Utilities;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
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

    internal ExportMetricsServiceResponse ProcessExportMetrics(ExportMetricsServiceRequest request)
    {
        foreach (var resourceMetric in request.ResourceMetrics)
        {
            var resource = this.GetResource(
                resourceMetric.Resource.Attributes.Where(
                    p =>
                        p.Value.HasStringValue
                        && !p.Key.StartsWith(
                            "telemetry.sdk.",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToImmutableDictionary(p => p.Key, p => p.Value.StringValue)
            );

            foreach (var scopeMetrics in resourceMetric.ScopeMetrics)
            {
                foreach (var metric in scopeMetrics.Metrics)
                {
                    switch (metric.DataCase)
                    {
                        case Metric.DataOneofCase.None:
                            continue;
                        case Metric.DataOneofCase.Gauge:
                            var guage = this.GetGauge(resource, scopeMetrics.Scope.Name, metric.Name, metric.Description);
                            guage.Add(metric.Gauge.DataPoints);
                            break;
                        case Metric.DataOneofCase.Sum:
                            var sum = this.GetSum(resource, scopeMetrics.Scope.Name, metric.Name, metric.Description);
                            sum.Add(metric.Sum.DataPoints);
                            break;
                        case Metric.DataOneofCase.Histogram:
                            var histograms = this.GetHistograms(resource, scopeMetrics.Scope.Name, metric.Name, metric.Description);
                            histograms.Add(metric.Histogram.DataPoints);
                            break;
                        case Metric.DataOneofCase.ExponentialHistogram:
                            break;
                        case Metric.DataOneofCase.Summary:
                            break;
                    }
                }
            }
        }

        return new ExportMetricsServiceResponse()
        {
            PartialSuccess = new ExportMetricsPartialSuccess()
            {
                RejectedDataPoints = 0,
                ErrorMessage = string.Empty,
            },
        };
    }

    public void Dispose()
    {
        mResourcesLock.Dispose();
        mSumsLock.Dispose();
        mGaugesLock.Dispose();
        mHistogramsLock.Dispose();
    }
}