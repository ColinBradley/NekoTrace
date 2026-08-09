namespace NekoTrace.Web.Analysis.Formatting;

using System.Collections.Immutable;

/// <summary>
/// Which attribute keys reach the output, on top of the hoisting <see cref="AttributeSummary"/> already does.
/// </summary>
/// <remarks>
/// Hoisting only removes keys that are constant across the <em>whole</em> response, which leaves the ones that
/// vary a little and mean nothing. <c>otel.library.name</c> and <c>otel.library.version</c> take four distinct
/// values across the 19,379 span trace in <c>TestTraces/</c>, so neither hoists, and together they are the
/// single largest thing in a rendered tree of it — about a third of every line, on every line.
/// Excluding them by default is a rendering choice rather than compaction, so it is stated in the output and
/// reversible with <c>attributes=all</c>.
/// </remarks>
internal sealed record AttributeSelector
{
    private static readonly ImmutableArray<string> sDefaultExclusions =
    [
        "otel.library.",
        "telemetry.sdk.",
    ];

    public static readonly AttributeSelector Default = new();

    /// <summary>Keys starting with any of these are dropped. Ignored when <see cref="Included"/> is set.</summary>
    public ImmutableArray<string> Excluded { get; init; } = sDefaultExclusions;

    /// <summary>When set, only keys starting with one of these are kept.</summary>
    public ImmutableArray<string>? Included { get; init; }

    /// <summary>
    /// A comma separated list of key prefixes, each with an optional trailing <c>*</c> —
    /// <c>http.*,db.statement</c>. A bare <c>*</c> keeps everything, including the keys excluded by default.
    /// Empty or absent means those defaults.
    /// </summary>
    /// <remarks>
    /// <c>*</c> rather than the word <c>all</c>, because every other value here is matched against attribute
    /// keys and nothing stops a key from beginning "all" — a magic word in the same space as the data is a
    /// collision waiting to happen. There is no spelling for "none": switching attributes off entirely is
    /// <see cref="SpanRenderOptions.IncludeAttributes"/>, which is a question about how much of a span to
    /// render rather than about which keys are interesting.
    /// </remarks>
    public static AttributeSelector Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        if (value.Trim() is "*")
        {
            return new AttributeSelector() { Excluded = [] };
        }

        var included = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(prefix => prefix.TrimEnd('*'))
            .Where(prefix => prefix.Length > 0)
            .ToImmutableArray();

        return included.IsEmpty
            ? Default
            : new AttributeSelector() { Included = included, Excluded = [] };
    }

    public bool Includes(string key)
    {
        if (this.Included is { } included)
        {
            return included.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        return !this.Excluded.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What to tell the reader was left out, or null when nothing was.</summary>
    public string? Explain() =>
        this switch
        {
            { Included: { } included } =>
                "only attributes matching " + Patterns(included) + " shown (attributeFilter=* for the rest)",
            { Excluded.IsEmpty: false } =>
                "attributes matching " + Patterns(this.Excluded)
                    + " omitted (attributeFilter=* to include them)",
            _ => null,
        };

    private static string Patterns(ImmutableArray<string> prefixes) =>
        string.Join(", ", prefixes.Select(prefix => prefix + "*"));
}
