namespace NekoTrace.Web.Analysis.Queries;

using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

/// <summary>
/// A set of <c>key=value</c> attribute pairs matched against a span, in the syntax
/// <c>TraceFilter.SpanAttributeFilter</c> already uses — <c>key=value;key=value</c>, compared case
/// insensitively, matching when <em>any</em> pair matches.
/// </summary>
/// <remarks>
/// Split out so the analysis endpoints don't invent a second spelling of the thing the UI's address bar and
/// the two config filters already use. See <c>docs/filtering.md</c>.
/// </remarks>
internal sealed record AttributeMatcher
{
    public static readonly AttributeMatcher Empty = new()
    {
        Pairs = ImmutableDictionary<string, string>.Empty,
    };

    public required ImmutableDictionary<string, string> Pairs { get; init; }

    public bool IsEmpty => this.Pairs.IsEmpty;

    public static AttributeMatcher Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty;
        }

        var pairs = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length is 2)
            .Select(parts => new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()))
            .Where(pair => !string.IsNullOrEmpty(pair.Key))
            .DistinctBy(pair => pair.Key, StringComparer.Ordinal);

        return new AttributeMatcher() { Pairs = pairs.ToImmutableDictionary(StringComparer.Ordinal) };
    }

    public bool Matches(SpanData span) =>
        !this.IsEmpty
        && this.Pairs.Any(pair =>
            span.Attributes.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value?.ToString(), StringComparison.OrdinalIgnoreCase)
        );
}
