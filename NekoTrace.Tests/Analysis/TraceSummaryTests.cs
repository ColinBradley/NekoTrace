namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class TraceSummaryTests
{
    [Fact]
    public void Build_FoldsIdenticalErrorsIntoOneClass()
    {
        // A trace where one endpoint 404s repeatedly would otherwise spend the whole error budget saying so,
        // and ASP.NET reports plenty of ordinary 4xx responses as failures.
        var summary = Summarise(
            Fake.Span(id: "a1", name: "root"),
            NotFound("b2"),
            NotFound("c3"),
            NotFound("d4"),
            Fake.Span(id: "e5", parentSpanId: "a1", name: "work", status: StatusCode.Error)
        );

        Assert.Equal(4, summary.ErrorSpanCount);
        Assert.Equal(2, summary.ErrorClasses.Length);
        Assert.Equal(3, summary.ErrorClasses[0].Count);
        Assert.Equal("404", summary.ErrorClasses[0].HttpStatusCode);
    }

    [Fact]
    public void Build_SpreadsErrorSamplesAcrossTheClasses()
    {
        // Two slots must show two different problems, not two copies of the loudest one.
        var summary = Summarise(
            new TraceSummaryOptions() { ErrorLimit = 2 },
            Fake.Span(id: "a1", name: "root"),
            NotFound("b2"),
            NotFound("c3"),
            NotFound("d4"),
            Fake.Span(id: "e5", parentSpanId: "a1", name: "work", status: StatusCode.Error)
        );

        Assert.Equal(2, summary.ErrorSamples.Length);
        Assert.Equal(["GET", "work"], summary.ErrorSamples.Select(sample => sample.Name).Order());
    }

    [Fact]
    public void Build_ExcludesErrorsTheCallerRuledOut()
    {
        var summary = Summarise(
            new TraceSummaryOptions()
            {
                ErrorExclusions = AttributeMatcher.Parse("http.response.status_code=404"),
            },
            Fake.Span(id: "a1", name: "root"),
            NotFound("b2"),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "work", status: StatusCode.Error)
        );

        Assert.Equal(1, summary.ErrorSpanCount);
        Assert.Equal("work", Assert.Single(summary.ErrorClasses).SpanName);
    }

    [Fact]
    public void Build_ReadsAnExceptionRecordedAsAnEventRatherThanAnAttribute()
    {
        // The OpenTelemetry convention is an event named "exception"; the traces in TestTraces/ use span
        // attributes instead. Classifying on only one of the two misses every error raised by the other.
        var failed = Fake.Span(id: "b2", parentSpanId: "a1", name: "work", status: StatusCode.Error) with
        {
            Events =
            [
                new SpanEvent()
                {
                    Name = "exception",
                    Time = Otlp.ORIGIN,
                    Attributes = new()
                    {
                        ["exception.type"] = "System.TimeoutException",
                        ["exception.message"] = "It took too long",
                    },
                },
            ],
        };

        var summary = Summarise(Fake.Span(id: "a1", name: "root"), failed);

        var errorClass = Assert.Single(summary.ErrorClasses);

        Assert.Equal("System.TimeoutException", errorClass.ErrorType);
        Assert.Equal("It took too long", errorClass.Message);
    }

    [Fact]
    public void Build_CountsAnExceptionOnASpanThatIsNotMarkedFailed()
    {
        // Recorded and handled: invisible to every error count, and regularly what explains a slow trace
        // with nothing wrong in it.
        var handled = Fake.Span(id: "b2", parentSpanId: "a1", name: "work") with
        {
            Events =
            [
                new SpanEvent()
                {
                    Name = "exception",
                    Time = Otlp.ORIGIN,
                    Attributes = [],
                },
            ],
        };

        var summary = Summarise(Fake.Span(id: "a1", name: "root"), handled);

        Assert.Equal(0, summary.ErrorSpanCount);
        Assert.Equal(1, summary.HandledExceptionCount);
    }

    [Fact]
    public void Build_FindsANameThatCallsItself()
    {
        var summary = Summarise(
            Fake.Span(id: "a1", name: "recurse"),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "recurse"),
            Fake.Span(id: "c3", parentSpanId: "b2", name: "leaf")
        );

        Assert.Equal(["recurse"], summary.RecursiveNames);
    }

    [Fact]
    public void Build_ReportsTimeWhenNothingWasRunning()
    {
        var summary = Summarise(
            new TraceSummaryOptions() { MinimumGapMs = 10 },
            Fake.Span(id: "a1", name: "root", startMs: 0, durationMs: 10),
            Fake.Span(id: "b2", name: "later", startMs: 500, durationMs: 10)
        );

        var gap = Assert.Single(summary.Gaps);

        Assert.Equal(10, gap.StartMs, tolerance: 0.001);
        Assert.Equal(490, gap.DurationMs, tolerance: 0.001);
    }

    [Fact]
    public void Build_JudgesOutliersOnSelfTimeNotDuration()
    {
        // A span that merely contains a slow one is not itself slow. Measured on duration, the outermost
        // instance of a recursive name always looks like an enormous outlier — which on the 19,379 span
        // trace in TestTraces/ filled the whole outlier list with the four names forming its recursion.
        var spans = new List<SpanData>
        {
            Fake.Span(id: "a1", name: "root", startMs: 0, durationMs: 1000),
        };

        // Five "wrapper" spans, each almost entirely taken up by a child, so none has notable self time.
        for (var index = 0; index < 5; index++)
        {
            spans.Add(
                Fake.Span(
                    id: "w" + index,
                    parentSpanId: "a1",
                    name: "wrapper",
                    startMs: index * 100,
                    durationMs: index is 0 ? 90 : 10
                )
            );
            spans.Add(
                Fake.Span(
                    id: "c" + index,
                    parentSpanId: "w" + index,
                    name: "inner",
                    startMs: index * 100,
                    durationMs: index is 0 ? 89 : 9
                )
            );
        }

        var summary = Summarise([.. spans]);

        Assert.DoesNotContain(summary.Outliers, outlier => outlier.Name is "wrapper");
    }

    private static SpanData NotFound(string id) =>
        Fake.Span(
            id: id,
            parentSpanId: "a1",
            name: "GET",
            status: StatusCode.Error,
            attributes: new()
            {
                ["http.response.status_code"] = 404L,
                ["error.type"] = "404",
            }
        );

    private static TraceSummary Summarise(params SpanData[] spans) =>
        Summarise(TraceSummaryOptions.Default, spans);

    private static TraceSummary Summarise(TraceSummaryOptions options, params SpanData[] spans)
    {
        using var repository = Fake.TracesRepository();

        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpans(spans);

        return TraceSummary.Build(trace, SpanTree.Build(trace.Spans), options);
    }
}
