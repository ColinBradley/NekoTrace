namespace NekoTrace.Web.Repositories.Metrics;

public abstract class MetricItemBase
{
    public required MetricResource Resource { get; init; }

    public required string ScopeName { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }
}