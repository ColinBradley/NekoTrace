namespace NekoTrace.Tests.Repositories;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Repositories.Traces;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class SpanRepositoryTests
{
    [Fact]
    public void Name_ComesFromTheFirstSpanAdded()
    {
        using var repository = new SpanRepository();

        Assert.Equal(string.Empty, repository.Name);

        repository.AddSpan(Fake.Span(name: "GET /things"));

        Assert.Equal("GET /things", repository.Name);
    }

    [Fact]
    public void ErrorSpans_HoldsOnlyTheFailures()
    {
        using var repository = new SpanRepository();

        repository.AddSpan(Fake.Span(id: "0000000000000001", status: StatusCode.Ok));
        repository.AddSpan(Fake.Span(id: "0000000000000002", status: StatusCode.Error));

        Assert.Equal(2, repository.Spans.Count);
        Assert.Equal("0000000000000002", Assert.Single(repository.ErrorSpans).Id);
    }

    [Fact]
    public void IsRootSpan_IsTrueWhenAnyRecordedSpanHasNoParent()
    {
        using var repository = new SpanRepository();

        repository.AddSpan(Fake.Span(id: "0000000000000001", parentSpanId: Otlp.ROOT_SPAN_ID));

        Assert.False(repository.IsRootSpan);

        repository.AddSpan(Fake.Span(id: "0000000000000002"));

        Assert.True(repository.IsRootSpan);
    }

    [Fact]
    public void MinAndMaxDuration_TrackTheExtremes()
    {
        using var repository = new SpanRepository();

        repository.AddSpan(Fake.Span(id: "0000000000000001", durationMs: 50));
        repository.AddSpan(Fake.Span(id: "0000000000000002", durationMs: 10));
        repository.AddSpan(Fake.Span(id: "0000000000000003", durationMs: 100));

        Assert.Equal(TimeSpan.FromMilliseconds(10), repository.MinDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), repository.MaxDuration);
    }

    [Fact]
    public void AverageDuration_IsTheMeanOfEverySpan()
    {
        using var repository = new SpanRepository();

        repository.AddSpan(Fake.Span(id: "0000000000000001", durationMs: 10));
        repository.AddSpan(Fake.Span(id: "0000000000000002", durationMs: 30));

        Assert.Equal(TimeSpan.FromMilliseconds(20), repository.AverageDuration);
    }

    [Fact]
    public void RemoveSpan_RecomputesTheExtremes()
    {
        using var repository = new SpanRepository();

        var slowest = Fake.Span(id: "0000000000000001", durationMs: 100);
        var fastest = Fake.Span(id: "0000000000000002", durationMs: 10);

        repository.AddSpan(slowest);
        repository.AddSpan(fastest);
        repository.AddSpan(Fake.Span(id: "0000000000000003", durationMs: 50));

        repository.RemoveSpan(slowest);
        repository.RemoveSpan(fastest);

        Assert.Equal(TimeSpan.FromMilliseconds(50), repository.MinDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(50), repository.MaxDuration);
    }

    [Fact]
    public void RemoveSpan_DropsTheSpanFromBothLists()
    {
        using var repository = new SpanRepository();

        var failure = Fake.Span(id: "0000000000000001", status: StatusCode.Error);

        repository.AddSpan(failure);
        repository.AddSpan(Fake.Span(id: "0000000000000002"));
        repository.RemoveSpan(failure);

        Assert.Single(repository.Spans);
        Assert.Empty(repository.ErrorSpans);
    }
}
