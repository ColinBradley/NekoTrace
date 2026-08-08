namespace NekoTrace.Tests.TestData;

using Microsoft.Extensions.Configuration;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

/// <summary>
/// Constructors for the storage-side types, for tests that are about what the repositories do with a span
/// rather than about how one is decoded. Times are milliseconds from <see cref="Otlp.ORIGIN"/>.
/// </summary>
internal static class Fake
{
    public static SpanData Span(
        string id = Otlp.ROOT_SPAN_ID,
        string? parentSpanId = null,
        string traceId = Otlp.TRACE_ID,
        string name = "span",
        long startMs = 0,
        long durationMs = 10,
        StatusCode status = StatusCode.Unset,
        Dictionary<string, object?>? attributes = null
    ) =>
        new()
        {
            Id = id,
            TraceId = traceId,
            ParentSpanId = parentSpanId,
            Name = name,
            Kind = SpanKind.Internal,
            Attributes = attributes ?? [],
            StartTime = Otlp.ORIGIN.AddMilliseconds(startMs),
            StartTimeMs = Otlp.ORIGIN_UNIX_MS + startMs,
            EndTime = Otlp.ORIGIN.AddMilliseconds(startMs + durationMs),
            EndTimeMs = Otlp.ORIGIN_UNIX_MS + startMs + durationMs,
            StatusCode = status,
            StatusMessage = null,
            TraceState = null,
            Events = [],
            Links = [],
        };

    /// <summary>
    /// A repository backed by in-memory configuration. Keys are the full path, so
    /// <c>"NekoTrace:TraceIngestFilter"</c> rather than just the property name.
    /// </summary>
#pragma warning disable CA2000 // Dispose objects before losing scope
    // TracesRepository.Dispose disposes the ConfigurationManager it was given, so ownership passes with it.
    public static TracesRepository TracesRepository(params (string Key, string? Value)[] configuration)
    {
        var configurationManager = new ConfigurationManager();

        configurationManager.AddInMemoryCollection(Settings(configuration));

        return new TracesRepository(configurationManager);
    }
#pragma warning restore CA2000

    /// <summary>
    /// Configuration for the consumers that take an <see cref="IConfiguration"/> and don't own it. Returned
    /// as the interface deliberately: an in-memory provider holds nothing that needs disposing, and the
    /// callers here have nowhere sensible to dispose it from.
    /// </summary>
    public static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(Settings(settings)).Build();

    private static IEnumerable<KeyValuePair<string, string?>> Settings(
        (string Key, string? Value)[] settings
    ) =>
        settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value));

    public static TraceSerializableData TraceFile(string id, params SpanData[] spans) =>
        new()
        {
            Version = TraceSerializableData.CURRENT_VERSION,
            Id = id,
            Spans = [.. spans],
        };

    /// <summary>Every id in <paramref name="file"/> rewritten as base64, as builds before hex wrote them.</summary>
    public static TraceSerializableData AsLegacyBase64(TraceSerializableData file) =>
        new()
        {
            Version = TraceSerializableData.LEGACY_VERSION,
            Id = ToBase64(file.Id),
            Spans =
            [
                .. file.Spans.Select(span => span with
                {
                    Id = ToBase64(span.Id),
                    TraceId = ToBase64(span.TraceId),
                    ParentSpanId = span.ParentSpanId is null ? null : ToBase64(span.ParentSpanId),
                }),
            ],
        };

    public static string ToBase64(string hexId) =>
        Convert.ToBase64String(Convert.FromHexString(hexId));

    public static string[] SpanIds(TraceItem trace) =>
        [.. trace.Spans.Select(span => span.Id)];
}
