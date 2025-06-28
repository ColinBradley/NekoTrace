namespace InfoCat.Web.Repositories;
using System.Collections.Immutable;

public sealed record Trace
{
    private readonly ReaderWriterLockSlim mLock = new();

    public required string Id { get; init; }

    public ImmutableArray<SpanData> Spans { get; private set; } = [];
    
    public SpanData? RootSpan { get; private set; }

    internal void AddSpan(SpanData span)
    {
        mLock.EnterWriteLock();

        this.Spans = [..this.Spans.Add(span).OrderBy(s => s.StartTime)];
        this.RootSpan = this.Spans.FirstOrDefault(s => s.ParentSpanId is null) ?? this.Spans.First()!;
        
        mLock.ExitWriteLock();
    }
}