namespace NekoTrace.Tests.Repositories;

using NekoTrace.Tests.TestData;
using System.Globalization;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class TraceItemTests
{
    // 5cc5ce3: FindLastIndex returns the last span starting *before* the new one, so the new span belongs at
    // that index + 1. Inserting at the index itself put it before its predecessor, reversing a trace that
    // arrived in start order; and the -1 no-match case was sent to Add at the end when it belongs at 0.

    [Fact]
    public void Spans_AreOrderedByStartTime_WhenTheyArriveInOrder()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: "0000000000000001", startMs: 0));
        trace.AddSpan(Fake.Span(id: "0000000000000002", startMs: 10));
        trace.AddSpan(Fake.Span(id: "0000000000000003", startMs: 20));

        Assert.Equal(
            ["0000000000000001", "0000000000000002", "0000000000000003"],
            Fake.SpanIds(trace)
        );
    }

    [Fact]
    public void Spans_AreOrderedByStartTime_WhenTheyArriveReversed()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: "0000000000000003", startMs: 20));
        trace.AddSpan(Fake.Span(id: "0000000000000002", startMs: 10));
        trace.AddSpan(Fake.Span(id: "0000000000000001", startMs: 0));

        Assert.Equal(
            ["0000000000000001", "0000000000000002", "0000000000000003"],
            Fake.SpanIds(trace)
        );
    }

    [Fact]
    public void Spans_AreOrderedByStartTime_WhenTheEarliestArrivesLast()
    {
        // The no-match branch: FindLastIndex returns -1, and -1 + 1 puts the span at the front.
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: "0000000000000002", parentSpanId: "0000000000000001", startMs: 10));
        trace.AddSpan(Fake.Span(id: "0000000000000003", parentSpanId: "0000000000000001", startMs: 20));
        trace.AddSpan(Fake.Span(id: "0000000000000001", startMs: 0));

        Assert.Equal(
            ["0000000000000001", "0000000000000002", "0000000000000003"],
            Fake.SpanIds(trace)
        );
        Assert.Equal("0000000000000001", trace.RootSpan?.Id);
    }

    [Fact]
    public void Spans_AreOrderedByStartTime_WhenAddedAsOneBatch()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpans(
            [
                Fake.Span(id: "0000000000000003", startMs: 20),
                Fake.Span(id: "0000000000000001", startMs: 0),
                Fake.Span(id: "0000000000000004", startMs: 30),
                Fake.Span(id: "0000000000000002", startMs: 10),
            ]
        );

        Assert.Equal(
            ["0000000000000001", "0000000000000002", "0000000000000003", "0000000000000004"],
            Fake.SpanIds(trace)
        );
    }

    [Fact]
    public void Spans_KeepArrivalOrder_WhenStartTimesTie()
    {
        // Plenty of SDKs have millisecond clock resolution, so ties are common rather than exotic. A strict
        // < in the FindLastIndex predicate filed each tying span ahead of the ones already held, reversing
        // the group — the same reversal 5cc5ce3 fixed for the general case.
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: "0000000000000001", startMs: 5));
        trace.AddSpan(Fake.Span(id: "0000000000000002", startMs: 5));
        trace.AddSpan(Fake.Span(id: "0000000000000003", startMs: 5));

        Assert.Equal(
            ["0000000000000001", "0000000000000002", "0000000000000003"],
            Fake.SpanIds(trace)
        );
    }

    [Fact]
    public void Spans_AreNeverOutOfOrder_HoweverTheyArrive()
    {
        // The documented invariant itself, over an arrival order that includes spans starting at the same
        // moment.
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        long[] arrivalOrder = [30, 0, 10, 10, 20, 5, 30, 0];

        for (var index = 0; index < arrivalOrder.Length; index++)
        {
            trace.AddSpan(
                Fake.Span(
                    id: index.ToString("x16", CultureInfo.InvariantCulture),
                    startMs: arrivalOrder[index]
                )
            );
        }

        var startTimes = trace.Spans.Select(span => span.StartTime).ToArray();

        Assert.Equal(arrivalOrder.Length, startTimes.Length);
        Assert.Equal(startTimes.Order().ToArray(), startTimes);
    }

    // 54ea84d: AddSpanCore appended to the ordered list unconditionally while keying SpansById, so a repeated
    // span id left the two indexes disagreeing — the dictionary collapsed the repeat, the list kept both.

    [Fact]
    public void AddSpan_IgnoresASpanIdItAlreadyHolds()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "original"));
        trace.AddSpan(Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "retry"));

        Assert.Single(trace.Spans);
        Assert.Equal(trace.Spans.Count, trace.SpansById.Count);
        // A span is immutable once exported, so the copy already held wins.
        Assert.Equal("original", trace.Spans[0].Name);
    }

    [Fact]
    public void AddSpan_DoesNotDoubleCountARepeatInTheSpanNameIndex()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things"));
        trace.AddSpan(Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things"));

        Assert.Single(repository.SpanRepositoriesByName["GET /things"].Spans);
    }

    [Fact]
    public void AddSpans_IgnoresRepeatsWithinASingleBatch()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpans(
            [
                Fake.Span(id: Otlp.ROOT_SPAN_ID),
                Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID),
                Fake.Span(id: Otlp.ROOT_SPAN_ID),
            ]
        );

        Assert.Equal(2, trace.Spans.Count);
        Assert.Equal(trace.Spans.Count, trace.SpansById.Count);
    }

    [Fact]
    public void Start_End_And_Duration_SpanTheWholeTrace()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        // Deliberately not the outermost span first, and one that ends inside the previous one.
        trace.AddSpan(Fake.Span(id: "0000000000000002", startMs: 20, durationMs: 5));
        trace.AddSpan(Fake.Span(id: "0000000000000001", startMs: 0, durationMs: 100));
        trace.AddSpan(Fake.Span(id: "0000000000000003", startMs: 30, durationMs: 10));

        Assert.Equal(Otlp.ORIGIN, trace.Start);
        Assert.Equal(Otlp.ORIGIN.AddMilliseconds(100), trace.End);
        Assert.Equal(TimeSpan.FromMilliseconds(100), trace.Duration);
    }

    [Fact]
    public void HasError_IsFalse_WhenEverySpanSucceeded()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: "0000000000000001", status: StatusCode.Ok));
        trace.AddSpan(Fake.Span(id: "0000000000000002", status: StatusCode.Unset));

        Assert.False(trace.HasError);
    }

    [Fact]
    public void HasError_LatchesOnceAnErrorSpanArrives()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: "0000000000000001", status: StatusCode.Error));
        trace.AddSpan(Fake.Span(id: "0000000000000002", status: StatusCode.Ok));

        Assert.True(trace.HasError);
    }

    [Fact]
    public void RootSpan_IsNull_UntilAParentlessSpanArrives()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID));

        Assert.Null(trace.RootSpan);
    }

    [Fact]
    public void TryGetRootSpanAttribute_ReadsThroughToTheRootSpan()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(
            Fake.Span(
                id: Otlp.ROOT_SPAN_ID,
                attributes: new()
                {
                    ["service.name"] = "checkout",
                    ["http.status_code"] = 200,
                    ["retry"] = true,
                }
            )
        );

        Assert.Equal("checkout", trace.TryGetRootSpanAttribute("service.name"));
        Assert.Equal("200", trace.TryGetRootSpanAttribute("http.status_code"));
        Assert.Equal("True", trace.TryGetRootSpanAttribute("retry"));
        Assert.Null(trace.TryGetRootSpanAttribute("absent"));
    }

    [Fact]
    public void TryGetRootSpanAttribute_RendersAnIntegerAttribute()
    {
        // OTLP's IntValue is a long, so every integer attribute arrives boxed as one — and the type switch
        // used to test for int, which a boxed long never matches. Integer columns were always blank.
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(
                resourceAttributes: [Otlp.Attribute("http.status_code", 200L)],
                scopeAttributes: null,
                Otlp.Span()
            )
        );

        var trace = Assert.Single(repository.Traces);

        Assert.Equal(200L, trace.RootSpan?.Attributes["http.status_code"]);
        Assert.Equal("200", trace.TryGetRootSpanAttribute("http.status_code"));
    }

    [Fact]
    public void TryGetRootSpanAttribute_RendersAValueOfAnUnexpectedType()
    {
        // Null means "absent". Anything present renders as something, rather than blanking the column.
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(attributes: new() { ["odd"] = new Uri("https://example.test/x") }));

        Assert.Equal("https://example.test/x", trace.TryGetRootSpanAttribute("odd"));
    }

    [Fact]
    public void TryGetRootSpanAttribute_IsNullForAnAttributeExplicitlySetToNull()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(attributes: new() { ["empty"] = null }));

        Assert.Null(trace.TryGetRootSpanAttribute("empty"));
    }

    [Fact]
    public void TryGetRootSpanAttribute_IsNull_WhenThereIsNoRootSpanYet()
    {
        using var repository = Fake.TracesRepository();
        var trace = repository.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpan(Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID));

        Assert.Null(trace.TryGetRootSpanAttribute("service.name"));
    }
}
