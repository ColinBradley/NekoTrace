namespace NekoTrace.Web.Endpoints;

using Google.Protobuf;
using Microsoft.Net.Http.Headers;
using NekoTrace.Web.Repositories.Metrics;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Utilities;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using System.Text;

// OTLP/HTTP, the sibling of the gRPC services in GrpcServices/. /v1/traces and /v1/metrics differ only in
// which message they parse and which repository they hand it to, so the body they share lives in one generic
// method — the content negotiation below is fiddly enough that two copies of it drifted apart once already.
internal static class OtlpHttpEndpoints
{
    private const string PROTOBUF_CONTENT_TYPE = "application/x-protobuf";

    public static IEndpointRouteBuilder MapOtlpHttpEndpoints(
        this IEndpointRouteBuilder endpoints,
        TracesRepository traces,
        MetricsRepository metrics
    )
    {
        // Typed as Func<HttpContext, Task<IResult>> rather than written inline. A bare lambda here also
        // converts to RequestDelegate, which is the more specific MapPost overload and therefore the one
        // chosen — and it discards the IResult, leaving every response a bodiless 200.
        Func<HttpContext, Task<IResult>> exportTraces =
            context => Export(context, ExportTraceServiceRequest.Parser, traces.ProcessTraces);

        Func<HttpContext, Task<IResult>> exportMetrics =
            context => Export(context, ExportMetricsServiceRequest.Parser, metrics.ProcessMetrics);

        endpoints.MapPost("/v1/traces", exportTraces);
        endpoints.MapPost("/v1/metrics", exportMetrics);

        return endpoints;
    }

    /// <summary>
    /// Reads an OTLP export request in whichever encoding it arrived as, processes it, and answers in that
    /// same encoding — a sender that posted protobuf gets protobuf back, and JSON gets JSON.
    /// </summary>
    private static async Task<IResult> Export<TRequest, TResponse>(
        HttpContext context,
        MessageParser<TRequest> parser,
        Func<TRequest, TResponse> process
    )
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage
    {
        var contentType = context.Request.ContentType;

        if (contentType?.Contains(PROTOBUF_CONTENT_TYPE, StringComparison.OrdinalIgnoreCase) is true)
        {
            using var requestStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(requestStream, context.RequestAborted);

            var response = process(parser.ParseFrom(requestStream.ToArray()));

            using var responseStream = new MemoryStream();
            response.WriteTo(responseStream);

            return Results.Bytes(responseStream.ToArray(), PROTOBUF_CONTENT_TYPE);
        }

        if (context.Request.HasJsonContentType())
        {
            using var reader = new StreamReader(
                context.Request.Body,
                ReadCharset(contentType),
                detectEncodingFromByteOrderMarks: true
            );

            var body = await reader.ReadToEndAsync(context.RequestAborted);

            // OTLP/JSON writes ids as hex, but the protobuf parser decodes every `bytes` field as base64.
            var response = process(parser.ParseJson(OtlpJsonIdNormalizer.NormalizeIds(body)));

            return Results.Text(JsonFormatter.Default.Format(response), "application/json");
        }

        return Results.BadRequest("Unknown content type");
    }

    /// <summary>
    /// The charset the body is in. UTF-8 is both the OTLP default and the fallback for a Content-Type that
    /// won't parse or names an encoding this machine doesn't have — a header NekoTrace can't read is not a
    /// reason to drop telemetry, and a misread body will fail in the parser with a better message.
    /// </summary>
    private static Encoding ReadCharset(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return Encoding.UTF8;
        }

        try
        {
            var charset = MediaTypeHeaderValue.Parse(contentType).Charset;

            return charset.HasValue
                ? Encoding.GetEncoding(charset.Value.Trim('"'))
                : Encoding.UTF8;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
#pragma warning restore CA1031
        {
            return Encoding.UTF8;
        }
    }
}
