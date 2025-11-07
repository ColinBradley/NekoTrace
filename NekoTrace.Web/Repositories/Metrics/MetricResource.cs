namespace NekoTrace.Web.Repositories.Metrics;

using System.Collections.Immutable;

public sealed class MetricResource
{
    public MetricResource()
    {
        this.Key = new(() => string.Join("; ", this.Attributes?.Select(p => $"{p.Key}: {p.Value}") ?? []));
    }

    public Lazy<string> Key { get; }

    public required ImmutableDictionary<string, string> Attributes { get; init; }
}