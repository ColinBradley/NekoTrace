using Google.Protobuf.Collections;
using Grpc.Core;
using NekoTrace.Web.Repositories;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using StatusCode = OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode;

namespace NekoTrace.Web.GrpcServices;

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
            var resourceAttributes = ConvertAttributes(resourceSpan.Resource.Attributes).ToArray();

            foreach (var scopeSpan in resourceSpan.ScopeSpans)
            {
                var scopeAttributes = ConvertAttributes(scopeSpan.Scope.Attributes)
                    .Concat(
                        [
                            new("otel.library.name", scopeSpan.Scope.Name),
                            new("otel.library.version", scopeSpan.Scope.Version),
                        ]
                    )
                    .ToArray();

                foreach (var span in scopeSpan.Spans)
                {
                    mTraces
                        .GetOrAddTrace(span.TraceId)
                        .AddSpan(ConvertSpan(span, [.. resourceAttributes, .. scopeAttributes]));
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

    private static SpanData ConvertSpan(
        OpenTelemetry.Proto.Trace.V1.Span span,
        IEnumerable<KeyValuePair<string, object?>> extraAttributes
    )
    {
        return new SpanData()
        {
            Id = span.SpanId.ToBase64(),
            ParentSpanId = span.ParentSpanId.IsEmpty ? null : span.ParentSpanId.ToBase64(),
            Name = span.Name,
            Kind = span.Kind,
            Attributes = new([.. ConvertAttributes(span.Attributes), .. extraAttributes]),
            StartTime = TimeFromUnixNano(span.StartTimeUnixNano),
            StartTimeMs = span.StartTimeUnixNano / 1_000_000.0,
            EndTime = TimeFromUnixNano(span.EndTimeUnixNano),
            EndTimeMs = span.EndTimeUnixNano / 1_000_000.0,
            StatusCode = span.Status?.Code ?? StatusCode.Unset,
            StatusMessage = span.Status?.Message,
            TraceState = span.TraceState,
            Events =
            [
                .. span.Events.Select(e => new SpanEvent()
                {
                    Name = e.Name,
                    Time = TimeFromUnixNano(e.TimeUnixNano),
                    Attributes = new(ConvertAttributes(e.Attributes)),
                }),
            ],
            Links =
            [
                .. span.Links.Select(l =>
                    l.Attributes.ToDictionary(e => e.Key, e => ConvertAnyValue(e.Value))
                ),
            ],
        };
    }

    private static DateTimeOffset TimeFromUnixNano(ulong time) =>
        DateTimeOffset.UnixEpoch.AddTicks(Convert.ToInt64(time / 100));

    private static object? ConvertAnyValue(AnyValue value)
    {
        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.None => null,
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue,
            AnyValue.ValueOneofCase.KvlistValue => value.KvlistValue,
            AnyValue.ValueOneofCase.BytesValue => value.BytesValue,
            _ => throw new Exception("Unknown content"),
        };
    }

    private static IEnumerable<KeyValuePair<string, object?>> ConvertAttributes(
        RepeatedField<KeyValue> attributes
    ) => attributes.Select(e => new KeyValuePair<string, object?>(e.Key, ConvertAnyValue(e.Value)));
}
