namespace NekoTrace.Tests.Repositories;

using NekoTrace.Tests.TestData;
using Xunit;

public sealed class SpanDataTests
{
    [Fact]
    public void Duration_IsTheGapBetweenStartAndEnd()
    {
        var span = Fake.Span(startMs: 10, durationMs: 250);

        Assert.Equal(TimeSpan.FromMilliseconds(250), span.Duration);
    }

    [Fact]
    public void Duration_FollowsTheTimesThroughAWithExpression()
    {
        // A record's copy constructor copies every field, so a cached duration held in a Lazy closing over
        // `this` came across still bound to the original and reported its length forever. Nothing in the
        // codebase currently rewrites a span's times — only its ids — but the trap was live.
        var original = Fake.Span(startMs: 0, durationMs: 10);

        var stretched = original with { EndTime = original.StartTime.AddMilliseconds(500) };

        Assert.Equal(TimeSpan.FromMilliseconds(10), original.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(500), stretched.Duration);
    }

    [Theory]
    [InlineData(0.4, "400µs")]
    [InlineData(15.55, "15.6ms")]
    [InlineData(2500, "2.5s")]
    public void DurationText_ScalesTheUnitToTheLength(double milliseconds, string expected)
    {
        var span = Fake.Span(startMs: 0) with
        {
            StartTime = Otlp.ORIGIN,
            EndTime = Otlp.ORIGIN.AddTicks((long)(milliseconds * TimeSpan.TicksPerMillisecond)),
        };

        Assert.Equal(expected, span.DurationText);
    }
}
