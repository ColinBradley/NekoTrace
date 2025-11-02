namespace NekoTrace.Web.GrpcServices;

using Grpc.Core;
using NekoTrace.Web.Repositories.Metrics;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using System.Collections.Immutable;

public class MetricsServiceImplementation : MetricsService.MetricsServiceBase
{
    private readonly MetricsRepository mMetrics;

    public MetricsServiceImplementation(
        MetricsRepository metrics
    )
    {
        mMetrics = metrics;
    }

    public override Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context
    )
    {
        foreach (var resourceMetric in request.ResourceMetrics)
        {
            var resource = mMetrics.GetResource(
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
                            var guage = mMetrics.GetGauge(resource, scopeMetrics.Scope.Name, metric.Name, metric.Description);
                            guage.Add(metric.Gauge.DataPoints);
                            break;
                        case Metric.DataOneofCase.Sum:
                            var sum = mMetrics.GetSum(resource, scopeMetrics.Scope.Name, metric.Name, metric.Description);
                            sum.Add(metric.Sum.DataPoints);
                            break;
                        case Metric.DataOneofCase.Histogram:
                            var histograms = mMetrics.GetHistograms(resource, scopeMetrics.Scope.Name, metric.Name, metric.Description);
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

        return Task.FromResult(
            new ExportMetricsServiceResponse()
            {
                PartialSuccess = new ExportMetricsPartialSuccess()
                {
                    RejectedDataPoints = 0,
                    ErrorMessage = string.Empty,
                },
            }
        );
    }
}
