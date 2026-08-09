namespace NekoTrace.Web.Analysis.Results;

using System.Collections.Immutable;

/// <summary>One span, in its own right.</summary>
internal sealed record TreeSpan : TreeNode
{
    public required SpanNode Node { get; init; }

    public required ImmutableArray<TreeNode> Children { get; init; }

    /// <summary>Set when the children were dropped for being past the requested depth.</summary>
    public required int UnrenderedDescendants { get; init; }
}
