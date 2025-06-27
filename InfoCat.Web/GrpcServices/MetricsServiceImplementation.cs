using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace InfoCat.Web.GrpcServices;

public class MetricsServiceImplementation : MetricsService.MetricsServiceBase
{
    public override Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context
    )
    {
        Console.WriteLine("Metric");

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
