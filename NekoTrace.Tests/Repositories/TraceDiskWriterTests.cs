namespace NekoTrace.Tests.Repositories;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Repositories.Traces;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

public sealed class TraceDiskWriterTests : IDisposable
{
    private readonly string mDirectory = Path.Combine(
        Path.GetTempPath(),
        "NekoTrace.Tests",
        Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture)
    );

    private readonly TracesRepository mTraces = Fake.TracesRepository();

    [Fact]
    public async Task WritesNothing_WhenNoSaveDirectoryIsConfigured()
    {
        this.Trace(Fake.Span());

        await using var writer = this.Writer(saveDirectory: null);
        await writer.Timer_Tick();

        Assert.False(Directory.Exists(mDirectory));
    }

    [Fact]
    public async Task CreatesTheSaveDirectory_WhenItIsMissing()
    {
        this.Trace(Fake.Span());

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        Assert.True(Directory.Exists(mDirectory));
    }

    [Fact]
    public async Task WritesEachTraceAsGzippedJsonNamedAfterItsRootSpan()
    {
        this.Trace(Fake.Span(name: "GET /things"));

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        var expectedName =
            "NekoTrace-"
            + Otlp.ORIGIN.ToString("yyMMddTHHmmss", CultureInfo.InvariantCulture)
            // '/' is not a legal filename character on any platform, so it is replaced.
            + "-GET _things-"
            + Otlp.TRACE_ID[..16]
            + ".json.gz";

        Assert.Equal([expectedName], this.FileNames());

        var written = await this.Read(expectedName);

        Assert.Equal(TraceSerializableData.CURRENT_VERSION, written.Version);
        Assert.Equal(Otlp.TRACE_ID, written.Id);
        Assert.Equal(Otlp.ROOT_SPAN_ID, Assert.Single(written.Spans).Id);
    }

    [Fact]
    public async Task WritesNoTempFileBehind()
    {
        this.Trace(Fake.Span());

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        Assert.Empty(Directory.GetFiles(mDirectory, "*.tmp"));
    }

    [Fact]
    public async Task DoesNotRewriteATraceThatHasNotGrown()
    {
        var trace = this.Trace(Fake.Span());

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        // Deleting the file is how the second tick tells us whether it wrote: if the span count short
        // circuit works, nothing reappears.
        foreach (var file in Directory.GetFiles(mDirectory))
        {
            File.Delete(file);
        }

        await writer.Timer_Tick();

        Assert.Empty(this.FileNames());
        Assert.Single(trace.Spans);
    }

    [Fact]
    public async Task RewritesATraceThatHasGrown()
    {
        var trace = this.Trace(Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things"));

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        trace.AddSpan(Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID, startMs: 1));

        await writer.Timer_Tick();

        var written = await this.Read(Assert.Single(this.FileNames()));

        Assert.Equal(2, written.Spans.Length);
    }

    [Fact]
    public async Task RenamesTheFileOnceTheRootSpanArrives()
    {
        // Spans can arrive in any order, so the first tick may only know the trace as "UnknownTrace".
        var trace = this.Trace(
            Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID, startMs: 5)
        );

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        Assert.Contains("UnknownTrace", Assert.Single(this.FileNames()), StringComparison.Ordinal);

        trace.AddSpan(Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things", startMs: 5));

        await writer.Timer_Tick();

        var fileName = Assert.Single(this.FileNames());

        Assert.Contains("GET _things", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("UnknownTrace", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsATraceTheSaveFilterDoesNotMatchYet()
    {
        this.Trace(Fake.Span());

        await using var writer = this.Writer(saveFilter: "SpansMinimum=3");
        await writer.Timer_Tick();

        Assert.Empty(this.FileNames());
    }

    [Fact]
    public async Task WritesATraceOnceTheSaveFilterMatches()
    {
        var trace = this.Trace(Fake.Span(id: "0000000000000001"));

        await using var writer = this.Writer(saveFilter: "SpansMinimum=2");
        await writer.Timer_Tick();

        Assert.Empty(this.FileNames());

        trace.AddSpan(Fake.Span(id: "0000000000000002", startMs: 1));

        await writer.Timer_Tick();

        Assert.Single(this.FileNames());
    }

    [Fact]
    public async Task DeletesTheFileOfATraceTheFilterLaterRejects()
    {
        // DurationMaximum is one of the criteria that can never be satisfied again once exceeded.
        var trace = this.Trace(Fake.Span(id: "0000000000000001", durationMs: 100));

        await using var writer = this.Writer(saveFilter: "DurationMaximum=1");
        await writer.Timer_Tick();

        Assert.Single(this.FileNames());

        trace.AddSpan(Fake.Span(id: "0000000000000002", startMs: 1, durationMs: 5_000));

        await writer.Timer_Tick();

        Assert.Empty(this.FileNames());
    }

    [Fact]
    public async Task DisposalDoesOneFinalTick()
    {
        this.Trace(Fake.Span());

        var writer = this.Writer();
        await writer.DisposeAsync();

        Assert.Single(this.FileNames());
    }

    [Fact]
    public async Task WritesEveryTraceItHolds()
    {
        this.Trace(Fake.Span(name: "GET /things"));

        mTraces
            .GetOrAddTrace(Otlp.OTHER_TRACE_ID)
            .AddSpan(Fake.Span(traceId: Otlp.OTHER_TRACE_ID, name: "GET /others"));

        await using var writer = this.Writer();
        await writer.Timer_Tick();

        Assert.Equal(2, this.FileNames().Length);
    }

    public void Dispose()
    {
        mTraces.Dispose();

        if (Directory.Exists(mDirectory))
        {
            Directory.Delete(mDirectory, recursive: true);
        }
    }

    private TraceItem Trace(params SpanData[] spans)
    {
        var trace = mTraces.GetOrAddTrace(Otlp.TRACE_ID);

        trace.AddSpans(spans);

        return trace;
    }

    private TraceDiskWriter Writer(string? saveDirectory = "", string? saveFilter = null) =>
        new(
            mTraces,
            Fake.Configuration(
                // The default sentinel means "the temp directory for this test"; an explicit null means
                // "not configured at all", which is the off switch for the whole writer.
                ("NekoTrace:TraceSaveDirectory", saveDirectory is "" ? mDirectory : saveDirectory),
                ("NekoTrace:TraceSaveFilter", saveFilter)
            )
        );

    private string[] FileNames() =>
        Directory.Exists(mDirectory)
            ? [.. Directory.GetFiles(mDirectory, "*.json.gz").Select(Path.GetFileName).OfType<string>().Order()]
            : [];

    private async Task<TraceSerializableData> Read(string fileName)
    {
        await using var fileStream = File.OpenRead(Path.Combine(mDirectory, fileName));
        await using var decompressed = new GZipStream(fileStream, CompressionMode.Decompress);

        return await JsonSerializer.DeserializeAsync<TraceSerializableData>(
            decompressed,
            TraceSerializableData.SerializerOptions,
            TestContext.Current.CancellationToken
        )
            ?? throw new InvalidOperationException($"'{fileName}' deserialised to null.");
    }
}
