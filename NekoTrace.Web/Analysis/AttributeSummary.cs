namespace NekoTrace.Web.Analysis;

using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

/// <summary>
/// Splits a set of spans' attributes into the ones every span agrees on and the ones that actually vary.
/// </summary>
/// <remarks>
/// Ingest concatenates the resource and scope attributes onto every span (see <c>TracesRepository.ConvertSpan</c>),
/// so <c>service.name</c>, <c>host.name</c> and <c>telemetry.sdk.*</c> are repeated once per span. Measured over
/// the 19,379 span trace in <c>TestTraces/</c>, keys holding a single distinct value account for 37% of the
/// file. Hoisting them into one block printed once is lossless, and it adapts by itself: <c>service.name</c>
/// is common in a single-service trace and stays inline in a multi-service one.
/// </remarks>
internal sealed record AttributeSummary
{
    private static readonly AttributeSummary sEmpty = new()
    {
        Common = ImmutableSortedDictionary<string, object?>.Empty,
        SpanCount = 0,
    };

    /// <summary>Keys carried by every span in the set with the same value throughout, ordered by key.</summary>
    public required ImmutableSortedDictionary<string, object?> Common { get; init; }

    public required int SpanCount { get; init; }

    public static AttributeSummary Build(IReadOnlyCollection<SpanData> spans)
    {
        if (spans.Count is 0)
        {
            return sEmpty;
        }

        var candidates = new Dictionary<string, (object? Value, int Count, bool IsConstant)>(
            StringComparer.Ordinal
        );

        foreach (var span in spans)
        {
            foreach (var (key, value) in span.Attributes)
            {
                if (candidates.TryGetValue(key, out var candidate))
                {
                    candidates[key] = (
                        candidate.Value,
                        candidate.Count + 1,
                        candidate.IsConstant && RendersTheSame(candidate.Value, value)
                    );
                }
                else
                {
                    candidates[key] = (value, 1, true);
                }
            }
        }

        var common = ImmutableSortedDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);

        foreach (var (key, candidate) in candidates)
        {
            // Every span has to carry the key, not just every span that mentions it. A key present on half the
            // set with one value is not common: hoisting it would assert it of the other half as well.
            if (candidate.IsConstant && candidate.Count == spans.Count)
            {
                common.Add(key, candidate.Value);
            }
        }

        return new AttributeSummary() { Common = common.ToImmutable(), SpanCount = spans.Count };
    }

    /// <summary>The attributes of <paramref name="span"/> that this did not hoist, ordered by key.</summary>
    public IEnumerable<KeyValuePair<string, object?>> Varying(SpanData span) =>
        span.Attributes
            .Where(pair => !this.Common.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal);

    /// <summary>
    /// Whether two attribute values are interchangeable. Compared by type and rendered text rather than with
    /// <see cref="object.Equals(object?)"/>, because a value read from a trace file can be a
    /// <see cref="System.Text.Json.JsonElement"/> — see <c>AttributeValueJsonConverter</c> — and that has no
    /// value equality, so two identical OTLP ArrayValues out of the same file would otherwise never hoist.
    /// The type check keeps the long <c>1</c> and the string <c>"1"</c> apart, which matters because the JSON
    /// format emits the hoisted value typed.
    /// </summary>
    private static bool RendersTheSame(object? left, object? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            _ => left.GetType() == right.GetType()
                && string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal),
        };
}
