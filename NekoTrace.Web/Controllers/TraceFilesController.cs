namespace NekoTrace.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Utilities;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

[Route("api/trace-files")]
[ApiController]
public sealed class TraceFilesController : ControllerBase
{
    private readonly TracesRepository mTraces;

    public TraceFilesController(TracesRepository traces)
    {
        mTraces = traces;
    }

    [HttpGet()]
    public async Task DownloadTraceSpans(
        [FromQuery] string traceId,
        CancellationToken cancellationToken
    )
    {
        var trace = mTraces.TryGetTrace(traceId);
        if (trace is null)
        {
            this.Response.StatusCode = 404;
            return;
        }

        this.Response.ContentType = "application/gzip";

        var timestamp = trace.Start.ToString("yyMMddTHHmmss", CultureInfo.InvariantCulture);

        this.Response.Headers.ContentDisposition = $"attachment; filename=\"NekoTrace-{timestamp}-{Uri.EscapeDataString(trace.RootSpan?.Name ?? traceId)}.json.gz\"";

        await using var compressionStream = new GZipStream(
            this.Response.Body,
            CompressionLevel.SmallestSize,
            leaveOpen: true
        );

        await JsonSerializer.SerializeAsync(
            compressionStream,
            new TraceSerializableData()
            {
                Version = TraceSerializableData.CURRENT_VERSION,
                Id = trace.Id,
                Spans = [.. trace.Spans],
            },
            TraceSerializableData.SerializerOptions,
            cancellationToken
        );
    }

    /// <summary>
    /// Takes trace files back in, and — for a caller that asked for JSON — answers with the ids it ingested.
    /// </summary>
    /// <remarks>
    /// The Home page posts a plain multipart form straight at this route from the browser, so the response is
    /// a navigation: any body at all would take the page off the app and leave the reader looking at it. That
    /// is why the default is still 204. A caller sending <c>Accept: application/json</c> is not a browser
    /// form — no browser asks for JSON on a form navigation — and gets what it needs to do anything with the
    /// upload, which is the id to query. Without it the CLI's <c>--file</c> would have to guess, either by
    /// reading the id out of the file itself (duplicating the base64 to hex normalisation below) or by
    /// diffing the trace list around the upload (wrong the moment a collector is also receiving spans).
    /// </remarks>
    [HttpPost()]
    public async Task<IActionResult> UploadTraceSpans(CancellationToken cancellationToken)
    {
        var form = await this.Request.ReadFormAsync(cancellationToken);
        var ingested = new List<string>();

        foreach (var file in form.Files)
        {
            await using var fileStream = file.OpenReadStream();

            TraceSerializableData? uploadedTrace;
            if (string.IsNullOrEmpty(file.FileName)
                || file.FileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            )
            {
                await using var decompressionStream = new GZipStream(
                    fileStream,
                    CompressionMode.Decompress
                );

                uploadedTrace = await JsonSerializer.DeserializeAsync<TraceSerializableData>(
                    decompressionStream,
                    TraceSerializableData.SerializerOptions,
                    cancellationToken
                );
            }
            else
            {
                uploadedTrace = await JsonSerializer.DeserializeAsync<TraceSerializableData>(
                    fileStream,
                    TraceSerializableData.SerializerOptions,
                    cancellationToken
                );
            }

            if (uploadedTrace is null)
            {
                continue;
            }

            if (uploadedTrace.Version > TraceSerializableData.CURRENT_VERSION)
            {
                return this.BadRequest(
                    FormattableString.Invariant(
                        $"'{file.FileName}' uses trace file format version {uploadedTrace.Version}, but this build of NekoTrace only understands up to {TraceSerializableData.CURRENT_VERSION}."
                    )
                );
            }

            // LEGACY_VERSION files carry base64 ids throughout, so every id in the file is normalised, not just
            // the trace's own — converting only the trace id would leave the spans pointing at a key that no
            // longer matches. Run unconditionally rather than gated on the version: it is idempotent for ids
            // that are already hex, so it also repairs a hand-edited or mislabelled file.
            var trace = mTraces.GetOrAddTrace(
                TraceIds.NormalizeToHex(uploadedTrace.Id, TraceIds.TRACE_ID_BYTE_LENGTH)
            );

            trace.AddSpans(uploadedTrace.Spans.Select(NormalizeSpanIds));

            if (!ingested.Contains(trace.Id, StringComparer.Ordinal))
            {
                ingested.Add(trace.Id);
            }
        }

        return this.WantsJson() ? this.Ok(ingested) : new NoContentResult();
    }

    private bool WantsJson() =>
        this.Request.Headers.Accept.Any(value =>
            value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) is true
        );

    private static SpanData NormalizeSpanIds(SpanData span)
    {
        var id = TraceIds.NormalizeToHex(span.Id, TraceIds.SPAN_ID_BYTE_LENGTH);
        var traceId = TraceIds.NormalizeToHex(span.TraceId, TraceIds.TRACE_ID_BYTE_LENGTH);
        var parentSpanId = string.IsNullOrEmpty(span.ParentSpanId)
            ? null
            : TraceIds.NormalizeToHex(span.ParentSpanId, TraceIds.SPAN_ID_BYTE_LENGTH);

        // Ids already in the stored form normalise to the very strings they arrived as, which is every id in
        // a file this build wrote — so the whole trace passes through without a span being copied.
        return ReferenceEquals(id, span.Id)
            && ReferenceEquals(traceId, span.TraceId)
            && ReferenceEquals(parentSpanId, span.ParentSpanId)
            ? span
            : span with
            {
                Id = id,
                TraceId = traceId,
                ParentSpanId = parentSpanId,
            };
    }
}
