namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Formatting;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using Xunit;

public sealed class AttributeSummaryTests
{
    [Fact]
    public void Build_HoistsAKeyEverySpanAgreesOn()
    {
        // Ingest copies the resource attributes onto every span, and over the 19,379 span trace in
        // TestTraces/ the keys that never vary are 37% of the file.
        var summary = AttributeSummary.Build(
            [
                Fake.Span(id: "a1", attributes: new() { ["service.name"] = "api", ["route"] = "/one" }),
                Fake.Span(id: "b2", attributes: new() { ["service.name"] = "api", ["route"] = "/two" }),
            ]
        );

        Assert.Equal(["service.name"], summary.Common.Keys);
    }

    [Fact]
    public void Build_LeavesAKeyOnlySomeSpansCarry()
    {
        // Hoisting it would assert it of the spans that never had it.
        var summary = AttributeSummary.Build(
            [
                Fake.Span(id: "a1", attributes: new() { ["db.system"] = "sqlite" }),
                Fake.Span(id: "b2", attributes: []),
            ]
        );

        Assert.Empty(summary.Common);
    }

    [Fact]
    public void Varying_ReturnsWhatWasNotHoisted()
    {
        var spans = new[]
        {
            Fake.Span(id: "a1", attributes: new() { ["service.name"] = "api", ["route"] = "/one" }),
            Fake.Span(id: "b2", attributes: new() { ["service.name"] = "api", ["route"] = "/two" }),
        };

        var summary = AttributeSummary.Build(spans);

        Assert.Equal(
            [new KeyValuePair<string, object?>("route", "/one")],
            summary.Varying(spans[0])
        );
    }

    [Theory]
    [InlineData(null, "otel.library.name", false)]
    [InlineData(null, "http.route", true)]
    [InlineData("*", "otel.library.name", true)]
    [InlineData("http.,db.", "http.route", true)]
    [InlineData("http.,db.", "url.full", false)]
    [InlineData("http.*", "http.route", true)]
    public void Selector_KeepsWhatTheCallerAskedFor(string? expression, string key, bool expected)
    {
        Assert.Equal(expected, AttributeSelector.Parse(expression).Includes(key));
    }

    [Fact]
    public void Selector_TreatsAWordLikeAllAsAnOrdinaryPrefix()
    {
        // "all" and "none" used to be magic here, in the same space as the attribute keys they are matched
        // against — and nothing stops a key from starting with either word. '*' cannot collide.
        var selector = AttributeSelector.Parse("all");

        Assert.True(selector.Includes("allocation.size"));
        Assert.False(selector.Includes("http.route"));
    }

    [Fact]
    public void Selector_SaysWhatItLeftOut()
    {
        // Excluding by default is a rendering choice rather than compaction, so it has to be visible and
        // reversible rather than something the reader has to notice.
        // attributeKeys, not attributeFilter: a filter decides which spans come back, and this decides which
        // of their fields get printed. The two shared a name once, on adjacent tools, with different syntaxes.
        Assert.Contains("attributeKeys=*", AttributeSelector.Default.Explain(), StringComparison.Ordinal);
    }
}
