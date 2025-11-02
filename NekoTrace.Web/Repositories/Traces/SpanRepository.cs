namespace NekoTrace.Web.Repositories.Traces;

using NekoTrace.Web.Utilities;
using System.Collections.Immutable;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed class SpanRepository : IDisposable
{
    private readonly BetterReaderWriterLock mLock = new();
    private TimeSpan? mAverageDuration;

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
            using var readLock = mLock.UpgradeableRead();

            if (mAverageDuration is null)
            {
                using var writeLock = mLock.Write();

#pragma warning disable CA1508 // Avoid dead conditional code
                if (mAverageDuration is null)
#pragma warning restore CA1508
                {
                    var average =
                        this.Spans.Sum(s => s.Duration.TotalMilliseconds)
                        / this.Spans.Count;

                    mAverageDuration = TimeSpan.FromMilliseconds(average);
                }
            }

            return mAverageDuration.Value;
        }
    }

    internal void AddSpan(SpanData span)
    {
        using var writeLock = mLock.Write();

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

    internal void RemoveSpan(SpanData span)
    {
        using var writeLock = mLock.Write();

        this.Spans = this.Spans.Remove(span);

        if (span.StatusCode is StatusCode.Error)
            this.ErrorSpans = this.ErrorSpans.Remove(span);

        var duration = span.Duration;
        if (duration == this.MinDuration && this.Spans.Count > 0)
            this.MinDuration = this.Spans.Min(s => s.Duration);

        if (duration == this.MaxDuration && this.Spans.Count > 0)
            this.MaxDuration = this.Spans.Max(s => s.Duration);
    }

    public void Dispose()
    {
        mLock.Dispose();
    }
}
