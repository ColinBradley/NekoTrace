namespace NekoTrace.Web.Analysis.Results;

using System.Collections.Immutable;

/// <summary>A rendered tree, with an account of everything left out of it.</summary>
/// <remarks>
/// The counts are not bookkeeping. Output that shrank for a reason the reader did not ask about is a trap,
/// so every one of them is reported along with the parameter that would bring the spans back.
/// </remarks>
internal sealed record TreeViewResult
{
    public required ImmutableArray<TreeNode> Roots { get; init; }

    public required int HiddenSpanCount { get; init; }

    public required double HiddenDurationMs { get; init; }

    public required int CollapsedGroupCount { get; init; }

    public required int CollapsedSpanCount { get; init; }

    public required int UnrenderedByDepth { get; init; }

    /// <summary>Absolute milliseconds that every offset in the tree is measured from.</summary>
    public required double TraceStartMs { get; init; }
}
