namespace NekoTrace.Web.Repositories;

using Google.Protobuf;
using NekoTrace.Web.Configuration;
using System.Collections.Concurrent;

public class TracesRepository : IDisposable
{
    private readonly ConfigurationManager mConfiguration;
    private readonly Timer mTrimTimer;

    private readonly ReaderWriterLockSlim mTracesLock = new();

    private readonly Dictionary<string, Trace> mTracesById = [];
    private readonly ConcurrentDictionary<string, SpanRepository> mSpansByName = [];

    public TracesRepository(ConfigurationManager configuration)
    {
        mConfiguration = configuration;
        mTrimTimer = new Timer(this.mTrimTimer_Tick, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public event Action? TracesChanged;

    public IQueryable<Trace> Traces { get; private set; } =
        Array.Empty<Trace>().AsQueryable();

    public IReadOnlyDictionary<string, SpanRepository> SpanRepositoriesByName => mSpansByName;

    public Trace? TryGetTrace(string id)
    {
        mTracesLock.EnterReadLock();

        try
        {
            return mTracesById.TryGetValue(id, out var trace)
                ? trace
                : null;
        }
        finally
        {
            mTracesLock.ExitReadLock();
        }
    }

    internal Trace GetOrAddTrace(ByteString traceId)
    {
        var stringId = traceId.ToBase64();

        mTracesLock.EnterUpgradeableReadLock();

        try
        {
            if (!mTracesById.TryGetValue(stringId, out var trace))
            {
                mTracesLock.EnterWriteLock();

                if (!mTracesById.TryGetValue(stringId, out trace))
                {
                    trace = mTracesById[stringId] = new Trace() { Id = stringId, Repository = this };
                    this.Traces = mTracesById.Values.ToArray().AsQueryable();
                }

                mTracesLock.ExitWriteLock();
            }

            return trace;
        }
        finally
        {
            mTracesLock.ExitUpgradeableReadLock();
        }
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

    internal void RemoveTrace(Trace trace)
    {
        mTracesLock.EnterWriteLock();

        try
        {
            if (!mTracesById.Remove(trace.Id))
            {
                return;
            }

            this.RemoveTraceSpans(trace);

            this.Traces = mTracesById.Values.ToArray().AsQueryable();
        }
        finally
        {
            mTracesLock.ExitWriteLock();
        }

        this.TracesChanged?.Invoke();
    }

    private void mTrimTimer_Tick(object? _)
    {
        var nekoTraceConfig = new NekoTraceConfiguration();
        mConfiguration.Bind("NekoTrace", nekoTraceConfig);

        var maxSpanAge = nekoTraceConfig?.MaxSpanAge;
        if (maxSpanAge is null)
        {
            return;
        }

        var oldTime = DateTimeOffset.Now.Subtract(maxSpanAge.Value);

        mTracesLock.EnterUpgradeableReadLock();

        try
        {
            var tracesToRemove = mTracesById.Values
                .Where(t => t.Start < oldTime)
                .ToArray();

            if (tracesToRemove.Length is 0)
            {
                return;
            }

            mTracesLock.EnterWriteLock();

            try
            {
                foreach (var oldTrace in tracesToRemove)
                {
                    mTracesById.Remove(oldTrace.Id);

                    this.RemoveTraceSpans(oldTrace);
                }

                this.Traces = mTracesById.Values.ToArray().AsQueryable();
            }
            finally
            {
                mTracesLock.ExitWriteLock();
            }
        }
        finally
        {
            mTracesLock.ExitUpgradeableReadLock();
        }

        this.TracesChanged?.Invoke();
    }

    private void RemoveTraceSpans(Trace oldTrace)
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
    }
}
