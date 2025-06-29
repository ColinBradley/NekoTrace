namespace InfoCat.Web.Repositories;

using System.Collections.Immutable;

public sealed record Trace
{
    private readonly ReaderWriterLockSlim mLock = new();
    
    public required string Id { get; init; }

    public required TracesRepository Repository { get; init; }

    public ImmutableList<SpanData> Spans { get; private set; } = [];

    public SpanData? RootSpan { get; private set; }

    public DateTimeOffset Start { get; private set; } = DateTimeOffset.MaxValue;

    public DateTimeOffset End { get; private set; } = DateTimeOffset.MinValue;

    public TimeSpan Duration { get; private set; }

    internal void AddSpan(SpanData span)
    {
        mLock.EnterWriteLock();

        var insertIndex = this.Spans.FindIndex(s => s.StartTime >= span.StartTime);
        
        this.Spans = insertIndex >= 0 
            ? this.Spans.Insert(insertIndex, span) 
            : this.Spans.Add(span);

        if (string.IsNullOrEmpty(span.ParentSpanId))
        {
            this.RootSpan = span;
        }

        var durationChanged = false;
        if (span.StartTime < Start)
        {
            this.Start = span.StartTime;
            durationChanged = true;
        }

        if (span.EndTime > End)
        {
            this.End = span.EndTime;
            durationChanged = true;
        }

        if (durationChanged)
        {
            this.Duration = this.End - this.Start;
        }

        mLock.ExitWriteLock();

        this.Repository.OnTraceChanged(this);
    }
}
