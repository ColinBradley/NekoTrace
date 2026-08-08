namespace NekoTrace.Tests.TestData;

using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;

/// <summary>
/// Builders for the OTLP protobuf messages an exporter would send. Spans are returned mutable, so a test that
/// needs an attribute or an event adds it to the message rather than growing another parameter here.
/// </summary>
internal static class Otlp
{
    // Ids from the W3C Trace Context examples, so they are recognisably well formed rather than "aaaa…".
    public const string TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736";
    public const string ROOT_SPAN_ID = "00f067aa0ba902b7";
    public const string CHILD_SPAN_ID = "1234567890abcdef";
    public const string OTHER_TRACE_ID = "0af7651916cd43dd8448eb211c80319c";

    /// <summary>Times are offsets in milliseconds from here, keeping the arithmetic in tests readable.</summary>
    public const long ORIGIN_UNIX_MS = 1_775_000_000_000;

    public static readonly DateTimeOffset ORIGIN =
        DateTimeOffset.FromUnixTimeMilliseconds(ORIGIN_UNIX_MS);

    public static ByteString Bytes(string hex) => ByteString.CopyFrom(Convert.FromHexString(hex));

    public static OtlpSpan Span(
        string spanId = ROOT_SPAN_ID,
        string? parentSpanId = null,
        string traceId = TRACE_ID,
        string name = "span",
        long startMs = 0,
        long durationMs = 10,
        StatusCode status = StatusCode.Unset,
        SpanKind kind = SpanKind.Internal
    )
    {
        var span = new OtlpSpan()
        {
            TraceId = Bytes(traceId),
            SpanId = Bytes(spanId),
            Name = name,
            Kind = kind,
            StartTimeUnixNano = UnixNano(startMs),
            EndTimeUnixNano = UnixNano(startMs + durationMs),
            Status = new Status() { Code = status },
        };

        if (parentSpanId is not null)
        {
            span.ParentSpanId = Bytes(parentSpanId);
        }

        return span;
    }

    public static ExportTraceServiceRequest Request(params OtlpSpan[] spans) =>
        Request(resourceAttributes: null, scopeAttributes: null, spans);

    public static ExportTraceServiceRequest Request(
        IEnumerable<KeyValue>? resourceAttributes,
        IEnumerable<KeyValue>? scopeAttributes,
        params OtlpSpan[] spans
    )
    {
        var resource = new Resource();
        if (resourceAttributes is not null)
        {
            resource.Attributes.AddRange(resourceAttributes);
        }

        var scope = new InstrumentationScope() { Name = "test.scope", Version = "1.0.0" };
        if (scopeAttributes is not null)
        {
            scope.Attributes.AddRange(scopeAttributes);
        }

        var scopeSpans = new ScopeSpans() { Scope = scope };
        scopeSpans.Spans.AddRange(spans);

        var resourceSpans = new ResourceSpans() { Resource = resource };
        resourceSpans.ScopeSpans.Add(scopeSpans);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpans);

        return request;
    }

    public static KeyValue Attribute(string key, string value) =>
        new() { Key = key, Value = new AnyValue() { StringValue = value } };

    public static KeyValue Attribute(string key, long value) =>
        new() { Key = key, Value = new AnyValue() { IntValue = value } };

    public static KeyValue Attribute(string key, bool value) =>
        new() { Key = key, Value = new AnyValue() { BoolValue = value } };

    public static ExportMetricsServiceRequest GaugeRequest(
        string metricName = "test.gauge",
        double value = 42
    )
    {
        var metric = new Metric() { Name = metricName, Description = "A gauge", Gauge = new Gauge() };
        metric.Gauge.DataPoints.Add(
            new NumberDataPoint() { TimeUnixNano = UnixNano(0), AsDouble = value }
        );

        var scopeMetrics = new ScopeMetrics()
        {
            Scope = new InstrumentationScope() { Name = "test.scope" },
        };
        scopeMetrics.Metrics.Add(metric);

        var resource = new Resource();
        resource.Attributes.Add(Attribute("service.name", "test-service"));

        var resourceMetrics = new ResourceMetrics() { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scopeMetrics);

        var request = new ExportMetricsServiceRequest();
        request.ResourceMetrics.Add(resourceMetrics);

        return request;
    }

    private static ulong UnixNano(long offsetMs) =>
        (ulong)(ORIGIN_UNIX_MS + offsetMs) * 1_000_000ul;
}
