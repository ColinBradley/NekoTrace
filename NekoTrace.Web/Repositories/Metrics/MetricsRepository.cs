namespace NekoTrace.Web.Repositories.Metrics;

using Microsoft.Extensions.Configuration;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.Utilities;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using System;
using System.Collections.Immutable;

public sealed class MetricsRepository : IDisposable
{
    private readonly IConfiguration mConfiguration;

    private readonly BetterReaderWriterLock mResourcesLock = new();
    private readonly BetterReaderWriterLock mSumsLock = new();
    private readonly BetterReaderWriterLock mGaugesLock = new();
    private readonly BetterReaderWriterLock mHistogramsLock = new();

    private readonly Timer mTrimTimer;

    public event Action? Updated;

    public MetricsRepository(IConfiguration configuration)
    {
        mConfiguration = configuration;
        mTrimTimer = new Timer(this.mTrimTimer_Tick, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

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

                this.Updated?.Invoke();

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

                this.Updated?.Invoke();

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

                this.Updated?.Invoke();

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

                this.Updated?.Invoke();

                return histograms;
            }
        );
    }

    internal ExportMetricsServiceResponse ProcessMetrics(ExportMetricsServiceRequest request)
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

    private void mTrimTimer_Tick(object? _)
    {
        var nekoTraceConfig = NekoTraceConfiguration.Get(mConfiguration);

        var maxMetricAge = nekoTraceConfig.MaxMetricAge;
        if (maxMetricAge is null)
        {
            return;
        }

        var oldTimeUnixNanoSeconds = Convert.ToUInt64(DateTimeOffset.Now.Subtract(maxMetricAge.Value).ToUnixTimeMilliseconds()) * 1_000_000ul;

        using (mSumsLock.Write())
        {
            foreach (var sum in this.Sums)
            {
                sum.Remove(d => d.TimeUnixNano < oldTimeUnixNanoSeconds);
            }
        }

        using (mGaugesLock.Write())
        {
            foreach (var gauge in this.Gauges)
            {
                gauge.Remove(d => d.TimeUnixNano < oldTimeUnixNanoSeconds);
            }
        }
    }

    public void Dispose()
    {
        mTrimTimer.Dispose();
        mResourcesLock.Dispose();
        mSumsLock.Dispose();
        mGaugesLock.Dispose();
        mHistogramsLock.Dispose();
    }
}