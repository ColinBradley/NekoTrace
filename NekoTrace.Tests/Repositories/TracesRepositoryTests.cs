namespace NekoTrace.Tests.Repositories;

using NekoTrace.Tests.TestData;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class TracesRepositoryTests
{
    [Fact]
    public void GetOrAddTrace_ReturnsTheSameInstanceForTheSameId()
    {
        using var repository = Fake.TracesRepository();

        var first = repository.GetOrAddTrace(Otlp.TRACE_ID);
        var second = repository.GetOrAddTrace(Otlp.TRACE_ID);

        Assert.Same(first, second);
        Assert.Single(repository.Traces);
    }

    [Fact]
    public void TryGetTrace_ReturnsNullForAnUnknownId()
    {
        using var repository = Fake.TracesRepository();

        Assert.Null(repository.TryGetTrace(Otlp.TRACE_ID));
    }

    [Fact]
    public void ProcessTraces_StoresIdsAsLowercaseHex()
    {
        // 7091de6: ids used to be the base64 of the raw protobuf bytes, which nothing else in the ecosystem
        // uses and which can contain a '/' that ASP.NET routing rejects in a path segment.
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(Otlp.Span(spanId: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID))
        );

        var trace = Assert.Single(repository.Traces);

        Assert.Equal(Otlp.TRACE_ID, trace.Id);

        var span = Assert.Single(trace.Spans);

        Assert.Equal(Otlp.CHILD_SPAN_ID, span.Id);
        Assert.Equal(Otlp.TRACE_ID, span.TraceId);
        Assert.Equal(Otlp.ROOT_SPAN_ID, span.ParentSpanId);
    }

    [Fact]
    public void ProcessTraces_LeavesParentSpanIdNullForARootSpan()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(Otlp.Request(Otlp.Span(spanId: Otlp.ROOT_SPAN_ID)));

        var trace = Assert.Single(repository.Traces);

        Assert.Null(trace.Spans[0].ParentSpanId);
        Assert.NotNull(trace.RootSpan);
    }

    [Fact]
    public void ProcessTraces_ConvertsTimesAndStatus()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(Otlp.Span(startMs: 0, durationMs: 250, status: StatusCode.Error))
        );

        var span = Assert.Single(Assert.Single(repository.Traces).Spans);

        Assert.Equal(Otlp.ORIGIN, span.StartTime);
        Assert.Equal(Otlp.ORIGIN.AddMilliseconds(250), span.EndTime);
        Assert.Equal(TimeSpan.FromMilliseconds(250), span.Duration);
        Assert.Equal(StatusCode.Error, span.StatusCode);
        Assert.True(Assert.Single(repository.Traces).HasError);
    }

    [Fact]
    public void ProcessTraces_FlattensResourceAndScopeAttributesOntoEverySpan()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(
                resourceAttributes: [Otlp.Attribute("service.name", "checkout")],
                scopeAttributes: [Otlp.Attribute("scope.flavour", "vanilla")],
                Otlp.Span()
            )
        );

        var span = Assert.Single(Assert.Single(repository.Traces).Spans);

        Assert.Equal("checkout", span.Attributes["service.name"]);
        Assert.Equal("vanilla", span.Attributes["scope.flavour"]);
        Assert.Equal("test.scope", span.Attributes["otel.library.name"]);
        Assert.Equal("1.0.0", span.Attributes["otel.library.version"]);
    }

    [Fact]
    public void ProcessTraces_KeepsTypedAttributeValues()
    {
        using var repository = Fake.TracesRepository();

        var span = Otlp.Span();
        span.Attributes.Add(Otlp.Attribute("http.status_code", 200L));
        span.Attributes.Add(Otlp.Attribute("http.cached", value: true));
        span.Attributes.Add(Otlp.Attribute("http.route", "/things/{id}"));

        repository.ProcessTraces(Otlp.Request(span));

        var stored = Assert.Single(Assert.Single(repository.Traces).Spans);

        Assert.Equal(200L, stored.Attributes["http.status_code"]);
        Assert.True(stored.Attributes["http.cached"] is true);
        Assert.Equal("/things/{id}", stored.Attributes["http.route"]);
    }

    // 0bb9655: a span carrying the same attribute key twice used to throw out of ToDictionary, which failed
    // the whole export request rather than the one attribute.

    [Fact]
    public void ProcessTraces_KeepsTheFirstOfADuplicatedSpanAttributeKey()
    {
        using var repository = Fake.TracesRepository();

        var span = Otlp.Span();
        span.Attributes.Add(Otlp.Attribute("duplicated", "first"));
        span.Attributes.Add(Otlp.Attribute("duplicated", "second"));

        repository.ProcessTraces(Otlp.Request(span));

        var stored = Assert.Single(Assert.Single(repository.Traces).Spans);

        Assert.Equal("first", stored.Attributes["duplicated"]);
    }

    [Fact]
    public void ProcessTraces_LetsASpanAttributeWinOverAResourceAttributeOfTheSameKey()
    {
        using var repository = Fake.TracesRepository();

        var span = Otlp.Span();
        span.Attributes.Add(Otlp.Attribute("service.name", "from-span"));

        repository.ProcessTraces(
            Otlp.Request(
                resourceAttributes: [Otlp.Attribute("service.name", "from-resource")],
                scopeAttributes: null,
                span
            )
        );

        var stored = Assert.Single(Assert.Single(repository.Traces).Spans);

        Assert.Equal("from-span", stored.Attributes["service.name"]);
    }

    [Fact]
    public void ProcessTraces_KeepsTheFirstOfADuplicatedResourceAttributeKey()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(
                resourceAttributes:
                [
                    Otlp.Attribute("deployment", "first"),
                    Otlp.Attribute("deployment", "second"),
                ],
                scopeAttributes: null,
                Otlp.Span()
            )
        );

        var stored = Assert.Single(Assert.Single(repository.Traces).Spans);

        Assert.Equal("first", stored.Attributes["deployment"]);
    }

    [Fact]
    public void ProcessTraces_SplitsSpansAcrossTracesByTraceId()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(
                Otlp.Span(spanId: Otlp.ROOT_SPAN_ID, traceId: Otlp.TRACE_ID),
                Otlp.Span(spanId: Otlp.ROOT_SPAN_ID, traceId: Otlp.OTHER_TRACE_ID)
            )
        );

        Assert.Equal(2, repository.Traces.Count());
        Assert.Single(repository.TryGetTrace(Otlp.TRACE_ID)!.Spans);
        Assert.Single(repository.TryGetTrace(Otlp.OTHER_TRACE_ID)!.Spans);
    }

    [Fact]
    public void ProcessTraces_RaisesTracesChanged()
    {
        using var repository = Fake.TracesRepository();

        var changes = 0;
        repository.TracesChanged += () => changes++;

        repository.ProcessTraces(Otlp.Request(Otlp.Span()));

        Assert.True(changes > 0, "Expected TracesChanged to be raised at least once.");
    }

    [Fact]
    public void SpanRepositoriesByName_IndexesEverySpanByName()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(
                Otlp.Span(spanId: Otlp.ROOT_SPAN_ID, name: "GET /things"),
                Otlp.Span(spanId: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID, name: "SELECT things"),
                Otlp.Span(spanId: "00000000000000ff", parentSpanId: Otlp.ROOT_SPAN_ID, name: "SELECT things", traceId: Otlp.OTHER_TRACE_ID)
            )
        );

        Assert.Single(repository.SpanRepositoriesByName["GET /things"].Spans);
        Assert.Equal(2, repository.SpanRepositoriesByName["SELECT things"].Spans.Count);
    }

    [Fact]
    public void RemoveTrace_DropsTheTraceAndItsSpansFromBothIndexes()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(Otlp.Span(spanId: Otlp.ROOT_SPAN_ID, name: "GET /things"))
        );

        repository.RemoveTrace(repository.TryGetTrace(Otlp.TRACE_ID)!);

        Assert.Empty(repository.Traces);
        Assert.Null(repository.TryGetTrace(Otlp.TRACE_ID));
        // A SpanRepository that empties is dropped entirely rather than left behind as a stale name.
        Assert.False(repository.SpanRepositoriesByName.ContainsKey("GET /things"));
    }

    [Fact]
    public void RemoveTrace_KeepsASpanNameThatAnotherTraceStillUses()
    {
        using var repository = Fake.TracesRepository();

        repository.ProcessTraces(
            Otlp.Request(
                Otlp.Span(spanId: Otlp.ROOT_SPAN_ID, name: "GET /things", traceId: Otlp.TRACE_ID),
                Otlp.Span(spanId: Otlp.ROOT_SPAN_ID, name: "GET /things", traceId: Otlp.OTHER_TRACE_ID)
            )
        );

        repository.RemoveTrace(repository.TryGetTrace(Otlp.TRACE_ID)!);

        Assert.Single(repository.SpanRepositoriesByName["GET /things"].Spans);
    }

    [Fact]
    public void ProcessTraces_DiscardsTracesTheIngestFilterRejects()
    {
        using var repository = Fake.TracesRepository(
            ("NekoTrace:TraceIngestFilter", "IgnoredTraceNames=GET /health")
        );

        repository.ProcessTraces(Otlp.Request(Otlp.Span(name: "GET /health")));

        Assert.Empty(repository.Traces);
    }

    [Fact]
    public void ProcessTraces_KeepsTracesTheIngestFilterDoesNotReject()
    {
        using var repository = Fake.TracesRepository(
            ("NekoTrace:TraceIngestFilter", "IgnoredTraceNames=GET /health")
        );

        repository.ProcessTraces(Otlp.Request(Otlp.Span(name: "GET /things")));

        Assert.Single(repository.Traces);
    }

    [Fact]
    public void ProcessTraces_KeepsATraceThatHasNotShownItsRootSpanYet()
    {
        // An incomplete trace must survive ingest — its root span may still be in flight.
        using var repository = Fake.TracesRepository(
            ("NekoTrace:TraceIngestFilter", "ExclusiveTraceNames=GET /things")
        );

        repository.ProcessTraces(
            Otlp.Request(Otlp.Span(spanId: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID))
        );

        Assert.Single(repository.Traces);
    }

    [Fact]
    public void ProcessTraces_ReturnsAPartialSuccessRejectingNothing()
    {
        using var repository = Fake.TracesRepository();

        var response = repository.ProcessTraces(Otlp.Request(Otlp.Span()));

        Assert.Equal(0, response.PartialSuccess.RejectedSpans);
        Assert.Empty(response.PartialSuccess.ErrorMessage);
    }
}
