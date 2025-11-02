namespace NekoTrace.Web.Repositories.Metrics;

using System.Collections.Immutable;

public sealed class MetricResource
{
    public required ImmutableDictionary<string, string> Attributes { get; init; }
}