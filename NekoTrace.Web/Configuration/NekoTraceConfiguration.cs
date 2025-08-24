namespace NekoTrace.Web.Configuration;

public record NekoTraceConfiguration
{
    public TimeSpan? MaxSpanAge { get; set; }
}
