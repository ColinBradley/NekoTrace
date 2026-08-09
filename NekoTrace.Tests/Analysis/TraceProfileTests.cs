namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class TraceProfileTests
{
    [Fact]
    public void Build_MergesSiblingsSharingAName()
    {
        var profile = TraceProfile.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root", startMs: 0, durationMs: 100),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "call", startMs: 0, durationMs: 10),
                    Fake.Span(id: "c3", parentSpanId: "a1", name: "call", startMs: 20, durationMs: 30),
                ]
            )
        );

        var merged = Assert.Single(profile[0].Children);

        Assert.Equal("call", merged.Name);
        Assert.Equal(2, merged.Count);
        Assert.Equal(40, merged.Durations.TotalMs, tolerance: 0.001);
        Assert.Equal(30, merged.Durations.MaxMs, tolerance: 0.001);
        Assert.Equal("c3", merged.SlowestSpanId);
    }

    [Fact]
    public void Build_MergesTheSubtreesOfMergedSiblingsToo()
    {
        // Merging only one level would leave the profile the same size as the trace one step down, which is
        // the whole difficulty: it has to collapse all the way to the leaves to stay small.
        var profile = TraceProfile.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root"),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "call"),
                    Fake.Span(id: "c3", parentSpanId: "a1", name: "call"),
                    Fake.Span(id: "d4", parentSpanId: "b2", name: "query"),
                    Fake.Span(id: "e5", parentSpanId: "c3", name: "query"),
                ]
            )
        );

        var query = Assert.Single(Assert.Single(profile[0].Children).Children);

        Assert.Equal("query", query.Name);
        Assert.Equal(2, query.Count);
    }

    [Fact]
    public void Build_ReportsWhenMergedMembersHadDifferentChildren()
    {
        var profile = TraceProfile.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root"),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "call"),
                    Fake.Span(id: "c3", parentSpanId: "a1", name: "call"),
                    Fake.Span(id: "d4", parentSpanId: "b2", name: "query"),
                    Fake.Span(id: "e5", parentSpanId: "c3", name: "cache"),
                ]
            )
        );

        Assert.Equal(2, Assert.Single(profile[0].Children).DistinctChildShapes);
    }

    [Fact]
    public void Build_CountsErrorsAcrossTheMergedSpans()
    {
        var profile = TraceProfile.Build(
            SpanTree.Build(
                [
                    Fake.Span(id: "a1", name: "root"),
                    Fake.Span(id: "b2", parentSpanId: "a1", name: "call", status: StatusCode.Error),
                    Fake.Span(id: "c3", parentSpanId: "a1", name: "call"),
                ]
            )
        );

        Assert.Equal(1, Assert.Single(profile[0].Children).ErrorCount);
    }

    [Fact]
    public void Build_SkipsSpansTheIncludePredicateRejects()
    {
        // The tree view hides a span and everything under it. Without this the hidden subtree reappears
        // inside any collapsed group covering the same spans.
        var tree = SpanTree.Build(
            [
                Fake.Span(id: "a1", name: "root"),
                Fake.Span(id: "b2", parentSpanId: "a1", name: "keep"),
                Fake.Span(id: "c3", parentSpanId: "a1", name: "drop"),
            ]
        );

        var profile = TraceProfile.Build(
            tree.Roots,
            node => !string.Equals(node.Span.Name, "drop", StringComparison.Ordinal)
        );

        Assert.Equal("keep", Assert.Single(profile[0].Children).Name);
    }
}
