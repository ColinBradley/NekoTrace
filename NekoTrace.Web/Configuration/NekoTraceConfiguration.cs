namespace NekoTrace.Web.Configuration;

public record NekoTraceConfiguration
{
    public TimeSpan? MaxSpanAge { get; set; }

    public int GrpcCollectionPort { get; set; } = 4317;

    public int HttpCollectionPort { get; set; } = 4318;

    public int WebApplicationPort { get; set; } = 8347;
}
