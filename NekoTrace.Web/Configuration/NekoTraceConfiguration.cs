namespace NekoTrace.Web.Configuration;

public record NekoTraceConfiguration
{
    public TimeSpan? MaxSpanAge { get; set; }

    public int CollectionPort { get; set; } = 4317;

    public int WebApplicationPort { get; set; } = 8347;
}
