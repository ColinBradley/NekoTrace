namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class SpanQueryTests
{
    [Theory]
    [InlineData("GET*", "GET /orders", true)]
    [InlineData("GET*", "POST /orders", false)]
    [InlineData("*Grain*", "IPlaylistGrain/GetModel", true)]
    [InlineData("get*", "GET /orders", true)]
    public void Matches_TreatsNameAsAWildcardPattern(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, new SpanQuery() { Name = pattern }.Matches(Fake.Span(name: name)));
    }

    [Fact]
    public void Matches_BoundsTheSpanStartAtBothEnds()
    {
        var query = new SpanQuery()
        {
            StartedAfter = Otlp.ORIGIN.AddMilliseconds(100),
            StartedBefore = Otlp.ORIGIN.AddMilliseconds(200),
        };

        Assert.False(query.Matches(Fake.Span(startMs: 50)));
        Assert.True(query.Matches(Fake.Span(startMs: 150)));
        Assert.False(query.Matches(Fake.Span(startMs: 250)));
    }

    [Fact]
    public void Matches_CombinesEveryDimensionWithAnd()
    {
        var query = new SpanQuery()
        {
            Name = "GET*",
            HasError = true,
            DurationMinimum = 0.1,
        };

        Assert.True(query.Matches(Fake.Span(name: "GET /", durationMs: 200, status: StatusCode.Error)));
        Assert.False(query.Matches(Fake.Span(name: "GET /", durationMs: 200)));
        Assert.False(query.Matches(Fake.Span(name: "GET /", durationMs: 10, status: StatusCode.Error)));
        Assert.False(query.Matches(Fake.Span(name: "POST /", durationMs: 200, status: StatusCode.Error)));
    }

    [Theory]
    [InlineData("2026-08-09T14:00:00Z", 14)]
    // No offset means UTC, not the host's zone: reading the host clock would make the same request mean
    // different things on different machines.
    [InlineData("2026-08-09T14:00:00", 14)]
    [InlineData("2026-08-09T15:00:00+01:00", 14)]
    public void ParseTimestamp_ReadsAnOffsetlessTimeAsUtc(string value, int expectedUtcHour)
    {
        var parsed = SpanQuery.ParseTimestamp(value);

        Assert.NotNull(parsed);
        Assert.Equal(expectedUtcHour, parsed.Value.UtcDateTime.Hour);
    }

    [Fact]
    public void ParseTimestamp_ReturnsNullForSomethingUnreadable()
    {
        Assert.Null(SpanQuery.ParseTimestamp("last tuesday"));
    }
}
