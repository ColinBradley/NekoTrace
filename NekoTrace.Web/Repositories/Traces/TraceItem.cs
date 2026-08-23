namespace NekoTrace.Web.Repositories.Traces;

using NekoTrace.Web.Utilities;
using System.Collections.Immutable;
using System.Globalization;

public sealed record TraceItem : IDisposable
{
    private readonly BetterReaderWriterLock mLock = new();

    private readonly List<SpanData> mOrderedSpans = [];
    private readonly Dictionary<string, SpanData> mSpansById = new(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, SpanData>? mPublishedSpansById;

    public required string Id { get; init; }

    public required TracesRepository Repository { get; init; }

    /// <summary>
    /// Every span held, ordered by start time.
    /// </summary>
    public ImmutableArray<SpanData> Spans { get; private set; } = [];

    public IReadOnlyDictionary<string, SpanData> SpansById
    {
        get
        {
            using var readLock = mLock.UpgradeableRead();

            if (mPublishedSpansById is null)
            {
                using var writeLock = mLock.Write();

                mPublishedSpansById = new Dictionary<string, SpanData>(mSpansById, StringComparer.Ordinal);
            }

            return mPublishedSpansById;
        }
    }

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

    internal void AddSpan(SpanData span) =>
        this.AddSpans([span]);

    internal void AddSpans(IEnumerable<SpanData> spans)
    {
        using (mLock.Write())
        {
            var added = false;
            foreach (var span in spans)
            {
                added |= this.AddSpanCore(span);
            }

            if (added)
            {
                this.Spans = mOrderedSpans.ToImmutableArray();
                mPublishedSpansById = null;
            }
        }

        this.Repository.OnTraceChanged();
    }

    /// <summary>
    /// Returns true when the span was new.
    /// </summary>
    private bool AddSpanCore(SpanData span)
    {
        // The same span id can arrive more than once — an exporter retrying a batch, or the same trace file
        // uploaded twice. A span is immutable once exported, so the repeat carries nothing new and the copy we
        // already hold wins. Without this the ordered list grows a second entry that SpansById, being keyed,
        // cannot see, leaving the two indexes disagreeing about how many spans the trace has.
        if (!mSpansById.TryAdd(span.Id, span))
        {
            return false;
        }

        mOrderedSpans.Insert(FindInsertIndex(mOrderedSpans, span.StartTime), span);

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

        return true;
    }

    /// <summary>
    /// Where <paramref name="startTime"/> belongs in a list already ordered by start time: one past the last
    /// span that starts no later than it. Landing *after* the run of equal start times is what keeps those
    /// spans in arrival order — an SDK with millisecond clock resolution produces plenty of them, and filing
    /// each new one ahead of its equals would reverse the group.
    /// </summary>
    private static int FindInsertIndex(List<SpanData> ordered, DateTimeOffset startTime)
    {
        var low = 0;
        var high = ordered.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (ordered[middle].StartTime <= startTime)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    public void Dispose()
    {
        mLock.Dispose();
    }
}
