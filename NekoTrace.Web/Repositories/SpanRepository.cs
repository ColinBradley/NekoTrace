namespace NekoTrace.Web.Repositories;

using System.Collections.Immutable;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public class SpanRepository
{
    private readonly ReaderWriterLockSlim mLock = new();
    private TimeSpan? mAverageDuration = null;

    public string Name { get; private set; } = string.Empty;

    public ImmutableList<SpanData> Spans { get; private set; } = [];

    public ImmutableList<SpanData> ErrorSpans { get; private set; } = [];

    public bool IsRootSpan => this.Spans.Any(s => s.ParentSpanId is null);

    public TimeSpan MinDuration { get; private set; } = TimeSpan.MaxValue;

    public TimeSpan MaxDuration { get; private set; } = TimeSpan.MinValue;

    public TimeSpan AverageDuration
    {
        get
        {
            mLock.EnterUpgradeableReadLock();

            try
            {
                if (mAverageDuration is null)
                {
                    mLock.EnterWriteLock();

                    try
                    {
                        if (mAverageDuration is null)
                        {
                            var average =
                                this.Spans.Sum(s => s.Duration.TotalMilliseconds)
                                / this.Spans.Count;

                            mAverageDuration = TimeSpan.FromMilliseconds(average);
                        }
                    }
                    finally
                    {
                        mLock.ExitWriteLock();
                    }
                }

                return mAverageDuration.Value;
            }
            finally
            {
                mLock.ExitUpgradeableReadLock();
            }
        }
    }

    internal void AddSpan(SpanData span)
    {
        mLock.EnterWriteLock();

        try
        {
            this.Spans = this.Spans.Add(span);

            if (this.Name is "")
                this.Name = span.Name;

            if (span.StatusCode is StatusCode.Error)
                this.ErrorSpans = this.ErrorSpans.Add(span);

            var duration = span.Duration;
            if (duration < this.MinDuration)
                this.MinDuration = duration;

            if (duration > this.MaxDuration)
                this.MaxDuration = duration;
        }
        finally
        {
            mLock.ExitWriteLock();
        }
    }

    internal void RemoveSpan(SpanData span)
    {
        mLock.EnterWriteLock();

        try
        {
            this.Spans = this.Spans.Remove(span);

            if (span.StatusCode is StatusCode.Error)
                this.ErrorSpans = this.ErrorSpans.Remove(span);

            var duration = span.Duration;
            if (duration == this.MinDuration && this.Spans.Count > 0)
                this.MinDuration = this.Spans.Min(s => s.Duration);

            if (duration == this.MaxDuration && this.Spans.Count > 0)
                this.MaxDuration = this.Spans.Max(s => s.Duration);
        }
        finally
        {
            mLock.ExitWriteLock();
        }
    }
}
