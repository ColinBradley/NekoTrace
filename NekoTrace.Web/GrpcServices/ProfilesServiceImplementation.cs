using Grpc.Core;
using OpenTelemetry.Proto.Collector.Profiles.V1Development;

namespace NekoTrace.Web.GrpcServices;

public class ProfilesServiceImplementation : ProfilesService.ProfilesServiceBase
{
    public override Task<ExportProfilesServiceResponse> Export(ExportProfilesServiceRequest request, ServerCallContext context)
    {
        return Task.FromResult(
            new ExportProfilesServiceResponse()
            {
                PartialSuccess = new ExportProfilesPartialSuccess()
                {
                    RejectedProfiles = 0,
                    ErrorMessage = string.Empty,
                },
            }
        );
    }
}