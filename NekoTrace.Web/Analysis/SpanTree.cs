namespace NekoTrace.Web.Analysis;

using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

/// <summary>
/// The parent/child structure of a trace, which nothing in <c>Repositories/</c> holds — spans arrive flat and
/// keep only their parent's id.
/// </summary>
/// <remarks>
/// Everything here walks with an explicit stack rather than recursion. Depth is a property of whatever was
/// collected, not of anything NekoTrace controls, so a pathological trace must not be able to overflow the
/// stack of a request thread.
/// </remarks>
internal sealed class SpanTree
{
    private readonly ImmutableDictionary<string, SpanNode> mNodesById;

    private SpanTree(
        ImmutableDictionary<string, SpanNode> nodesById,
        ImmutableArray<SpanNode> roots,
        int orphanCount,
        int cycleCount,
        int maxDepth
    )
    {
        mNodesById = nodesById;
        this.Roots = roots;
        this.OrphanCount = orphanCount;
        this.CycleCount = cycleCount;
        this.MaxDepth = maxDepth;
    }

    /// <summary>Forest tops, chronological. More than one is normal — see <see cref="OrphanCount"/>.</summary>
    public ImmutableArray<SpanNode> Roots { get; }

    /// <summary>Spans that named a parent which never arrived.</summary>
    public int OrphanCount { get; }

    /// <summary>
    /// Spans that were only reachable from themselves. Always zero for data an SDK produced; non-zero means
    /// a hand-edited or corrupted file, and the spans in question were promoted to forest tops to keep the
    /// walk finite rather than dropped.
    /// </summary>
    public int CycleCount { get; }

    /// <summary>Deepest nesting reached, counting a forest top as zero.</summary>
    public int MaxDepth { get; }

    public int Count => mNodesById.Count;

    public SpanNode? TryGetNode(string spanId) =>
        mNodesById.TryGetValue(spanId, out var node) ? node : null;

    public static SpanTree Build(IEnumerable<SpanData> spans)
    {
        var nodesById = ImmutableDictionary.CreateBuilder<string, SpanNode>(StringComparer.Ordinal);
        foreach (var span in spans)
        {
            // TraceItem already refuses a span id it holds, but this also serves files and test fixtures.
            // First one wins, matching AddSpanCore.
            if (!nodesById.ContainsKey(span.Id))
            {
                nodesById.Add(span.Id, new SpanNode(span));
            }
        }

        var nodes = nodesById.ToImmutable();

        var roots = new List<SpanNode>();
        var childrenByParentId = new Dictionary<string, List<SpanNode>>(StringComparer.Ordinal);
        var orphanCount = 0;

        foreach (var node in nodes.Values)
        {
            var parentId = node.Span.ParentSpanId;

            if (string.IsNullOrEmpty(parentId))
            {
                roots.Add(node);
                continue;
            }

            if (!nodes.TryGetValue(parentId, out var parent))
            {
                node.IsOrphan = true;
                orphanCount++;
                roots.Add(node);
                continue;
            }

            node.Parent = parent;

            if (!childrenByParentId.TryGetValue(parentId, out var siblings))
            {
                childrenByParentId[parentId] = siblings = [];
            }

            siblings.Add(node);
        }

        foreach (var (parentId, siblings) in childrenByParentId)
        {
            siblings.Sort(CompareByStart);
            nodes[parentId].Children = [.. siblings];
        }

        roots.Sort(CompareByStart);

        var (maxDepth, cycleCount) = AssignDepths(roots, nodes);

        return new SpanTree(nodes, [.. roots], orphanCount, cycleCount, maxDepth);
    }

    /// <summary>
    /// Depth first from every forest top, chronological within each level. The order a tree view renders in.
    /// </summary>
    public IEnumerable<SpanNode> EnumerateDepthFirst() => EnumerateDepthFirst(this.Roots);

    /// <summary>Depth first from <paramref name="from"/> inclusive.</summary>
    public static IEnumerable<SpanNode> EnumerateDepthFirst(SpanNode from) => EnumerateDepthFirst([from]);

    private static IEnumerable<SpanNode> EnumerateDepthFirst(ImmutableArray<SpanNode> from)
    {
        var stack = new Stack<SpanNode>();

        for (var index = from.Length - 1; index >= 0; index--)
        {
            stack.Push(from[index]);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            yield return node;

            // Reversed so the earliest child comes off the stack first.
            for (var index = node.Children.Length - 1; index >= 0; index--)
            {
                stack.Push(node.Children[index]);
            }
        }
    }

