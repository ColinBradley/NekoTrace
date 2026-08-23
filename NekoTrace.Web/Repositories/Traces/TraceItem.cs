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

    internal void AddSpan(SpanData span) =>
        this.AddSpans([span]);

    internal void AddSpans(IEnumerable<SpanData> spans)
    {
        using (mLock.Write())
        {
            // Builders so a batch pays one tree rebuild instead of one per span. Both round-trips are O(1),
            // so the single-span path loses nothing, and the batch only becomes visible to readers once it
            // is whole.
            var ordered = this.Spans.ToBuilder();
            var byId = this.SpansById.ToBuilder();

            foreach (var span in spans)
            {
                this.AddSpanCore(ordered, byId, span);
            }

            this.Spans = ordered.ToImmutable();
            this.SpansById = byId.ToImmutable();
        }

        this.Repository.OnTraceChanged();
    }

    private void AddSpanCore(
        ImmutableList<SpanData>.Builder ordered,
        ImmutableDictionary<string, SpanData>.Builder byId,
        SpanData span
    )
    {
        // The same span id can arrive more than once — an exporter retrying a batch, or the same trace file
        // uploaded twice. A span is immutable once exported, so the repeat carries nothing new and the copy we
        // already hold wins. Without this the ordered list grows a second entry that SpansById, being keyed,
        // cannot see, leaving the two indexes disagreeing about how many spans the trace has.
        if (byId.ContainsKey(span.Id))
        {
            return;
        }

        ordered.Insert(FindInsertIndex(ordered, span.StartTime), span);
        byId.Add(span.Id, span);

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

    /// <summary>
    /// Where <paramref name="startTime"/> belongs in a list already ordered by start time: one past the last
    /// span that starts no later than it. Landing *after* the run of equal start times is what keeps those
    /// spans in arrival order — an SDK with millisecond clock resolution produces plenty of them, and filing
    /// each new one ahead of its equals would reverse the group.
    /// </summary>
    private static int FindInsertIndex(ImmutableList<SpanData>.Builder ordered, DateTimeOffset startTime)
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
