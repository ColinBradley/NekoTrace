using Grpc.Core;
using InfoCat.Web.Repositories;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using StatusCode = OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode;

namespace InfoCat.Web.GrpcServices;

public class TraceServiceImplementation : TraceService.TraceServiceBase
{
    private readonly TracesRepository mTraces;

    public TraceServiceImplementation(TracesRepository traces)
    {
        mTraces = traces;
    }

    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context
    )
    {
        foreach (var resourceSpan in request.ResourceSpans)
        {
            foreach (var scopeSpan in resourceSpan.ScopeSpans)
            {
                foreach (var span in scopeSpan.Spans)
                {
                    mTraces
                        .GetOrAddTrace(span.TraceId)
                        .AddSpan(ConvertSpan(span));
                }
            }
        }

        return Task.FromResult(
            new ExportTraceServiceResponse()
            {
                PartialSuccess = new ExportTracePartialSuccess()
                {
                    RejectedSpans = 0,
                    ErrorMessage = string.Empty,
                },
            }
        );
    }

    private static SpanData ConvertSpan(OpenTelemetry.Proto.Trace.V1.Span span)
    {
        return new SpanData()
        {
            Id = span.SpanId.ToBase64(),
            ParentSpanId = span.ParentSpanId.ToBase64(),
            Name = span.Name,
            Kind = span.Kind,
            Attributes = span.Attributes.ToDictionary(e => e.Key, e => ConvertAnyValue(e.Value)),
            StartTime = TimeFromUnixNano(span.StartTimeUnixNano),
            EndTime = TimeFromUnixNano(span.EndTimeUnixNano),
            StatusCode = span.Status?.Code ?? StatusCode.Unset,
            StatusMessage = span.Status?.Message,
            TraceState = span.TraceState,
            Events =
            [
                .. span.Events.Select(e => new SpanEvent()
                {
                    Name = e.Name,
                    Time = TimeFromUnixNano(e.TimeUnixNano),
                    Attributes = e.Attributes.ToDictionary(e => e.Key, e => e.Value),
                }),
            ],
            Links =
            [
                .. span.Links.Select(l => l.Attributes.ToDictionary(e => e.Key, e => ConvertAnyValue(e.Value))),
            ],
        };
    }

    private static DateTimeOffset TimeFromUnixNano(ulong time) =>
        DateTimeOffset.UnixEpoch.AddTicks(Convert.ToInt64(time / 100));

    private static object ConvertAnyValue(AnyValue value)
    {
        if (value.HasStringValue)
            return value.StringValue;

        if (value.HasBoolValue)
            return value.BoolValue;

        if (value.HasIntValue)
            return value.IntValue;

        if (value.HasDoubleValue)
            return value.DoubleValue;

        throw new Exception("Unknown content");
    }
}
