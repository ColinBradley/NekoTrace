namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using Xunit;

public sealed class SpanTreeTests
{
    [Fact]
    public void Build_NestsChildrenUnderTheirParent()
    {
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", name: "root", startMs: 0, durationMs: 100),
                Fake.Span(id: "b2", parentSpanId: "a1", name: "child", startMs: 10, durationMs: 20),
            ]
        );

        var root = Assert.Single(tree.Roots);
        var child = Assert.Single(root.Children);

        Assert.Equal("root", root.Span.Name);
        Assert.Equal("child", child.Span.Name);
        Assert.Equal(0, root.Depth);
        Assert.Equal(1, child.Depth);
    }

    [Fact]
    public void Build_OrdersChildrenByStartTime()
    {
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", name: "root"),
                Fake.Span(id: "late", parentSpanId: "a1", name: "late", startMs: 50),
                Fake.Span(id: "early", parentSpanId: "a1", name: "early", startMs: 10),
            ]
        );

        Assert.Equal(
            ["early", "late"],
            tree.Roots[0].Children.Select(child => child.Span.Name)
        );
    }

    [Fact]
    public void Build_TreatsASpanWhoseParentNeverArrivedAsAForestTop()
    {
        // The 230,313 span trace in TestTraces/ has 4,043 of these. Dropping them, or hanging the walk
        // looking for a parent that is not coming, loses a sixth of that trace.
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", name: "root"),
                Fake.Span(id: "orphan", parentSpanId: "missing", name: "orphan"),
            ]
        );

        Assert.Equal(2, tree.Roots.Length);
        Assert.Equal(1, tree.OrphanCount);
        Assert.True(tree.Roots.Single(root => root.Span.Name is "orphan").IsOrphan);
        Assert.False(tree.Roots.Single(root => root.Span.Name is "root").IsOrphan);
    }

    [Fact]
    public void Build_BreaksAParentCycleInsteadOfLoopingForever()
    {
        // No SDK produces one, but a hand edited file can, and every walk in here is unbounded — so a cycle
        // that survived construction would hang a request thread rather than return a bad answer.
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", parentSpanId: "b2", name: "first"),
                Fake.Span(id: "b2", parentSpanId: "a1", name: "second"),
            ]
        );

        Assert.Equal(1, tree.CycleCount);
        Assert.Equal(2, tree.EnumerateDepthFirst().Count());
    }

    [Fact]
    public void SelfTime_SubtractsTheUnionOfChildrenNotTheirSum()
    {
        // Two children covering 10-30ms and 20-40ms of a 0-100ms parent overlap for 10ms. Their durations
        // sum to 40ms, but they only account for 30ms of wall clock, so the parent's own time is 70ms.
        // Summing would say 60ms, and on a trace where children overlap heavily it goes negative.
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", name: "root", startMs: 0, durationMs: 100),
                Fake.Span(id: "b2", parentSpanId: "a1", startMs: 10, durationMs: 20),
                Fake.Span(id: "c3", parentSpanId: "a1", startMs: 20, durationMs: 20),
            ]
        );

        Assert.Equal(70, tree.Roots[0].SelfTimeMs, tolerance: 0.001);
    }

    [Fact]
    public void SelfTime_ClampsAChildThatOverrunsItsParent()
    {
        // Clocks differ between services, so a child can be recorded as ending after its parent did. Without
        // the clamp the union covers more time than the parent ever ran for and self time goes negative.
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", name: "root", startMs: 0, durationMs: 50),
                Fake.Span(id: "b2", parentSpanId: "a1", startMs: 10, durationMs: 500),
            ]
        );

        Assert.Equal(10, tree.Roots[0].SelfTimeMs, tolerance: 0.001);
    }

    [Fact]
    public void SelfTime_OfALeafIsItsWholeDuration()
    {
        var tree = SpanTree.Build([Fake.Span(id: "a1", durationMs: 42)]);

        Assert.Equal(42, tree.Roots[0].SelfTimeMs, tolerance: 0.001);
    }
}
