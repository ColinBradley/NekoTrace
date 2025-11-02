namespace NekoTrace.Web.Repositories.Traces;

using Google.Protobuf;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.Utilities;
using System.Collections.Concurrent;

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
}
