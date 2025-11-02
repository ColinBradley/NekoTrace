namespace NekoTrace.Web.GrpcServices;

using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

public class LogsServiceImplementation : LogsService.LogsServiceBase
{
    public override Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context
    )
    {
        return Task.FromResult(
            new ExportLogsServiceResponse()
            {
                PartialSuccess = new ExportLogsPartialSuccess()
                {
                    RejectedLogRecords = 0,
                    ErrorMessage = string.Empty,
                },
            }
        );
    }
}
