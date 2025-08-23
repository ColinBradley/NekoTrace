namespace NekoTrace.Web.Repositories;

using Google.Protobuf;
using System.Collections.Concurrent;

public class TracesRepository
{
    private readonly ReaderWriterLockSlim mTracesLock = new();

    private readonly Dictionary<string, Trace> mTracesById = [];
    private readonly ConcurrentDictionary<string, SpanRepository> mSpansByName = [];

    public event Action<string>? TracesChanged;

    public IQueryable<Trace> Traces { get; private set; } =
        Array.Empty<Trace>().AsQueryable();

    public IEnumerable<SpanRepository> SpanRepositories =>
        mSpansByName.Values;

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

    internal Trace? TryGetTrace(string id)
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

    internal void AddSpan(SpanData span)
    {
        mSpansByName
            .GetOrAdd(
                span.Name, 
                (_) => new SpanRepository()
            )
            .AddSpan(span);
    }

    internal void OnTraceChanged(Trace trace)
    {
        this.TracesChanged?.Invoke(trace.Id);
    }
}
