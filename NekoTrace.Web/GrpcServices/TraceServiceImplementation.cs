using Grpc.Core;
using NekoTrace.Web.Repositories.Traces;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace NekoTrace.Web.GrpcServices;

public class TraceServiceImplementation : TraceService.TraceServiceBase
{
    private readonly TracesRepository mTraces;

    public TraceServiceImplementation(TracesRepository traces)
    {
        mTraces = traces;
    }

    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context
    )
    {
        return Task.FromResult(mTraces.ProcessTraces(request));
    }
}
