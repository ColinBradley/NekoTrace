namespace NekoTrace.Web.Repositories.Traces;

using Google.Protobuf;
using Google.Protobuf.Collections;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.Utilities;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using System.Collections.Concurrent;
using System.Linq;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class TracesRepository : IDisposable
{
    private readonly ConfigurationManager mConfiguration;
    private readonly Timer mTrimTimer;

    private readonly BetterReaderWriterLock mTracesLock = new();

    private readonly Dictionary<string, TraceItem> mTracesById = [];
    private readonly ConcurrentDictionary<string, SpanRepository> mSpansByName = [];

    public TracesRepository(ConfigurationManager configuration)
    {
        mConfiguration = configuration;
        mTrimTimer = new Timer(this.mTrimTimer_Tick, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public event Action? TracesChanged;

    public IQueryable<TraceItem> Traces { get; private set; } =
        Array.Empty<TraceItem>().AsQueryable();

    public IReadOnlyDictionary<string, SpanRepository> SpanRepositoriesByName => mSpansByName;

    public TraceItem? TryGetTrace(string id)
    {
        using var readLock = mTracesLock.Read();

        return mTracesById.TryGetValue(id, out var trace)
            ? trace
            : null;
    }

    internal TraceItem GetOrAddTrace(ByteString traceId)
    {
        var stringId = traceId.ToBase64();

        using var readLock = mTracesLock.UpgradeableRead();

        if (!mTracesById.TryGetValue(stringId, out var trace))
        {
            using var writeLock = mTracesLock.Write();

            if (!mTracesById.TryGetValue(stringId, out trace))
            {
                trace = mTracesById[stringId] = new TraceItem() { Id = stringId, Repository = this };
                this.Traces = mTracesById.Values.ToArray().AsQueryable();
            }
        }

        return trace;
    }

    internal void AddSpan(SpanData span)
    {
        mSpansByName
            .GetOrAdd(
                span.Name,
                (_) => new SpanRepository()
            )
            .AddSpan(span);
    }

    internal void OnTraceChanged()
    {
        this.TracesChanged?.Invoke();
    }

    internal void RemoveTrace(TraceItem trace)
    {
        using (var writeLock = mTracesLock.Write())
        {
            if (!mTracesById.Remove(trace.Id))
            {
                return;
            }

            this.RemoveTraceSpans(trace);

            this.Traces = mTracesById.Values.ToArray().AsQueryable();
        }

        this.TracesChanged?.Invoke();
    }

    internal ExportTraceServiceResponse ProcessExportTrace(
        ExportTraceServiceRequest request
    )
    {
        foreach (var resourceSpan in request.ResourceSpans)
        {
            var resourceAttributes =
                ConvertAttributes(resourceSpan.Resource.Attributes)
                    .ToArray();

            foreach (var scopeSpan in resourceSpan.ScopeSpans)
            {
                var scopeAttributes =
                    ConvertAttributes(scopeSpan.Scope.Attributes)
                        .Concat(
                            [
                                new("otel.library.name", scopeSpan.Scope.Name),
                                new("otel.library.version", scopeSpan.Scope.Version),
                            ]
                        )
                        .ToArray();

                foreach (var span in scopeSpan.Spans)
                {
                    this.GetOrAddTrace(span.TraceId)
                        .AddSpan(ConvertSpan(span, [.. resourceAttributes, .. scopeAttributes]));
                }
            }
        }

        return new ExportTraceServiceResponse()
        {
            PartialSuccess = new ExportTracePartialSuccess()
            {
                RejectedSpans = 0,
                ErrorMessage = string.Empty,
            },
        };
    }

    private void mTrimTimer_Tick(object? _)
    {
        var nekoTraceConfig = new NekoTraceConfiguration();
        mConfiguration.Bind("NekoTrace", nekoTraceConfig);

        var maxSpanAge = nekoTraceConfig.MaxSpanAge;
        if (maxSpanAge is null)
        {
            return;
        }

        var oldTime = DateTimeOffset.Now.Subtract(maxSpanAge.Value);

        using (mTracesLock.Write())
        {
            var tracesToRemove = mTracesById.Values
                .Where(t => t.Start < oldTime)
                .ToArray();

            if (tracesToRemove.Length is 0)
            {
                return;
            }

            foreach (var oldTrace in tracesToRemove)
            {
                mTracesById.Remove(oldTrace.Id);

                this.RemoveTraceSpans(oldTrace);
            }

            this.Traces = mTracesById.Values.ToArray().AsQueryable();
        }

        this.TracesChanged?.Invoke();
    }

    private void RemoveTraceSpans(TraceItem oldTrace)
    {
        foreach (var oldSpan in oldTrace.Spans)
        {
            if (!mSpansByName.TryGetValue(oldSpan.Name, out var spanRepository))
            {
                continue;
            }

            spanRepository.RemoveSpan(oldSpan);

            if (spanRepository.Spans.Count is 0)
            {
                mSpansByName.TryRemove(new KeyValuePair<string, SpanRepository>(oldSpan.Name, spanRepository));
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        mTrimTimer.Dispose();
        mConfiguration.Dispose();
        mTracesLock.Dispose();
    }

    private static SpanData ConvertSpan(
       OpenTelemetry.Proto.Trace.V1.Span span,
       IEnumerable<KeyValuePair<string, object?>> extraAttributes
   )
    {
        return new SpanData()
        {
            TraceId = span.TraceId.ToBase64(),
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
