namespace NekoTrace.Tests.Controllers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NekoTrace.Tests.TestData;
using NekoTrace.Web.Controllers;
using NekoTrace.Web.Repositories.Traces;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

public sealed class TraceFilesControllerTests
{
    [Fact]
    public async Task Download_Returns404ForAnUnknownTrace()
    {
        using var repository = Fake.TracesRepository();
        var (controller, response) = Controller(repository);

        await controller.DownloadTraceSpans(Otlp.TRACE_ID, TestContext.Current.CancellationToken);

        Assert.Equal(404, controller.Response.StatusCode);
        Assert.Empty(response.ToArray());
    }

    [Fact]
    public async Task Download_WritesGzippedJsonStampedWithTheCurrentVersion()
    {
        using var repository = Fake.TracesRepository();
        Ingest(repository, Fake.Span(name: "GET /things"));

        var file = await Download(repository, Otlp.TRACE_ID);

        Assert.Equal(TraceSerializableData.CURRENT_VERSION, file.Version);
        Assert.Equal(Otlp.TRACE_ID, file.Id);
        Assert.Equal(Otlp.ROOT_SPAN_ID, Assert.Single(file.Spans).Id);
    }

    [Fact]
    public async Task Download_NamesTheFileAfterTheRootSpan()
    {
        using var repository = Fake.TracesRepository();
        Ingest(repository, Fake.Span(name: "GET /things"));

        var (controller, _) = Controller(repository);
        await controller.DownloadTraceSpans(Otlp.TRACE_ID, TestContext.Current.CancellationToken);

        Assert.Equal("application/gzip", controller.Response.ContentType);
        Assert.Contains(
            "GET%20%2Fthings",
            controller.Response.Headers.ContentDisposition.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RoundTrip_PreservesEverySpanAndItsShape()
    {
        using var source = Fake.TracesRepository();
        Ingest(
            source,
            Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things", startMs: 0, durationMs: 100),
            Fake.Span(
                id: Otlp.CHILD_SPAN_ID,
                parentSpanId: Otlp.ROOT_SPAN_ID,
                name: "SELECT things",
                startMs: 10,
                durationMs: 20,
                attributes: new() { ["db.system"] = "postgresql" }
            )
        );

        using var destination = Fake.TracesRepository();
        await Upload(destination, await DownloadBytes(source, Otlp.TRACE_ID));

        var original = source.TryGetTrace(Otlp.TRACE_ID)!;
        var restored = destination.TryGetTrace(Otlp.TRACE_ID);

        Assert.NotNull(restored);
        Assert.Equal(Fake.SpanIds(original), Fake.SpanIds(restored));
        Assert.Equal(original.RootSpan?.Id, restored.RootSpan?.Id);
        Assert.Equal(original.Start, restored.Start);
        Assert.Equal(original.End, restored.End);
        Assert.Equal(original.Duration, restored.Duration);

        var child = restored.SpansById[Otlp.CHILD_SPAN_ID];

        Assert.Equal(Otlp.ROOT_SPAN_ID, child.ParentSpanId);
        Assert.Equal("SELECT things", child.Name);
        Assert.Equal("postgresql", child.Attributes["db.system"]?.ToString());
    }

    [Fact]
    public async Task Upload_OrdersSpansByStartTimeEvenWhenTheFileDoesNot()
    {
        // 5cc5ce3. A file written by an older build carries the reversed order it was stored in.
        using var repository = Fake.TracesRepository();

        await Upload(
            repository,
            Gzip(
                Fake.TraceFile(
                    Otlp.TRACE_ID,
                    Fake.Span(id: "0000000000000003", startMs: 20),
                    Fake.Span(id: "0000000000000001", startMs: 0),
                    Fake.Span(id: "0000000000000002", startMs: 10)
                )
            )
        );

        Assert.Equal(
            ["0000000000000001", "0000000000000002", "0000000000000003"],
            Fake.SpanIds(repository.TryGetTrace(Otlp.TRACE_ID)!)
        );
    }

    [Fact]
    public async Task Upload_OfTheSameFileTwiceDoesNotDoubleTheSpanCount()
    {
        // 54ea84d. This is the case that made the bug visible: the ordered list grew a second entry that
        // SpansById, being keyed, could not see.
        using var repository = Fake.TracesRepository();

        var bytes = Gzip(
            Fake.TraceFile(
                Otlp.TRACE_ID,
                Fake.Span(id: Otlp.ROOT_SPAN_ID),
                Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID)
            )
        );

        await Upload(repository, bytes);
        await Upload(repository, bytes);

        var trace = repository.TryGetTrace(Otlp.TRACE_ID)!;

        Assert.Equal(2, trace.Spans.Count);
        Assert.Equal(trace.Spans.Count, trace.SpansById.Count);
    }

    [Fact]
    public async Task Upload_RewritesEveryIdInALegacyBase64File()
    {
        // 7091de6. Converting only the trace id would leave the spans pointing at a key that no longer
        // matches, so the parent link is the assertion that actually matters here.
        using var repository = Fake.TracesRepository();

        var legacy = Fake.AsLegacyBase64(
            Fake.TraceFile(
                Otlp.TRACE_ID,
                Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things", startMs: 0),
                Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID, startMs: 1)
            )
        );

        Assert.Equal(TraceSerializableData.LEGACY_VERSION, legacy.Version);

        await Upload(repository, Gzip(legacy));

        var trace = repository.TryGetTrace(Otlp.TRACE_ID);

        Assert.NotNull(trace);
        Assert.Equal([Otlp.ROOT_SPAN_ID, Otlp.CHILD_SPAN_ID], Fake.SpanIds(trace));
        Assert.Equal(Otlp.ROOT_SPAN_ID, trace.SpansById[Otlp.CHILD_SPAN_ID].ParentSpanId);
        Assert.Equal(Otlp.TRACE_ID, trace.SpansById[Otlp.CHILD_SPAN_ID].TraceId);
        Assert.Equal(Otlp.ROOT_SPAN_ID, trace.RootSpan?.Id);
    }

