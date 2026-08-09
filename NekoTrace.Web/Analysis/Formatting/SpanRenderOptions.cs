namespace NekoTrace.Web.Analysis.Formatting;

/// <summary>How much of each span to render.</summary>
/// <remarks>
/// Three independent switches rather than one <c>minimal|standard|full</c> level. A level bundles unrelated
/// decisions — whether ids are shortened has nothing to do with whether events are shown — so a caller who
/// wants one of them has to accept the rest, and no single word describes what it is going to get.
/// </remarks>
internal sealed record SpanRenderOptions
{
    public static readonly SpanRenderOptions Default = new();

    /// <summary>The attributes <see cref="AttributeSelector"/> keeps. Off drops them regardless of it.</summary>
    public bool IncludeAttributes { get; init; } = true;

    /// <summary>Span events and their attributes. Off by default: most spans have none, and a stack is long.</summary>
    public bool IncludeEvents { get; init; }

    /// <summary>
    /// Print each span id as the shortest prefix unique within the response. Off prints whole ids, which is
    /// what a caller feeding them to something other than this API wants.
    /// </summary>
    public bool ShortenSpanIds { get; init; } = true;
}
