namespace NekoTrace.Web.Repositories.Metrics;

public abstract class MetricItemBase
{
    public event Action? Updated;

    public required MetricResource Resource { get; init; }

    public required string ScopeName { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    protected void RaiseUpdated()
    {
        this.Updated?.Invoke();
    }
}