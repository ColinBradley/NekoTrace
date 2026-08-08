namespace NekoTrace.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Utilities;
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
        this.Response.Headers.ContentDisposition = $"attachment; filename=\"NekoTrace-{trace.Start:yyMMddTHHmmss}-{Uri.EscapeDataString(trace.RootSpan?.Name ?? traceId)}.json.gz\"";

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
            cancellationToken: cancellationToken
        );
    }

    [HttpPost()]
    public async Task<IActionResult> UploadTraceSpans(CancellationToken cancellationToken)
    {
        var form = await this.Request.ReadFormAsync(cancellationToken);

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
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                uploadedTrace = await JsonSerializer.DeserializeAsync<TraceSerializableData>(
                    fileStream,
                    cancellationToken: cancellationToken
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
        }

        return new NoContentResult();
    }

    private static SpanData NormalizeSpanIds(SpanData span) =>
        span with
        {
            Id = TraceIds.NormalizeToHex(span.Id, TraceIds.SPAN_ID_BYTE_LENGTH),
            TraceId = TraceIds.NormalizeToHex(span.TraceId, TraceIds.TRACE_ID_BYTE_LENGTH),
            ParentSpanId = string.IsNullOrEmpty(span.ParentSpanId)
                ? null
                : TraceIds.NormalizeToHex(span.ParentSpanId, TraceIds.SPAN_ID_BYTE_LENGTH),
        };
}