    [Fact]
    public async Task Upload_RejectsAFileFromANewerBuild()
    {
        // e0554cc. Failing here with a clear message beats failing obscurely part way through the spans.
        using var repository = Fake.TracesRepository();

        var future = new TraceSerializableData()
        {
            Version = TraceSerializableData.CURRENT_VERSION + 1,
            Id = Otlp.TRACE_ID,
            Spans = [Fake.Span()],
        };

        var result = await Upload(repository, Gzip(future), fileName: "from-the-future.json.gz");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        Assert.Contains(
            "from-the-future.json.gz",
            badRequest.Value?.ToString() ?? string.Empty,
            StringComparison.Ordinal
        );
        Assert.Empty(repository.Traces);
    }

    [Fact]
    public async Task Upload_AcceptsAFileWithNoVersionAtAllAsLegacy()
    {
        // Files written before the version field existed have no property to read.
        using var repository = Fake.TracesRepository();

        var json = $$"""
            {
                "Id": "{{Fake.ToBase64(Otlp.TRACE_ID)}}",
                "Spans": [{{SpanJson(Fake.ToBase64(Otlp.ROOT_SPAN_ID), Fake.ToBase64(Otlp.TRACE_ID))}}]
            }
            """;

        await Upload(repository, Gzip(Encoding.UTF8.GetBytes(json)));

        var trace = repository.TryGetTrace(Otlp.TRACE_ID);

        Assert.NotNull(trace);
        Assert.Equal(Otlp.ROOT_SPAN_ID, Assert.Single(trace.Spans).Id);
    }

    [Fact]
    public async Task Upload_AcceptsAnUncompressedJsonFile()
    {
        using var repository = Fake.TracesRepository();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            Fake.TraceFile(Otlp.TRACE_ID, Fake.Span())
        );

        await Upload(repository, bytes, fileName: "trace.json");

        Assert.Single(repository.Traces);
    }

    [Fact]
    public async Task Upload_MergesIntoATraceAlreadyBeingCollected()
    {
        // The upload path goes through GetOrAddTrace, so a file for a live trace tops it up.
        using var repository = Fake.TracesRepository();
        Ingest(repository, Fake.Span(id: Otlp.ROOT_SPAN_ID, name: "GET /things"));

        await Upload(
            repository,
            Gzip(
                Fake.TraceFile(
                    Otlp.TRACE_ID,
                    Fake.Span(id: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID, startMs: 5)
                )
            )
        );

        var trace = Assert.Single(repository.Traces);

        Assert.Equal([Otlp.ROOT_SPAN_ID, Otlp.CHILD_SPAN_ID], Fake.SpanIds(trace));
    }

    private static void Ingest(TracesRepository repository, params SpanData[] spans) =>
        repository.GetOrAddTrace(Otlp.TRACE_ID).AddSpans(spans);

    private static (TraceFilesController Controller, MemoryStream ResponseBody) Controller(
        TracesRepository repository
    )
    {
        var responseBody = new MemoryStream();

        var controller = new TraceFilesController(repository)
        {
            ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { Response = { Body = responseBody } },
            },
        };

        return (controller, responseBody);
    }

    private static async Task<byte[]> DownloadBytes(TracesRepository repository, string traceId)
    {
        var (controller, responseBody) = Controller(repository);

        await controller.DownloadTraceSpans(traceId, TestContext.Current.CancellationToken);

        return responseBody.ToArray();
    }

    private static async Task<TraceSerializableData> Download(
        TracesRepository repository,
        string traceId
    )
    {
        using var compressed = new MemoryStream(await DownloadBytes(repository, traceId));
        await using var decompressed = new GZipStream(compressed, CompressionMode.Decompress);

        return await JsonSerializer.DeserializeAsync<TraceSerializableData>(
            decompressed,
            cancellationToken: TestContext.Current.CancellationToken
        )
            ?? throw new InvalidOperationException("The download deserialised to null.");
    }

    private static async Task<IActionResult> Upload(
        TracesRepository repository,
        byte[] fileBytes,
        string fileName = "trace.json.gz"
    )
    {
        var (controller, _) = Controller(repository);

        using var fileStream = new MemoryStream(fileBytes);

        controller.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.Ordinal),
            new FormFileCollection()
            {
                new FormFile(fileStream, 0, fileStream.Length, "files", fileName),
            }
        );

        return await controller.UploadTraceSpans(TestContext.Current.CancellationToken);
    }

    private static byte[] Gzip(TraceSerializableData file) =>
        Gzip(JsonSerializer.SerializeToUtf8Bytes(file));

    private static byte[] Gzip(byte[] contents)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(contents);
        }

        return output.ToArray();
    }

    private static string SpanJson(string spanId, string traceId) =>
        $$"""
        {
            "Id": "{{spanId}}",
            "TraceId": "{{traceId}}",
            "ParentSpanId": null,
            "Name": "GET /things",
            "Kind": 1,
            "Attributes": {},
            "StartTime": "2026-08-08T12:00:00+00:00",
            "StartTimeMs": 1775000000000,
            "EndTime": "2026-08-08T12:00:00.01+00:00",
            "EndTimeMs": 1775000000010,
            "StatusCode": 0,
            "StatusMessage": null,
            "TraceState": null,
            "Events": [],
            "Links": []
        }
        """;
}
