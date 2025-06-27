using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace InfoCat.Web.GrpcServices;

public class LogsServiceImplementation : LogsService.LogsServiceBase
{
    public override Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context
    )
    {
        Console.WriteLine("log");

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
