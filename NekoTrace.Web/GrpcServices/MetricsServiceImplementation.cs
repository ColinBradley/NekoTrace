namespace NekoTrace.Web.GrpcServices;

using Grpc.Core;
using NekoTrace.Web.Repositories.Metrics;
using OpenTelemetry.Proto.Collector.Metrics.V1;

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
        return Task.FromResult(mMetrics.ProcessExportMetrics(request));
    }
}