    /// <summary>
    /// Walks from the forest tops setting <see cref="SpanNode.Depth"/> and <see cref="SpanNode.SelfTimeMs"/>,
    /// then promotes anything it could not reach. Unreachable nodes are exactly those in a parent cycle, which
    /// no SDK produces but a hand-edited file can, and which would otherwise make every later walk run forever.
    /// </summary>
    private static (int MaxDepth, int CycleCount) AssignDepths(
        List<SpanNode> roots,
        ImmutableDictionary<string, SpanNode> nodes
    )
    {
        var allNodes = nodes.Values.ToArray();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<SpanNode>();
        var maxDepth = 0;
        var cycleCount = 0;
        var nextRootIndex = 0;

        // Only ever moves forward: visited never shrinks, so a node this has already stepped over stays
        // visited, which keeps the search for stranded nodes linear however many cycles there turn out to be.
        var scanIndex = 0;

        while (true)
        {
            // Promoting a stranded node appends to roots, so this picks up where it left off each time round.
            for (; nextRootIndex < roots.Count; nextRootIndex++)
            {
                stack.Push(roots[nextRootIndex]);

                while (stack.Count > 0)
                {
                    var node = stack.Pop();

                    // Guards the traversal itself, not just the assignment below: a node in a cycle must
                    // never be expanded twice or this walk does not terminate.
                    if (!visited.Add(node.Span.Id))
                    {
                        continue;
                    }

                    node.Depth = node.Parent is null ? 0 : node.Parent.Depth + 1;
                    node.SelfTimeMs = ComputeSelfTime(node);

                    if (node.Depth > maxDepth)
                    {
                        maxDepth = node.Depth;
                    }

                    foreach (var child in node.Children)
                    {
                        stack.Push(child);
                    }
                }
            }

            while (scanIndex < allNodes.Length && visited.Contains(allNodes[scanIndex].Span.Id))
            {
                scanIndex++;
            }

            if (scanIndex == allNodes.Length)
            {
                return (maxDepth, cycleCount);
            }

            var stranded = allNodes[scanIndex];
            cycleCount++;

            // Severing the child link as well as the parent one is what actually breaks the cycle. Clearing
            // only Parent would leave the ring intact in the child lists, and every later walk would circle it.
            if (stranded.Parent is { } formerParent)
            {
                formerParent.Children = formerParent.Children.Remove(stranded);
                formerParent.SelfTimeMs = ComputeSelfTime(formerParent);
                stranded.Parent = null;
            }

            roots.Add(stranded);
        }
    }

    /// <summary>
    /// Duration minus the union of the child intervals, each clamped to the parent's own window. The clamp
    /// matters because clocks differ between services, so a child can legitimately be recorded as starting
    /// before its parent or ending after it, and an unclamped union then reports more covered time than the
    /// parent ever ran for.
    /// </summary>
    private static double ComputeSelfTime(SpanNode node)
    {
        var start = node.Span.StartTimeMs;
        var end = node.Span.EndTimeMs;
        var duration = end - start;

        if (node.Children.Length is 0 || duration <= 0)
        {
            return Math.Max(0, duration);
        }

        var covered = 0d;
        var cursor = start;

        // Children are chronological, so one pass with a high water mark is enough to union them.
        foreach (var child in node.Children)
        {
            var childStart = Math.Max(child.Span.StartTimeMs, start);
            var childEnd = Math.Min(child.Span.EndTimeMs, end);

            if (childEnd <= childStart || childEnd <= cursor)
            {
                continue;
            }

            covered += childEnd - Math.Max(childStart, cursor);
            cursor = childEnd;
        }

        return Math.Max(0, duration - covered);
    }

    private static int CompareByStart(SpanNode left, SpanNode right)
    {
        var byStart = left.Span.StartTimeMs.CompareTo(right.Span.StartTimeMs);

        // Ties are broken by id so that a tree renders the same way twice. Spans sharing a start time are
        // common: plenty of SDKs stamp them from a clock with millisecond resolution.
        return byStart is not 0
            ? byStart
            : string.CompareOrdinal(left.Span.Id, right.Span.Id);
    }
}
