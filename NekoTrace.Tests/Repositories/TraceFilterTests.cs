namespace NekoTrace.Tests.Repositories;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Repositories.Traces;
using System.Globalization;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class TraceFilterParseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ReturnsEmptyForNothing(string? queryString)
    {
        var filter = TraceFilter.Parse(queryString);

        Assert.True(filter.IsEmpty);
        Assert.Same(TraceFilter.Empty, filter);
    }

    [Fact]
    public void Parse_AcceptsALeadingQuestionMark()
    {
        // A URL copied out of the UI's address bar keeps the '?'; the config values don't have one.
        Assert.Equal(3, TraceFilter.Parse("?SpansMinimum=3").SpansMinimum);
    }

    [Fact]
    public void Parse_ReadsATimeWithNoOffsetAsUtc()
    {
        // Not as the host's local time. None of Parse's three callers has a browser to ask — the read API,
        // TraceIngestFilter and TraceSaveFilter are all server side — so the host's zone is the only other
        // candidate, and it makes one config string mean different things on different machines. The UI
        // never arrives here: it builds a filter through BrowserTimeZone.ParseInputToLocal, which is where a
        // viewer's zone belongs.
        var filter = TraceFilter.Parse("StartTime=2026-08-09T14:00:00");

        Assert.Equal(TimeSpan.Zero, filter.StartTime!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero), filter.StartTime);
    }

    [Fact]
    public void Parse_KeepsAnExplicitOffset()
    {
        var filter = TraceFilter.Parse("EndTime=2026-08-09T14:00:00%2B02:00");

        Assert.Equal(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero), filter.EndTime);
    }

    [Fact]
    public void Parse_AcceptsTheTimestampShapeTheReadApiPrints()
    {
        // Units.Timestamp's format, so a time copied out of any analysis output feeds straight back in.
        var filter = TraceFilter.Parse("StartTime=2026-08-09T14:00:00.000Z");

        Assert.Equal(new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero), filter.StartTime);
    }

    [Fact]
    public void Parse_ReadsTheSameValuesWhateverTheHostCulture()
    {
        // UseRequestLocalization sets CurrentCulture from Accept-Language, so a caller's culture reaches
        // this. A query string is invariant however that reads.
        var previous = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var filter = TraceFilter.Parse(
                "SpansMinimum=1234&DurationMinimum=1.5&StartTime=2026-08-09T14:00:00Z"
            );

            Assert.Equal(1234, filter.SpansMinimum);
            Assert.Equal(1.5, filter.DurationMinimum);
            Assert.Equal(new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero), filter.StartTime);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Parse_ReadsEveryDimension()
    {
        var filter = TraceFilter.Parse(
            "SpansMinimum=3"
            + "&DurationMinimum=0.5"
            + "&DurationMaximum=30"
            + "&HasError=true"
            + "&IgnoredTraceNames=GET /health|GET /ping"
            + "&ExclusiveTraceNames=GET /things"
            + "&SpanAttributeFilter=service.name=checkout;tier=gold"
            + "&StartTime=2026-08-08T10:00:00%2B00:00"
            + "&EndTime=2026-08-08T11:00:00%2B00:00"
        );

        Assert.Equal(3, filter.SpansMinimum);
        Assert.Equal(0.5, filter.DurationMinimum);
        Assert.Equal(30, filter.DurationMaximum);
        Assert.True(filter.HasError);
        Assert.Equal(["GET /health", "GET /ping"], filter.IgnoredTraceNames.Order().ToArray());
        Assert.Equal(["GET /things"], filter.ExclusiveTraceNames!.Order().ToArray());
        Assert.Equal("checkout", filter.SpanAttributeFilter["service.name"]);
        Assert.Equal("gold", filter.SpanAttributeFilter["tier"]);
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero), filter.StartTime);
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 11, 0, 0, TimeSpan.Zero), filter.EndTime);
        Assert.False(filter.IsEmpty);
    }

    [Theory]
    // Unparseable or out-of-range values are silently ignored rather than erroring.
    [InlineData("SpansMinimum=nope")]
    [InlineData("SpansMinimum=0")]
    [InlineData("SpansMinimum=-1")]
    [InlineData("DurationMinimum=nope")]
    [InlineData("DurationMinimum=0")]
    [InlineData("DurationMaximum=-2")]
    [InlineData("HasError=maybe")]
    [InlineData("StartTime=yesterday")]
    [InlineData("EndTime=")]
    [InlineData("IgnoredTraceNames=")]
    [InlineData("ExclusiveTraceNames=")]
    [InlineData("SpanAttributeFilter=")]
    [InlineData("SomethingElseEntirely=7")]
    public void Parse_IgnoresValuesItCannotUse(string queryString) =>
        Assert.True(TraceFilter.Parse(queryString).IsEmpty);

    [Fact]
    public void Parse_ReadsDurationsInInvariantCulture()
    {
        // '.' is the decimal separator regardless of the machine's locale.
        Assert.Equal(1.5, TraceFilter.Parse("DurationMinimum=1.5").DurationMinimum);
    }

    [Fact]
    public void Parse_DropsEmptyEntriesFromNameLists()
    {
        var filter = TraceFilter.Parse("IgnoredTraceNames=|GET /health||GET /ping|");

        Assert.Equal(["GET /health", "GET /ping"], filter.IgnoredTraceNames.Order().ToArray());
    }

    [Fact]
    public void Parse_ReadsAnAttributeValueContainingAnEqualsSign()
    {
        var filter = TraceFilter.Parse("SpanAttributeFilter=db.statement=a=b");

        Assert.Equal("a=b", filter.SpanAttributeFilter["db.statement"]);
    }

    [Fact]
    public void Parse_KeepsTheFirstOfADuplicatedAttributeKey()
    {
        var filter = TraceFilter.Parse("SpanAttributeFilter=tier=gold;tier=silver");

        Assert.Equal("gold", Assert.Single(filter.SpanAttributeFilter).Value);
    }

    [Theory]
    // Pairs without a '=' or without a key are dropped rather than stored as junk.
    [InlineData("SpanAttributeFilter=tier")]
    [InlineData("SpanAttributeFilter==gold")]
    public void Parse_DropsMalformedAttributePairs(string queryString) =>
        Assert.Empty(TraceFilter.Parse(queryString).SpanAttributeFilter);
}

