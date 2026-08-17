namespace NekoTrace.Web.Analysis;

using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using System.Collections.Immutable;

/// <summary>
/// The aggregated call tree: siblings sharing a name are merged, and so are their subtrees, all the way down.
/// </summary>
/// <remarks>
/// This is the view that makes a large trace readable at all, because it grows with the number of distinct
/// call paths rather than with the number of spans. Over <c>TestTraces/</c>: 172 spans merge to 20 nodes,
/// 19,379 to 74, and 230,313 to 988. The last of those is a 217 MB file.
/// </remarks>
internal static class TraceProfile
{
    /// <summary>The separator joining names into a <see cref="ProfileRow.Path"/>.</summary>
    /// <remarks>
    /// <c>;</c> because that is what Brendan Gregg's collapsed-stack format uses, so the path column feeds a
    /// flamegraph directly. <c>/</c> would collide — it appears in every Orleans grain name.
    /// </remarks>
    private const char PATH_SEPARATOR = ';';

    public static ImmutableArray<ProfileNode> Build(SpanTree tree) => Build(tree.Roots);

    /// <param name="include">
    /// Applied to every span before it is merged. Callers hiding a subtree pass the same predicate here that
    /// they use elsewhere, so a name hidden from the tree does not reappear inside a collapsed group.
    /// </param>
    public static ImmutableArray<ProfileNode> Build(
        IEnumerable<SpanNode> roots,
        Func<SpanNode, bool>? include = null
    )
    {
        // Breadth first into a builder tree, then converted bottom up by walking that in reverse. Both halves
        // are iterative for the reason given on SpanTree: nesting depth is whatever was collected.
        var rootLevels = GroupByName(roots, include);
        var levels = new List<Level>(rootLevels);
        var queue = new Queue<Level>(rootLevels);

        while (queue.Count > 0)
        {
            var level = queue.Dequeue();

            foreach (var child in GroupByName(level.Members.SelectMany(member => member.Children), include))
            {
                level.Children.Add(child);
                levels.Add(child);
                queue.Enqueue(child);
            }
        }

        // Breadth first order puts every parent before its children, so reversed it puts every child before
        // its parent — exactly the order needed to build immutable nodes from the leaves up.
        var built = new Dictionary<Level, ProfileNode>();

        for (var index = levels.Count - 1; index >= 0; index--)
        {
            var level = levels[index];

            built.Add(level, level.ToNode([.. level.Children.Select(child => built[child])]));
        }

        return [.. rootLevels.Select(level => built[level]).OrderByDescending(node => node.Durations.TotalMs)];
    }

    /// <summary>
    /// Groups spans by name, keeping the earliest starting group first so that a profile of a trace whose
    /// names are all distinct still reads chronologically.
    /// </summary>
    private static List<Level> GroupByName(IEnumerable<SpanNode> nodes, Func<SpanNode, bool>? include)
    {
        var byName = new Dictionary<string, Level>(StringComparer.Ordinal);
        var ordered = new List<Level>();

        foreach (var node in nodes)
        {
            if (include is not null && !include(node))
            {
                continue;
            }

            if (!byName.TryGetValue(node.Span.Name, out var level))
            {
                byName[node.Span.Name] = level = new Level(node.Span.Name);
                ordered.Add(level);
            }

            level.Members.Add(node);
        }

        return ordered;
    }

    /// <summary>One node's worth of spans while the tree is being assembled. Compared by reference throughout.</summary>
    private sealed class Level
    {
        public Level(string name)
        {
            this.Name = name;
        }

        public string Name { get; }

        public List<SpanNode> Members { get; } = [];

        public List<Level> Children { get; } = [];

        public ProfileNode ToNode(ImmutableArray<ProfileNode> children)
        {
            var durations = new List<double>(this.Members.Count);
            var selfMs = 0d;
            var errorCount = 0;
            var slowest = this.Members[0];
            var childShapes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var member in this.Members)
            {
                durations.Add(member.DurationMs);
                selfMs += member.SelfTimeMs;

                if (member.HasError)
                {
                    errorCount++;
                }

                if (member.DurationMs > slowest.DurationMs)
                {
                    slowest = member;
                }

                // Sorted, so that two members which called the same things in a different order still count
                // as the same shape. Merging those is the point; merging genuinely different ones is what
                // DistinctChildShapes exists to admit to.
                childShapes.Add(
                    string.Join(
                        '\n',
                        member.Children.Select(child => child.Span.Name).Order(StringComparer.Ordinal)
                    )
                );
            }

            return new ProfileNode()
            {
                Name = this.Name,
                Durations = DurationStatistics.From(durations),
                SelfMs = selfMs,
                ErrorCount = errorCount,
                SlowestSpanId = slowest.Span.Id,
                DistinctChildShapes = childShapes.Count,
                Children = [.. children.OrderByDescending(child => child.Durations.TotalMs)],
            };
        }
    }

    /// <summary>
    /// The tree as a depth first list, each node carrying the path that got to it.
    /// </summary>
    /// <remarks>
    /// Everything here walks a tree with an explicit stack because trace depth is not ours to bound; this is
    /// the same rule applied to handing one over, since serialising a nested tree is bounded by the
    /// serialiser's depth limit. <see cref="Formatting.FlatFormatter.Profile"/> renders these same rows, so
    /// the flat output and the JSON model cannot disagree about what a profile contains.
    /// </remarks>
    public static ImmutableArray<ProfileRow> Flatten(ImmutableArray<ProfileNode> roots)
    {
        var rows = ImmutableArray.CreateBuilder<ProfileRow>();
        var stack = new Stack<(ProfileNode Node, int Depth, string Path)>();

        for (var index = roots.Length - 1; index >= 0; index--)
        {
            stack.Push((roots[index], 0, roots[index].Name));
        }

        while (stack.Count > 0)
        {
            var (node, depth, path) = stack.Pop();

            rows.Add(
                new ProfileRow()
                {
                    Path = path,
                    Depth = depth,
                    Name = node.Name,
                    Count = node.Count,
                    TotalMs = node.Durations.TotalMs,
                    SelfMs = node.SelfMs,
                    MedianMs = node.Durations.MedianMs,
                    P95Ms = node.Durations.P95Ms,
                    MaxMs = node.Durations.MaxMs,
                    ErrorCount = node.ErrorCount,
                    DistinctChildShapes = node.DistinctChildShapes,
                    SlowestSpanId = node.SlowestSpanId,
                }
            );

            for (var index = node.Children.Length - 1; index >= 0; index--)
            {
                stack.Push((node.Children[index], depth + 1, path + PATH_SEPARATOR + node.Children[index].Name));
            }
        }

        return rows.ToImmutable();
    }
}
