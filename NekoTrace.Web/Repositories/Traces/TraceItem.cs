namespace NekoTrace.Web.Repositories.Traces;

using NekoTrace.Web.Utilities;
using System.Collections.Immutable;
using System.Globalization;

public sealed record TraceItem : IDisposable
{
    private readonly BetterReaderWriterLock mLock = new();

    public required string Id { get; init; }

    public required TracesRepository Repository { get; init; }

    public ImmutableList<SpanData> Spans { get; private set; } = [];

    public ImmutableDictionary<string, SpanData> SpansById { get; private set; } = ImmutableDictionary.Create<string, SpanData>(StringComparer.Ordinal);

    public SpanData? RootSpan { get; private set; }

    public DateTimeOffset Start { get; private set; } = DateTimeOffset.MaxValue;

    public DateTimeOffset End { get; private set; } = DateTimeOffset.MinValue;

    public TimeSpan Duration { get; private set; }

    public bool HasError { get; private set; }

    /// <summary>
    /// A root span attribute rendered for display, or null when the root span hasn't arrived or carries no
    /// such attribute. Note that null means *absent*: every value that is present renders as something.
    /// </summary>
    public string? TryGetRootSpanAttribute(string name)
    {
        return this.RootSpan?.Attributes.TryGetValue(name, out var value) is true
            ? value switch
            {
                null => null,
                string stringValue => stringValue,
                bool v => v.ToString(),
                // OTLP's IntValue is a long, so an integer attribute arrives boxed as one and never
                // matched the int case that used to be here.
                long v => v.ToString(CultureInfo.InvariantCulture),
                int v => v.ToString(CultureInfo.InvariantCulture),
                double v => v.ToString(CultureInfo.InvariantCulture),
                // Anything else — an OTLP ArrayValue, a KvlistValue — renders the way the rest of the UI
                // renders attributes, rather than silently blanking the column.
                _ => value.ToString(),
            }
            : null;
    }

    internal void AddSpan(SpanData span)
    {
        using (mLock.Write())
        {
            this.AddSpanCore(span);
        }

        this.Repository.OnTraceChanged();
    }

    internal void AddSpans(IEnumerable<SpanData> spans)
    {
        using (mLock.Write())
        {
            foreach (var span in spans)
            {
                this.AddSpanCore(span);
            }
        }

        this.Repository.OnTraceChanged();
    }

    private void AddSpanCore(SpanData span)
    {
        // The same span id can arrive more than once — an exporter retrying a batch, or the same trace file
        // uploaded twice. A span is immutable once exported, so the repeat carries nothing new and the copy we
        // already hold wins. Without this the ordered list grows a second entry that SpansById, being keyed,
        // cannot see, leaving the two indexes disagreeing about how many spans the trace has.
        if (this.SpansById.ContainsKey(span.Id))
        {
            return;
        }

        // Insert *after* the last span that starts no later than this one, hence the + 1. That also covers
        // the no-match case: FindLastIndex returns -1 when this span starts before everything held, which
        // lands it at index 0. The comparison includes equality so that spans sharing a start time keep
        // their arrival order — an SDK with millisecond clock resolution produces plenty of those, and a
        // strict < would file each one ahead of its predecessors, reversing the group.
        var insertIndex = this.Spans.FindLastIndex(s => s.StartTime <= span.StartTime);

        this.Spans = this.Spans.Insert(insertIndex + 1, span);
        this.SpansById = this.SpansById.SetItem(span.Id, span);

        this.HasError =
            this.HasError
            || span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error;

        if (string.IsNullOrEmpty(span.ParentSpanId))
        {
            this.RootSpan = span;
        }

        var durationChanged = false;
        if (span.StartTime < this.Start)
        {
            this.Start = span.StartTime;
            durationChanged = true;
        }

        if (span.EndTime > this.End)
        {
            this.End = span.EndTime;
            durationChanged = true;
        }

        if (durationChanged)
        {
            this.Duration = this.End - this.Start;
        }

        this.Repository.AddSpan(span);
    }

    public void Dispose()
    {
        mLock.Dispose();
    }
}