/// <summary>
/// <c>Matches</c> is "show or save this trace now"; <c>IsRejected</c> is the stricter "this can never
/// qualify, throw it away". The gap between them is what stops a trace that is merely still arriving from
/// being discarded, so each dimension is checked against both.
/// </summary>
public sealed class TraceFilterMatchingTests : IDisposable
{
    private readonly TracesRepository mRepository = Fake.TracesRepository();

    private int mTraceCount;

    [Fact]
    public void SpansMinimum_DoesNotRejectATraceStillGrowingTowardsIt()
    {
        var filter = TraceFilter.Parse("SpansMinimum=3");
        var trace = this.Trace(Fake.Span(id: "0000000000000001"));

        Assert.False(filter.Matches(trace));
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void SpansMinimum_MatchesOnceEnoughSpansArrive()
    {
        var filter = TraceFilter.Parse("SpansMinimum=2");
        var trace = this.Trace(
            Fake.Span(id: "0000000000000001"),
            Fake.Span(id: "0000000000000002")
        );

        Assert.True(filter.Matches(trace));
    }

    [Fact]
    public void DurationMinimum_DoesNotRejectAShortTraceThatCouldStillLengthen()
    {
        var filter = TraceFilter.Parse("DurationMinimum=10");
        var trace = this.Trace(Fake.Span(durationMs: 5));

        Assert.False(filter.Matches(trace));
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void DurationMaximum_RejectsATraceThatHasAlreadyRunTooLong()
    {
        // Nothing can shorten a trace, so this is the one duration bound that can reject.
        var filter = TraceFilter.Parse("DurationMaximum=1");
        var trace = this.Trace(Fake.Span(durationMs: 5000));

        Assert.False(filter.Matches(trace));
        Assert.True(filter.IsRejected(trace));
    }

    [Fact]
    public void HasErrorFalse_RejectsATraceThatHasErrored()
    {
        var filter = TraceFilter.Parse("HasError=false");
        var trace = this.Trace(Fake.Span(status: StatusCode.Error));

        Assert.False(filter.Matches(trace));
        Assert.True(filter.IsRejected(trace));
    }

    [Fact]
    public void HasErrorTrue_DoesNotRejectACleanTraceThatCouldStillFail()
    {
        var filter = TraceFilter.Parse("HasError=true");
        var trace = this.Trace(Fake.Span(status: StatusCode.Ok));

        Assert.False(filter.Matches(trace));
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void IgnoredTraceNames_RejectsAMatchingRootSpanName()
    {
        var filter = TraceFilter.Parse("IgnoredTraceNames=GET /health");
        var trace = this.Trace(Fake.Span(name: "GET /health"));

        Assert.False(filter.Matches(trace));
        Assert.True(filter.IsRejected(trace));
    }

    [Fact]
    public void IgnoredTraceNames_PassesATraceWithNoRootSpanYet()
    {
        // The ignored name belongs to a child here; with no root span there is nothing to judge against.
        var filter = TraceFilter.Parse("IgnoredTraceNames=GET /health");
        var trace = this.Trace(Fake.Span(parentSpanId: Otlp.ROOT_SPAN_ID, name: "GET /health"));

        Assert.True(filter.Matches(trace));
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void ExclusiveTraceNames_RejectsARootSpanWithTheWrongName()
    {
        var filter = TraceFilter.Parse("ExclusiveTraceNames=GET /things");
        var trace = this.Trace(Fake.Span(name: "GET /health"));

        Assert.False(filter.Matches(trace));
        Assert.True(filter.IsRejected(trace));
    }

    [Fact]
    public void ExclusiveTraceNames_DoesNotRejectATraceWithNoRootSpanYet()
    {
        // The root span may still be in flight, and it may yet turn out to carry a wanted name.
        var filter = TraceFilter.Parse("ExclusiveTraceNames=GET /things");
        var trace = this.Trace(Fake.Span(parentSpanId: Otlp.ROOT_SPAN_ID, name: "SELECT things"));

        Assert.False(filter.Matches(trace));
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void SpanAttributeFilter_MatchesWhenAnySpanCarriesAnyPair()
    {
        var filter = TraceFilter.Parse("SpanAttributeFilter=tier=gold");
        var trace = this.Trace(
            Fake.Span(id: "0000000000000001"),
            Fake.Span(id: "0000000000000002", attributes: new() { ["tier"] = "gold" })
        );

        Assert.True(filter.Matches(trace));
    }

    [Fact]
    public void SpanAttributeFilter_ComparesValuesCaseInsensitively()
    {
        var filter = TraceFilter.Parse("SpanAttributeFilter=tier=GOLD");
        var trace = this.Trace(Fake.Span(attributes: new() { ["tier"] = "gold" }));

        Assert.True(filter.Matches(trace));
    }

    [Fact]
    public void SpanAttributeFilter_DoesNotMatchWhenNoSpanCarriesAPair()
    {
        var filter = TraceFilter.Parse("SpanAttributeFilter=tier=gold");
        var trace = this.Trace(Fake.Span(attributes: new() { ["tier"] = "bronze" }));

        Assert.False(filter.Matches(trace));
        // A matching attribute could still arrive on a later span, so this never rejects.
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void StartTime_RejectsATraceThatStartedBeforeIt()
    {
        var filter = TraceFilter.Parse($"StartTime={Query(Otlp.ORIGIN.AddMinutes(1))}");
        var trace = this.Trace(Fake.Span());

        Assert.False(filter.Matches(trace));
        Assert.True(filter.IsRejected(trace));
    }

    [Fact]
    public void EndTime_RejectsATraceThatStartedAfterIt()
    {
        var filter = TraceFilter.Parse($"EndTime={Query(Otlp.ORIGIN.AddMinutes(-1))}");
        var trace = this.Trace(Fake.Span());

        Assert.False(filter.Matches(trace));
        Assert.True(filter.IsRejected(trace));
    }

    [Fact]
    public void TimeBounds_DoNotRejectATraceThatHasNoSpansYet()
    {
        // An empty trace has Start == DateTimeOffset.MaxValue, a placeholder rather than a real start time,
        // so it must not be read as "started after EndTime" and thrown away before its spans land.
        var filter = TraceFilter.Parse($"EndTime={Query(Otlp.ORIGIN)}");
        var trace = this.Trace();

        Assert.Equal(DateTimeOffset.MaxValue, trace.Start);
        Assert.False(filter.IsRejected(trace));
    }

    [Fact]
    public void EmptyFilter_MatchesAndRejectsNothing()
    {
        var trace = this.Trace(Fake.Span(status: StatusCode.Error, durationMs: 100_000));

        Assert.True(TraceFilter.Empty.Matches(trace));
        Assert.False(TraceFilter.Empty.IsRejected(trace));
    }

    public void Dispose() => mRepository.Dispose();

    private static string Query(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>A fresh trace, keyed so that each test in the class gets its own.</summary>
    private TraceItem Trace(params SpanData[] spans)
    {
        var trace = mRepository.GetOrAddTrace((++mTraceCount).ToString("x32", CultureInfo.InvariantCulture));

        if (spans.Length > 0)
        {
            trace.AddSpans(spans);
        }

        return trace;
    }
}
