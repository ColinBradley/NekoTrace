namespace NekoTrace.Web.Analysis.Queries;

using System.Collections.Immutable;

/// <summary>Knobs for <see cref="TreeView.Build"/>.</summary>
internal sealed record TreeViewOptions
{
    public static readonly TreeViewOptions Default = new();

    /// <summary>
    /// How many siblings sharing a name it takes before they are merged into one group. Zero renders every
    /// span, which on the traces in <c>TestTraces/</c> means up to 230,313 lines.
    /// </summary>
    public int CollapseThreshold { get; init; } = 3;

    /// <summary>
    /// Span names to drop along with everything beneath them, matching <c>HiddenSpanNames</c> in the trace
    /// viewer's query string so a URL copied from the UI means the same thing here.
    /// </summary>
    public ImmutableHashSet<string> HiddenSpanNames { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>Span ids to drop along with everything beneath them. <c>HiddenSpanIds</c> in the viewer.</summary>
    public ImmutableHashSet<string> HiddenSpanIds { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>Names whose groups are listed member by member instead of being merged.</summary>
    public ImmutableHashSet<string> ExpandedNames { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>Levels below the starting point to render. Null renders all of them.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Where to start. Null starts at the trace's forest tops; a span id starts at that span.</summary>
    public string? RootSpanId { get; init; }
}
