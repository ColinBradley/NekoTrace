namespace NekoTrace.Web.Analysis;

using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

/// <summary>
/// A fixed size report on one trace, whatever the trace's size. The first thing a caller should ask for.
/// </summary>
/// <remarks>
/// This deliberately carries only what is unusual — no routine spans, no attribute dumps — because its job is
/// to let a reader decide which of the other views to ask for next. See <c>docs/ai-access.md</c>.
/// </remarks>
internal sealed record TraceSummary
{
    private const string EXCEPTION_EVENT_NAME = "exception";

    public required string TraceId { get; init; }

    public required string? RootSpanName { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required double DurationMs { get; init; }

    public required int SpanCount { get; init; }

    public required int DistinctSpanNames { get; init; }

    public required ImmutableArray<string> Services { get; init; }

    /// <summary>More than one means the trace is a forest — see <see cref="OrphanCount"/>.</summary>
    public required int ForestTopCount { get; init; }

    public required int OrphanCount { get; init; }

    public required int CycleCount { get; init; }

    public required int ErrorSpanCount { get; init; }

    /// <summary>
    /// Spans carrying an exception event but not marked as failed. Recorded and handled, in other words —
    /// invisible to every error count, and regularly the thing that explains a slow trace with no errors in it.
    /// </summary>
    public required int HandledExceptionCount { get; init; }

    /// <summary>Errors folded by what went wrong, so a wall of identical 404s is one line.</summary>
    public required ImmutableArray<ErrorClass> ErrorClasses { get; init; }

    /// <summary>Whole spans, attributes and events included. Spread across <see cref="ErrorClasses"/>.</summary>
    public required ImmutableArray<SpanData> ErrorSamples { get; init; }

    public required ImmutableArray<NameCost> TimeByName { get; init; }

    public required ImmutableArray<NameCost> Outliers { get; init; }

    public required int MaxDepth { get; init; }

    public required int WidestFanOut { get; init; }

    public required string? WidestFanOutName { get; init; }

    public required string? WidestFanOutSpanId { get; init; }

    /// <summary>Names that appear more than once on a single root to leaf path.</summary>
    public required ImmutableArray<string> RecursiveNames { get; init; }

    /// <summary>Stretches inside the trace's window during which no span at all was running.</summary>
    public required ImmutableArray<DeadTime> Gaps { get; init; }

    public required AttributeSummary Attributes { get; init; }

    public static TraceSummary Build(TraceItem trace, SpanTree tree, TraceSummaryOptions options)
    {
        var spans = trace.Spans;

        var byName = new Dictionary<string, List<SpanNode>>(StringComparer.Ordinal);
        var widest = (SpanNode?)null;

        foreach (var node in tree.EnumerateDepthFirst())
        {
            if (!byName.TryGetValue(node.Span.Name, out var group))
            {
                byName[node.Span.Name] = group = [];
            }

            group.Add(node);

            if (node.Children.Length > (widest?.Children.Length ?? 0))
            {
                widest = node;
            }
        }

        var costs = BuildCosts(byName, trace.Duration.TotalMilliseconds);
        var errorSpans = FindErrors(spans, options.ErrorExclusions);
        var errorClasses = ClassifyErrors(errorSpans);

        return new TraceSummary()
        {
            TraceId = trace.Id,
            RootSpanName = trace.RootSpan?.Name,
            Start = trace.Start,
            DurationMs = trace.Duration.TotalMilliseconds,
            SpanCount = spans.Count,
            DistinctSpanNames = byName.Count,
            Services =
            [
                .. spans
                    .Select(span => span.TryGetAttributeValue("service.name")?.ToString())
                    .OfType<string>()
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ],
            ForestTopCount = tree.Roots.Length,
            OrphanCount = tree.OrphanCount,
            CycleCount = tree.CycleCount,
            ErrorSpanCount = errorSpans.Count,
            HandledExceptionCount = spans.Count(span =>
                span.StatusCode is not OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error
                && span.Events.Any(spanEvent =>
                    string.Equals(spanEvent.Name, EXCEPTION_EVENT_NAME, StringComparison.Ordinal)
                )
            ),
            ErrorClasses = errorClasses,
            ErrorSamples = TakeErrorSamples(errorClasses, options.ErrorLimit),
            TimeByName =
            [
                .. costs.OrderByDescending(cost => cost.SelfMs).Take(options.TopCount),
            ],
            Outliers =
            [
                .. costs
                    .Where(cost =>
                        cost.SelfDurations.Count >= options.OutlierMinimumSamples
                        && cost.SelfDurations.TailRatio >= options.OutlierTailRatio
                    )
                    .OrderByDescending(cost => cost.SelfDurations.MaxMs)
                    .Take(options.TopCount),
            ],
            MaxDepth = tree.MaxDepth,
            WidestFanOut = widest?.Children.Length ?? 0,
            WidestFanOutName = widest?.Span.Name,
            WidestFanOutSpanId = widest?.Span.Id,
            RecursiveNames = FindRecursiveNames(tree),
            Gaps = FindDeadTime(spans, options.MinimumGapMs, options.TopCount),
            Attributes = AttributeSummary.Build(spans),
        };
    }

    private static List<NameCost> BuildCosts(
        Dictionary<string, List<SpanNode>> byName,
        double traceDurationMs
    )
    {
        var costs = new List<NameCost>(byName.Count);

        foreach (var (name, nodes) in byName)
        {
            var durations = new List<double>(nodes.Count);
            var selfDurations = new List<double>(nodes.Count);
            var selfMs = 0d;
            var errorCount = 0;
            var slowest = nodes[0];

            foreach (var node in nodes)
            {
                durations.Add(node.DurationMs);
                selfDurations.Add(node.SelfTimeMs);
                selfMs += node.SelfTimeMs;

                if (node.HasError)
                {
                    errorCount++;
                }

                if (node.SelfTimeMs > slowest.SelfTimeMs)
                {
                    slowest = node;
                }
            }

            costs.Add(
                new NameCost()
                {
                    Name = name,
                    Durations = DurationStatistics.From(durations),
                    SelfDurations = DurationStatistics.From(selfDurations),
                    SelfMs = selfMs,
                    ErrorCount = errorCount,
                    SlowestSpanId = slowest.Span.Id,
                    // Against the trace's wall clock rather than the summed duration of every span, which in
                    // a parallel trace runs to many times the time that actually elapsed.
                    SelfPercent = traceDurationMs > 0 ? 100 * selfMs / traceDurationMs : 0,
                }
            );
        }

        return costs;
    }

    /// <summary>
    /// An exception attribute from wherever the SDK put it. The OpenTelemetry convention is a span event
    /// named <c>exception</c> carrying <c>exception.type</c>, <c>exception.message</c> and
    /// <c>exception.stacktrace</c>, but plenty of instrumentation writes them as span attributes instead —
    /// including whatever produced the traces in <c>TestTraces/</c>. Classifying on only one of the two
    /// misses every error raised by the other.
    /// </summary>
    private static string? TryGetExceptionAttribute(SpanData span, string key)
    {
        foreach (var spanEvent in span.Events)
        {
            if (
                string.Equals(spanEvent.Name, EXCEPTION_EVENT_NAME, StringComparison.Ordinal)
                && spanEvent.Attributes.TryGetValue(key, out var value)
                && value?.ToString() is { Length: > 0 } text
            )
            {
                return text;
            }
        }

        return null;
    }

    private static List<SpanData> FindErrors(IEnumerable<SpanData> spans, AttributeMatcher exclusions) =>
        [
            .. spans.Where(span =>
                span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error
                && !exclusions.Matches(span)
            ),
        ];

    /// <summary>
    /// Folds errors by what actually went wrong. Without this a trace where one endpoint 404s four thousand
    /// times spends the whole error budget saying so — and ASP.NET reports plenty of ordinary 4xx responses
    /// as errors, so that is the common case rather than the odd one.
    /// </summary>
    private static ImmutableArray<ErrorClass> ClassifyErrors(List<SpanData> errorSpans)
    {
        var classes = new Dictionary<(string, string?, string?), List<SpanData>>();

        foreach (var span in errorSpans)
        {
            var key = (
                span.Name,
                span.TryGetAttributeValue("error.type")?.ToString()
                    ?? TryGetExceptionAttribute(span, "exception.type"),
                span.TryGetAttributeValue("http.response.status_code")?.ToString()
            );

            if (!classes.TryGetValue(key, out var members))
            {
                classes[key] = members = [];
            }

            members.Add(span);
        }

        return
        [
            .. classes
                .Select(entry => new ErrorClass()
                {
                    SpanName = entry.Key.Item1,
                    ErrorType = entry.Key.Item2,
                    HttpStatusCode = entry.Key.Item3,
                    Count = entry.Value.Count,
                    Message = string.IsNullOrEmpty(entry.Value[0].StatusMessage)
                        ? TryGetExceptionAttribute(entry.Value[0], "exception.message")
                        : entry.Value[0].StatusMessage,
                    Members = [.. entry.Value],
                })
                .OrderByDescending(errorClass => errorClass.Count)
                .ThenBy(errorClass => errorClass.SpanName, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Fills the sample budget a class at a time rather than a span at a time, so that ten slots show ten
    /// different problems instead of ten copies of the loudest one.
    /// </summary>
    private static ImmutableArray<SpanData> TakeErrorSamples(
        ImmutableArray<ErrorClass> classes,
        int limit
    )
    {
        var samples = ImmutableArray.CreateBuilder<SpanData>();

        for (var round = 0; samples.Count < limit; round++)
        {
            var exhausted = true;

            foreach (var errorClass in classes)
            {
                if (round >= errorClass.Members.Length)
                {
                    continue;
                }

                exhausted = false;
                samples.Add(errorClass.Members[round]);

                if (samples.Count == limit)
                {
                    break;
                }
            }

            if (exhausted)
            {
                break;
            }
        }

        return samples.ToImmutable();
    }

    /// <summary>
    /// Names appearing twice on one root to leaf path. Worth stating once: the 230k span trace in
    /// <c>TestTraces/</c> repeats a four name cycle down 25 levels, and a reader who is not told will
    /// re-derive it from the profile's paths instead.
    /// </summary>
    private static ImmutableArray<string> FindRecursiveNames(SpanTree tree)
    {
        var recursive = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in tree.EnumerateDepthFirst())
        {
            if (recursive.Contains(node.Span.Name))
            {
                continue;
            }

            for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (string.Equals(ancestor.Span.Name, node.Span.Name, StringComparison.Ordinal))
                {
                    recursive.Add(node.Span.Name);

                    break;
                }
            }
        }

        return [.. recursive.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The complement of the union of every span's interval, within the trace's own window. Frequently the
    /// answer on its own: time nothing was recorded for is time the trace cannot account for.
    /// </summary>
    private static ImmutableArray<DeadTime> FindDeadTime(
        IEnumerable<SpanData> spans,
        double minimumMs,
        int limit
    )
    {
        var intervals = spans
            .Select(span => (Start: span.StartTimeMs, End: span.EndTimeMs))
            .OrderBy(interval => interval.Start)
            .ToArray();

        if (intervals.Length is 0)
        {
            return [];
        }

        var gaps = new List<DeadTime>();
        var traceStart = intervals[0].Start;
        var cursor = intervals[0].End;

        foreach (var interval in intervals)
        {
            if (interval.Start - cursor >= minimumMs)
            {
                gaps.Add(
                    new DeadTime()
                    {
                        StartMs = cursor - traceStart,
                        DurationMs = interval.Start - cursor,
                    }
                );
            }

            if (interval.End > cursor)
            {
                cursor = interval.End;
            }
        }

        return [.. gaps.OrderByDescending(gap => gap.DurationMs).Take(limit)];
    }
}
