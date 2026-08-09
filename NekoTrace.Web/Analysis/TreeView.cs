namespace NekoTrace.Web.Analysis;

using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using System.Collections.Immutable;

/// <summary>
/// The literal span tree, in start order, with repeated siblings merged so that the repetition costs a line
/// rather than thousands of them.
/// </summary>
/// <remarks>
/// Unlike <see cref="TraceProfile"/> this keeps chronology, which is what makes it the view to reach for when
/// the question is what happened in which order rather than where the time went.
/// </remarks>
internal static class TreeView
{
    public static TreeViewResult Build(SpanTree tree, TreeViewOptions options)
    {
        var starts = tree.Roots;

        if (options.RootSpanId is { } rootSpanId)
        {
            var node = tree.TryGetNode(rootSpanId);

            starts = node is null ? [] : [node];
        }

        var hidden = FindHidden(tree, options);
        var include = hidden.Ids.Count is 0
            ? (Func<SpanNode, bool>?)null
            : node => !hidden.Ids.Contains(node.Span.Id);

        var traceStartMs = tree.Roots.Length is 0 ? 0 : tree.Roots.Min(root => root.Span.StartTimeMs);

        var visible = starts.Where(start => !hidden.Ids.Contains(start.Span.Id)).ToArray();

        // Breadth first into builders, then converted bottom up by walking them in reverse — the same shape as
        // TraceProfile.Build, and iterative for the same reason.
        var rootBuilders = Arrange(visible, 0, options, include, traceStartMs);
        var builders = new List<Builder>(rootBuilders);
        var queue = new Queue<Builder>(rootBuilders);
        var collapsedGroups = 0;
        var collapsedSpans = 0;
        var truncated = 0;

        while (queue.Count > 0)
        {
            var builder = queue.Dequeue();

            if (builder.GroupMembers is { } members)
            {
                collapsedGroups++;
                collapsedSpans += members.Count;

                continue;
            }

            var span = builder.Span!;

            if (options.MaxDepth is { } maxDepth && builder.Depth >= maxDepth)
            {
                builder.UnrenderedDescendants = CountDescendants(span, hidden.Ids);

                truncated += builder.UnrenderedDescendants;

                continue;
            }

            foreach (var child in Arrange(span.Children, builder.Depth + 1, options, include, traceStartMs))
            {
                builder.Children.Add(child);
                builders.Add(child);
                queue.Enqueue(child);
            }
        }

        var built = new Dictionary<Builder, TreeNode>();

        for (var index = builders.Count - 1; index >= 0; index--)
        {
            var builder = builders[index];

            built.Add(builder, builder.ToNode([.. builder.Children.Select(child => built[child])]));
        }

        return new TreeViewResult()
        {
            Roots = [.. rootBuilders.Select(builder => built[builder])],
            HiddenSpanCount = hidden.Ids.Count,
            HiddenDurationMs = hidden.DurationMs,
            CollapsedGroupCount = collapsedGroups,
            CollapsedSpanCount = collapsedSpans,
            UnrenderedByDepth = truncated,
            TraceStartMs = traceStartMs,
        };
    }

    /// <summary>
    /// Turns one level of siblings into builders, merging the ones sharing a name once there are enough of
    /// them. Groups sit at their earliest member's start time, so the level still reads in order.
    /// </summary>
    private static List<Builder> Arrange(
        IEnumerable<SpanNode> siblings,
        int depth,
        TreeViewOptions options,
        Func<SpanNode, bool>? include,
        double traceStartMs
    )
    {
        var byName = new Dictionary<string, List<SpanNode>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var sibling in siblings)
        {
            if (include is not null && !include(sibling))
            {
                continue;
            }

            if (!byName.TryGetValue(sibling.Span.Name, out var members))
            {
                byName[sibling.Span.Name] = members = [];
                order.Add(sibling.Span.Name);
            }

            members.Add(sibling);
        }

        var builders = new List<Builder>();

        foreach (var name in order)
        {
            var members = byName[name];

            if (
                options.CollapseThreshold <= 0
                || members.Count < options.CollapseThreshold
                || options.ExpandedNames.Contains(name)
            )
            {
                builders.AddRange(
                    members.Select(member => new Builder()
                    {
                        Span = member,
                        Depth = depth,
                        OffsetMs = member.Span.StartTimeMs - traceStartMs,
                    })
                );

                continue;
            }

            builders.Add(
                new Builder()
                {
                    GroupMembers = members,
                    Depth = depth,
                    OffsetMs = members.Min(member => member.Span.StartTimeMs) - traceStartMs,
                    EndOffsetMs = members.Max(member => member.Span.EndTimeMs) - traceStartMs,
                    Merged = TraceProfile.Build(members, include)[0],
                }
            );
        }

        builders.Sort((left, right) => left.OffsetMs.CompareTo(right.OffsetMs));

        return builders;
    }

    /// <summary>
    /// Every span dropped by the hidden name and id sets, and what their subtrees were worth. Reported rather
    /// than quietly subtracted: output that shrank for a reason the reader did not ask about is a trap.
    /// </summary>
    private static (HashSet<string> Ids, double DurationMs) FindHidden(
        SpanTree tree,
        TreeViewOptions options
    )
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var durationMs = 0d;

        if (options.HiddenSpanNames.IsEmpty && options.HiddenSpanIds.IsEmpty)
        {
            return (ids, durationMs);
        }

        foreach (var node in tree.EnumerateDepthFirst())
        {
            if (
                ids.Contains(node.Span.Id)
                || (!options.HiddenSpanNames.Contains(node.Span.Name)
                    && !options.HiddenSpanIds.Contains(node.Span.Id))
            )
            {
                continue;
            }

            // The subtree root's own duration, not the sum over the subtree — its children ran inside it, so
            // adding them up would report more time removed than the trace contains.
            durationMs += node.DurationMs;

            foreach (var removed in SpanTree.EnumerateDepthFirst(node))
            {
                ids.Add(removed.Span.Id);
            }
        }

        return (ids, durationMs);
    }

    private static int CountDescendants(SpanNode node, HashSet<string> hidden)
    {
        var count = 0;

        foreach (var descendant in SpanTree.EnumerateDepthFirst(node))
        {
            if (!ReferenceEquals(descendant, node) && !hidden.Contains(descendant.Span.Id))
            {
                count++;
            }
        }

        return count;
    }

    private sealed class Builder
    {
        public SpanNode? Span { get; init; }

        public List<SpanNode>? GroupMembers { get; init; }

        public ProfileNode? Merged { get; init; }

        public required int Depth { get; init; }

        public required double OffsetMs { get; init; }

        public double EndOffsetMs { get; init; }

        public int UnrenderedDescendants { get; set; }

        public List<Builder> Children { get; } = [];

        public TreeNode ToNode(ImmutableArray<TreeNode> children) =>
            this.Merged is { } merged
                ? new TreeGroup()
                {
                    OffsetMs = this.OffsetMs,
                    EndOffsetMs = this.EndOffsetMs,
                    Merged = merged,
                }
                : new TreeSpan()
                {
                    OffsetMs = this.OffsetMs,
                    Node = this.Span!,
                    Children = children,
                    UnrenderedDescendants = this.UnrenderedDescendants,
                };
    }
}
