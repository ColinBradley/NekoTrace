namespace NekoTrace.Web.Configuration;

public record NekoTraceConfiguration
{
    internal const string CONFIGIRATION_SECTION_PATH = "NekoTrace";

    public TimeSpan? MaxMetricAge { get; set; }

    public TimeSpan? MaxSpanAge { get; set; }

    public int GrpcCollectionPort { get; set; } = 4317;

    public int HttpCollectionPort { get; set; } = 4318;

    public int WebApplicationPort { get; set; } = 8347;

    internal static NekoTraceConfiguration Get(IConfiguration config) =>
        config.GetSection(CONFIGIRATION_SECTION_PATH).Get<NekoTraceConfiguration>()
            ?? new NekoTraceConfiguration();
}
