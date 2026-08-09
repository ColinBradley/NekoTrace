namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using System.Collections.Immutable;
using Xunit;

public sealed class TreeViewTests
{
    [Fact]
    public void Build_MergesEnoughSiblingsSharingAName()
    {
        var result = TreeView.Build(SiblingTree(3), TreeViewOptions.Default);

        var group = Assert.IsType<TreeGroup>(Assert.Single(Assert.IsType<TreeSpan>(result.Roots[0]).Children));

        Assert.Equal(3, group.Merged.Count);
        Assert.Equal(1, result.CollapsedGroupCount);
        Assert.Equal(3, result.CollapsedSpanCount);
    }

    [Fact]
    public void Build_LeavesTooFewSiblingsAlone()
    {
        var result = TreeView.Build(SiblingTree(2), TreeViewOptions.Default);

        Assert.All(
            Assert.IsType<TreeSpan>(result.Roots[0]).Children,
            child => Assert.IsType<TreeSpan>(child)
        );
        Assert.Equal(0, result.CollapsedGroupCount);
    }

    [Fact]
    public void Build_RendersEverySpanWhenCollapsingIsOff()
    {
        var result = TreeView.Build(
            SiblingTree(5),
            TreeViewOptions.Default with { CollapseThreshold = 0 }
        );

        Assert.Equal(5, Assert.IsType<TreeSpan>(result.Roots[0]).Children.Length);
        Assert.Equal(0, result.CollapsedGroupCount);
    }

    [Fact]
    public void Build_ListsAnExpandedGroupMemberByMember()
    {
        var result = TreeView.Build(
            SiblingTree(5),
            TreeViewOptions.Default with { ExpandedNames = ["call"] }
        );

        Assert.Equal(5, Assert.IsType<TreeSpan>(result.Roots[0]).Children.Length);
    }

    [Fact]
    public void Build_DropsAHiddenSpanAlongWithItsDescendants()
    {
        var result = TreeView.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root", durationMs: 100),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "noisy", durationMs: 40),
                    Fake.Span(id: "c3", parentSpanId: "b2", name: "under noisy"),
                    Fake.Span(id: "d4", parentSpanId: "a1", name: "kept"),
                ]
            ),
            TreeViewOptions.Default with { HiddenSpanNames = ["noisy"] }
        );

        var children = Assert.IsType<TreeSpan>(result.Roots[0]).Children;

        Assert.Equal("kept", Assert.IsType<TreeSpan>(Assert.Single(children)).Node.Span.Name);
        Assert.Equal(2, result.HiddenSpanCount);

        // The subtree root's own duration, not the sum over the subtree: its child ran inside it, so adding
        // them would claim more time was removed than the parent contains.
        Assert.Equal(40, result.HiddenDurationMs, tolerance: 0.001);
    }

    [Fact]
    public void Build_StopsAtTheDepthLimitAndSaysWhatIsBelowIt()
    {
        var result = TreeView.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root"),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "child"),
                    Fake.Span(id: "c3", parentSpanId: "b2", name: "grandchild"),
                    Fake.Span(id: "d4", parentSpanId: "c3", name: "great grandchild"),
                ]
            ),
            TreeViewOptions.Default with { MaxDepth = 1 }
        );

        var child = Assert.IsType<TreeSpan>(Assert.Single(Assert.IsType<TreeSpan>(result.Roots[0]).Children));

        Assert.Empty(child.Children);
        Assert.Equal(2, child.UnrenderedDescendants);
        Assert.Equal(2, result.UnrenderedByDepth);
    }

    [Fact]
    public void Build_StartsAtTheRequestedSpan()
    {
        var result = TreeView.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root"),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "branch"),
                    Fake.Span(id: "c3", parentSpanId: "b2", name: "leaf"),
                    Fake.Span(id: "d4", parentSpanId: "a1", name: "other branch"),
                ]
            ),
            TreeViewOptions.Default with { RootSpanId = "b2" }
        );

        var root = Assert.IsType<TreeSpan>(Assert.Single(result.Roots));

        Assert.Equal("branch", root.Node.Span.Name);
        Assert.Equal("leaf", Assert.IsType<TreeSpan>(Assert.Single(root.Children)).Node.Span.Name);
    }

    private static SpanTree SiblingTree(int siblings)
    {
        var spans = ImmutableArray.CreateBuilder<NekoTrace.Web.Repositories.Traces.SpanData>();

        spans.Add(Fake.Span(id: "a1", name: "root", durationMs: 1000));

        for (var index = 0; index < siblings; index++)
        {
            spans.Add(
                Fake.Span(
                    id: "s" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    parentSpanId: "a1",
                    name: "call",
                    startMs: index * 10,
                    durationMs: 5
                )
            );
        }

        return SpanTree.Build(spans.ToImmutable());
    }
}
