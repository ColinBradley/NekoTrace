namespace NekoTrace.Web.Controllers;

using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NekoTrace.Web.Repositories;

[Route("api/trace-files")]
[ApiController]
public class TraceFilesController : ControllerBase
{
    private readonly TracesRepository mTraces;

    public TraceFilesController(TracesRepository traces)
    {
        mTraces = traces;
    }

    [HttpGet("{traceId}.json.gz")]
    public async Task DownloadTraceSpans(
        [FromRoute] string traceId,
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
        this.Response.Headers.ContentDisposition = "attachment";

        await using var compressionStream = new GZipStream(
            this.Response.Body,
            CompressionLevel.SmallestSize,
            leaveOpen: true
        );

        await JsonSerializer.SerializeAsync(
            compressionStream,
            new TraceData() { Id = trace.Id, Spans = [.. trace.Spans] },
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

            TraceData? uploadedTrace;
            if (string.IsNullOrEmpty(file.FileName) || file.FileName.EndsWith(".gz"))
            {
                await using var decompressionStream = new GZipStream(
                    fileStream,
                    CompressionMode.Decompress
                );

                uploadedTrace = await JsonSerializer.DeserializeAsync<TraceData>(
                    decompressionStream,
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                uploadedTrace = await JsonSerializer.DeserializeAsync<TraceData>(
                    fileStream,
                    cancellationToken: cancellationToken
                );
            }

            if (uploadedTrace is null)
            {
                continue;
            }

            var trace = mTraces.GetOrAddTrace(
                Google.Protobuf.ByteString.CopyFromUtf8(uploadedTrace.Id)
            );

            trace.AddSpans(uploadedTrace.Spans);
        }

        return new NoContentResult();
    }
}
