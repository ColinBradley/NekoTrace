namespace InfoCat.Web.Repositories;

using Google.Protobuf;

public class TracesRepository
{
    private readonly ReaderWriterLockSlim mLock = new();

    private readonly Dictionary<string, TraceData> mTracesById = [];

    public IQueryable<TraceData> Traces { get; private set; } = 
        Array.Empty<TraceData>().AsQueryable();

    internal TraceData GetOrAddTrace(ByteString traceId)
    {
        var stringId = traceId.ToBase64();

        mLock.EnterUpgradeableReadLock();

        try
        {
            if (!mTracesById.TryGetValue(stringId, out var trace))
            {
                mLock.EnterWriteLock();
                
                if (!mTracesById.TryGetValue(stringId, out trace))
                {
                    trace = mTracesById[stringId] = new TraceData() { Id = stringId };
                    Traces = mTracesById.Values.ToArray().AsQueryable();
                }

                mLock.ExitWriteLock();
            }

            return trace;
        }
        finally
        {
            mLock.ExitUpgradeableReadLock();
        }
    }

    internal TraceData? TryGetTrace(string id)
    {
        mLock.EnterReadLock();

        try
        {
            return mTracesById.TryGetValue(id, out var trace) 
                ? trace 
                : null;
        }
        finally
        {
            mLock.ExitReadLock();
        }
    }
}
