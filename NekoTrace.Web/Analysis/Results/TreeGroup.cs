namespace NekoTrace.Web.Analysis.Results;

/// <summary>
/// Siblings that shared a name, merged. Never to be rendered in a way that could pass for a single span:
/// a group is frequently the finding rather than noise, and a reader who mistakes one for a span will
/// misread both the count and the timings.
/// </summary>
internal sealed record TreeGroup : TreeNode
{
    /// <summary>The merged subtree, including the group's own totals and spread.</summary>
    public required ProfileNode Merged { get; init; }

    /// <summary>When the last member finished, so a group's span on the clock is still visible.</summary>
    public required double EndOffsetMs { get; init; }
}
