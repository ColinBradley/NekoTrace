namespace NekoTrace.Web.Mcp;

using ModelContextProtocol.Server;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Formatting;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using System.ComponentModel;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;

/// <summary>
/// The MCP surface, mounted in process at <c>/mcp</c>.
/// </summary>
/// <remarks>
/// <para>
/// Six tools with distinct names rather than one tool with a mode parameter, because models choose between
/// names far more reliably than between enum values. Each returns the compact text rendering and closes with
/// the call to make next, which is what turns a caller into one that drills rather than one that dumps.
/// </para>
/// <para>
/// Every filter dimension is its own parameter, spelled out and described. The HTTP API takes the same
/// filters as a raw query string, which is right there — a URL from the UI's address bar pastes straight in —
/// but there is no address bar here, and handing a model one opaque string means the schema can say nothing
/// about what may go in it. **<see cref="ListTraces"/> has to keep pace with <c>TraceFilter</c>**: a
/// dimension added there and not here is one an MCP caller cannot reach.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class TraceTools
{
    private const int DEFAULT_LIMIT = 50;
    private const int MINIMUM_SAMPLES_FOR_SPREAD = 5;

    private readonly TraceViews mViews;

    public TraceTools(TraceViews views)
    {
        mViews = views;
    }

    [McpServerTool(Name = "list_traces")]
    [Description(
        "Lists collected traces, newest first. Every argument is optional and they combine with AND. "
        + "Start here when you do not already have a trace id."
    )]
    public string ListTraces(
        [Description("Only traces that do (true) or do not (false) contain a failed span.")]
        bool? hasError = null,
        [Description("Only traces holding at least this many spans. Useful for skipping trivial ones.")]
        int? minSpans = null,
        [Description("Only traces lasting at least this long, in seconds. Accepts fractions, e.g. 0.25.")]
        double? minDurationSeconds = null,
        [Description("Only traces lasting at most this long, in seconds.")]
        double? maxDurationSeconds = null,
        [Description(
            "Only traces that started at or after this instant. ISO 8601, e.g. '2026-08-09T14:00:00Z'. "
            + "Read as UTC when no offset is given."
        )]
        string? startedAfter = null,
        [Description(
            "Only traces that started at or before this instant. ISO 8601, read as UTC when no offset is given."
        )]
        string? startedBefore = null,
        [Description(
            "Only traces whose root span has one of these names, pipe separated — 'GET /|POST /orders'. "
            + "A trace whose root span has not arrived yet is excluded."
        )]
        string? rootSpanNames = null,
        [Description("Traces whose root span has one of these names are left out. Pipe separated.")]
        string? excludeRootSpanNames = null,
        [Description(
            "Only traces where at least one span carries one of these attributes, as 'key=value;key=value'. "
            + "Values are compared case insensitively, and a trace matches when any one pair matches."
        )]
        string? spanAttributeFilter = null,
        [Description("Maximum traces to return. Defaults to 50.")]
        int limit = DEFAULT_LIMIT
    ) =>
        mViews.ListTraces(
            new TraceFilter()
            {
                HasError = hasError,
                SpansMinimum = minSpans,
                DurationMinimum = minDurationSeconds,
                DurationMaximum = maxDurationSeconds,
                StartTime = SpanQuery.ParseTimestamp(startedAfter),
                EndTime = SpanQuery.ParseTimestamp(startedBefore),
                ExclusiveTraceNames = rootSpanNames is null ? null : Split(rootSpanNames),
                IgnoredTraceNames = Split(excludeRootSpanNames),
                SpanAttributeFilter = AttributeMatcher.Parse(spanAttributeFilter).Pairs,
            },
            limit
        ).Text
        + "\nNext: get_trace_summary with one of these ids.";

    [McpServerTool(Name = "get_trace_summary")]
    [Description(
        "A fixed size report on one trace whatever its size: errors grouped by cause with full detail on a "
        + "sample of each, where the time went by span name, names whose worst case is far above their "
        + "typical one, the tree's shape, time when nothing was running, and the attributes every span "
        + "shares. Ask for this before anything else about a trace. Times are UTC."
    )]
    public string GetTraceSummary(
        [Description("Trace id, as given by list_traces.")]
        string traceId,
        [Description("How many error spans to render in full, spread across the distinct causes. Defaults to 10.")]
        int errorLimit = 10,
        [Description(
            "Excludes errors matching these attributes, as 'key=value;key=value'. Use "
            + "'http.response.status_code=404' when 404s are being reported as failures and are not the problem."
        )]
        string? errorAttributeFilter = null
    ) =>
        Found(
            mViews.Summary(
                traceId,
                new TraceSummaryOptions()
                {
                    ErrorLimit = errorLimit,
                    ErrorExclusions = AttributeMatcher.Parse(errorAttributeFilter),
                }
            ),
            traceId
        )
        + "\nNext: get_trace_profile for where the time went, or get_span for any id above.";

    [McpServerTool(Name = "get_trace_profile")]
    [Description(
        "The aggregated call tree: every span reaching the same place in the tree merged into one node with "
        + "its count, total time, self time and spread. Grows with the number of distinct call paths rather "
        + "than the number of spans, so it stays readable on traces of any size — a 230,000 span trace "
        + "renders as under a thousand lines. Use this to find what is slow."
    )]
    public string GetTraceProfile(
        [Description("Trace id.")]
        string traceId
    ) =>
        Found(mViews.Profile(traceId, MINIMUM_SAMPLES_FOR_SPREAD), traceId)
        + "\nNext: get_trace_tree to see the same work in the order it happened, or get_span for one span.";

    [McpServerTool(Name = "get_trace_tree")]
    [Description(
        "The literal span tree in the order things happened. Siblings sharing a name are merged into a '×N' "
        + "line once there are enough of them — those lines are a group, never a single span. Each span is "
        + "printed with its id in square brackets, shortened to however few characters are unique in this "
        + "response; those short ids are accepted anywhere this API takes a span id. Times are UTC. Prefer "
        + "get_trace_profile unless the ordering of events is what you need."
    )]
    public string GetTraceTree(
        [Description("Trace id.")]
        string traceId,
        [Description(
            "Render only this span and its descendants, rather than the whole trace. Give a span id, or the "
            + "shortened form printed in square brackets by an earlier call."
        )]
        string? startAtSpanId = null,
        [Description(
            "How many siblings sharing a name it takes before they are merged into one '×N' line. "
            + "0 renders every span individually. Defaults to 3."
        )]
        int collapseThreshold = 3,
        [Description(
            "Names whose merged groups should be listed span by span instead. Pipe separated. "
            + "Use after seeing a '×N' line you want the members of."
        )]
        string? expandNames = null,
        [Description(
            "Span names to leave out along with everything beneath them, pipe separated. "
            + "For pruning branches you have already ruled out. The output reports what was removed."
        )]
        string? hiddenSpanNames = null,
        [Description(
            "How deep to go. The starting span is depth 0 and its direct children are depth 1, so 1 shows "
            + "the starting span and its children only. Omit for the whole tree; spans left out are counted "
            + "on the line above them."
        )]
        int? maxSpanDepth = null,
        [Description(
            "Which span attributes to print, as comma separated key prefixes — 'http.,db.' or 'url.full'. "
            + "'*' prints every attribute. Omit for the default, which prints all of them except "
            + "otel.library.* and telemetry.sdk.*."
        )]
        string? attributeKeys = null,
        [Description("Print span attributes at all. Defaults to true.")]
        bool includeAttributes = true,
        [Description(
            "Print span events and their attributes, which is where exception type, message and stack live "
            + "for most SDKs. Defaults to false: most spans have none and a stack trace is long."
        )]
        bool includeEvents = false,
        [Description(
            "Shorten span ids to their unique prefix. Defaults to true. Set false for whole ids, e.g. to "
            + "correlate with something outside NekoTrace."
        )]
        bool shortenSpanIds = true
    ) =>
        Found(
            mViews.Tree(
                traceId,
                new TreeViewOptions()
                {
                    CollapseThreshold = collapseThreshold,
                    ExpandedNames = Split(expandNames),
                    HiddenSpanNames = Split(hiddenSpanNames),
                    MaxDepth = maxSpanDepth,
                    RootSpanId = startAtSpanId,
                },
                AttributeSelector.Parse(attributeKeys),
                new SpanRenderOptions()
                {
                    IncludeAttributes = includeAttributes,
                    IncludeEvents = includeEvents,
                    ShortenSpanIds = shortenSpanIds,
                },
                MINIMUM_SAMPLES_FOR_SPREAD
            ),
            traceId
        );

    [McpServerTool(Name = "get_span")]
    [Description(
        "One span with everything on it — all attributes, all events including exception type, message and "
        + "stack, its chain of ancestors and its immediate children. Times are UTC."
    )]
    public string GetSpan(
        [Description(
            "Span id, or the shortened form printed in square brackets by get_trace_tree. Any prefix that "
            + "matches exactly one span in the trace is accepted; if it matches several, the reply says so "
            + "and lists them."
        )]
        string spanId,
        [Description("Trace id the span belongs to.")]
        string traceId
    ) =>
        Found(mViews.Span(traceId, spanId), traceId);

    [McpServerTool(Name = "search_spans")]
    [Description(
        "Finds spans by name, duration, status, kind, time or attribute, within one trace or across every "
        + "trace held. Every argument is optional and they combine with AND. Each match is printed with its "
        + "attributes, above them the attributes identical across every match, and below them how many "
        + "matched in total. Both of those cover the whole result, not the page: limit=1 tells you how many "
        + "spans match and what all of them share, for the price of one row. That is how to establish what a "
        + "set of spans has in common — opening a few with get_span and generalising is a claim about the "
        + "few. Returns trace and span ids to feed to get_span."
    )]
    public string SearchSpans(
        [Description("Wildcard pattern over the span name, e.g. 'GET*' or '*Grain*'. Case insensitive.")]
        string? name = null,
        [Description("Restrict to one trace. Omit to search every trace held.")]
        string? traceId = null,
        [Description("Only spans lasting at least this long, in seconds. Accepts fractions, e.g. 0.05.")]
        double? minDurationSeconds = null,
        [Description("Only spans lasting at most this long, in seconds.")]
        double? maxDurationSeconds = null,
        [Description(
            "Only spans that started at or after this instant. ISO 8601, e.g. '2026-08-09T14:00:00Z'. "
            + "Read as UTC when no offset is given."
        )]
        string? startedAfter = null,
        [Description("Only spans that started at or before this instant. ISO 8601, read as UTC without an offset.")]
        string? startedBefore = null,
        [Description("Only spans that are (true) or are not (false) marked as failed.")]
        bool? hasError = null,
        [Description("Only spans of this kind: Internal, Server, Client, Producer or Consumer.")]
        string? kind = null,
        [Description(
            "Only spans carrying one of these attributes, as 'key=value;key=value'. Values are compared "
            + "case insensitively, and a span matches when any one pair matches. This decides which spans "
            + "come back; attributeKeys decides what is printed of them."
        )]
        string? attributeFilter = null,
        [Description(
            "Which of each match's attributes to print, as comma separated key prefixes — 'http.,db.' or "
            + "'url.full'. '*' prints every attribute. Omit for the default, which prints all of them except "
            + "otel.library.* and telemetry.sdk.*."
        )]
        string? attributeKeys = null,
        [Description(
            "Print the matches' attributes at all. Defaults to true. False gives just ids, names and "
            + "durations, which is enough when you already know what the matches are and only want to count "
            + "them."
        )]
        bool includeAttributes = true,
        [Description(
            "How many matches to print. Defaults to 50. It does not limit the count or the shared attribute "
            + "block, which always describe every match — so lower it freely when you want the totals rather "
            + "than the rows."
        )]
        int limit = DEFAULT_LIMIT
    ) =>
        mViews.SearchSpans(
            new SpanQuery()
            {
                Name = name,
                DurationMinimum = minDurationSeconds,
                DurationMaximum = maxDurationSeconds,
                StartedAfter = SpanQuery.ParseTimestamp(startedAfter),
                StartedBefore = SpanQuery.ParseTimestamp(startedBefore),
                HasError = hasError,
                Kind = Enum.TryParse<SpanKind>(kind, ignoreCase: true, out var parsed) ? parsed : null,
                Attributes = AttributeMatcher.Parse(attributeFilter),
            },
            traceId,
            limit,
            AttributeSelector.Parse(attributeKeys),
            new SpanRenderOptions() { IncludeAttributes = includeAttributes }
        ).Text;

    private static string Found(TraceView? view, string traceId) =>
        view?.Text ?? ("No trace with id '" + traceId + "'. Call list_traces to see what is held.");

    private static ImmutableHashSet<string> Split(string? value) =>
        value?.Split('|', StringSplitOptions.RemoveEmptyEntries).ToImmutableHashSet(StringComparer.Ordinal)
            ?? ImmutableHashSet<string>.Empty;
}
