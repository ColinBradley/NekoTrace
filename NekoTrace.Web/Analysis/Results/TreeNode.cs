namespace NekoTrace.Web.Analysis.Results;

/// <summary>
/// An entry in a rendered trace tree. Either one literal span (<see cref="TreeSpan"/>) or a merged group of
/// them (<see cref="TreeGroup"/>) — a closed pair, so anything consuming a tree can switch on the two.
/// </summary>
internal abstract record TreeNode
{
    /// <summary>Milliseconds from the start of the trace. What the tree is ordered by.</summary>
    public required double OffsetMs { get; init; }
}
