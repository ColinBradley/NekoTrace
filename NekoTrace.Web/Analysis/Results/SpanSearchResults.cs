namespace NekoTrace.Web.Analysis.Results;

using System.Collections.Immutable;

/// <summary>
/// The whole answer to a span search: how many matched, what all of them share, and the ones printed.
/// </summary>
/// <remarks>
/// The <c>format=json</c> model. <see cref="Total"/> and <see cref="Common"/> describe every match rather
/// than the page, so they answer "how many, and what do they all share" without listing anything.
/// <para>
/// <see cref="Common"/> repeats values that are also on the spans in <see cref="Matches"/>, rather than being
/// stripped from them the way the text and flat renderings strip them. Both halves stay directly addressable:
/// <c>.common["url.path"]</c> answers what the search asked, and <c>.matches[].span.attributes</c> stays a
/// complete span for anything else.
/// </para>
/// </remarks>
internal sealed record SpanSearchResults
{
    /// <summary>Every span the query matched, not just the ones in <see cref="Matches"/>.</summary>
    public required int Total { get; init; }

    /// <summary>
    /// Attribute keys carried by <em>every</em> match with the same value throughout, computed across all
    /// <see cref="Total"/> of them. Empty when they do not all agree, which is itself the finding.
    /// </summary>
    public required ImmutableSortedDictionary<string, object?> Common { get; init; }

    /// <summary>The matches that fit under the limit, whole.</summary>
    public required ImmutableArray<SpanSearchResult> Matches { get; init; }
}
