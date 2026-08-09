namespace NekoTrace.Web.Analysis;

using NekoTrace.Web.Analysis.Formatting;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using System.Text;

/// <summary>
/// Assembles the views the HTTP API and the MCP server both serve, so neither owns the wiring and the two
/// cannot drift into answering the same question differently.
/// </summary>
/// <remarks>
/// Public so that dependency injection and the MCP tool type can reach it, but every method is internal:
/// the option records they take are analysis internals, and widening those just to be constructible from
/// outside the assembly would make an API out of something that is only ever built here.
/// </remarks>
public sealed class TraceViews
{
    private readonly TracesRepository mTraces;

    public TraceViews(TracesRepository traces)
    {
        mTraces = traces;
    }

    internal TraceView ListTraces(TraceFilter filter, int limit)
    {
        var entries = mTraces.Traces
            .Where(filter.Matches)
            .OrderByDescending(trace => trace.Start)
            .Take(limit)
            .Select(trace => new TraceListEntry()
            {
                Id = trace.Id,
                RootSpanName = trace.RootSpan?.Name,
                Start = trace.Start,
                DurationMs = trace.Duration.TotalMilliseconds,
                SpanCount = trace.Spans.Count,
                HasError = trace.HasError,
            })
            .ToImmutableArray();

        return new TraceView(entries, () => RenderTraceList(entries));
    }

    private static string RenderTraceList(ImmutableArray<TraceListEntry> entries)
    {
        var text = new StringBuilder();

        text.AppendLine("# id  start  duration  spans  root span name");

        foreach (var entry in entries)
        {
            text.Append(entry.Id)
                .Append("  ").Append(Units.Timestamp(entry.Start))
                .Append("  ").Append(Units.Duration(entry.DurationMs))
                .Append("  ").Append(Units.Count(entry.SpanCount)).Append(" spans");

            if (entry.HasError)
            {
                text.Append("  ERRORS");
            }

            text.Append("  ").Append(entry.RootSpanName ?? "(no root span yet)").AppendLine();
        }

        if (entries.IsEmpty)
        {
            text.AppendLine("(no traces matched)");
        }

        return text.ToString();
    }

    internal TraceView? Summary(string traceId, TraceSummaryOptions options)
    {
        if (mTraces.TryGetTrace(traceId) is not { } trace)
        {
            return null;
        }

        var summary = TraceSummary.Build(trace, SpanTree.Build(trace.Spans), options);

        return new TraceView(summary, () => RenderSummary(summary));
    }

    private static string RenderSummary(TraceSummary summary)
    {
        var text = new StringBuilder(TextFormatter.Summary(summary));

        if (!summary.ErrorSamples.IsEmpty)
        {
            text.AppendLine().Append("error spans in full (")
                .Append(Units.Count(summary.ErrorSamples.Length)).Append(" of ")
                .Append(Units.Count(summary.ErrorSpanCount)).AppendLine(", one class at a time):");

            foreach (var sample in summary.ErrorSamples)
            {
                text.AppendLine().Append(TextFormatter.SpanDetail(sample, summary.Attributes));
            }
        }

        return text.ToString();
    }

    internal TraceView? Profile(string traceId, int minimumSamplesForSpread)
    {
        if (mTraces.TryGetTrace(traceId) is not { } trace)
        {
            return null;
        }

        var profile = TraceProfile.Build(SpanTree.Build(trace.Spans));

        return new TraceView(profile, () => TextFormatter.Profile(profile, minimumSamplesForSpread));
    }

    internal TraceView? Tree(
        string traceId,
        TreeViewOptions options,
        AttributeSelector selector,
        SpanRenderOptions render,
        int minimumSamplesForSpread
    )
    {
        if (mTraces.TryGetTrace(traceId) is not { } trace)
        {
            return null;
        }

        var tree = SpanTree.Build(trace.Spans);

        // A span id given as a prefix has to become a whole one before the tree is built, since the tree
        // looks its starting point up by exact id.
        if (options.RootSpanId is { } prefix)
        {
            options = options with { RootSpanId = ResolveSpanId(trace, prefix) ?? prefix };
        }

        var result = TreeView.Build(tree, options);

        return new TraceView(
            result,
            () => TextFormatter.Tree(
                result,
                render.ShortenSpanIds
                    ? SpanIdShortener.For(trace.Spans.Select(span => span.Id))
                    : SpanIdShortener.None,
                AttributeSummary.Build(trace.Spans),
                selector,
                render,
                minimumSamplesForSpread
            )
        );
    }

    internal TraceView? Span(string traceId, string spanIdPrefix)
    {
        if (mTraces.TryGetTrace(traceId) is not { } trace)
        {
            return null;
        }

        var matches = MatchSpanIds(trace, spanIdPrefix);

        if (matches.Length is not 1)
        {
            var explanation = matches.IsEmpty
                ? "No span in this trace has an id starting '" + spanIdPrefix + "'."
                : "'" + spanIdPrefix + "' matches " + Units.Count(matches.Length)
                    + " spans: " + string.Join(", ", matches.Take(10)) + ". Use more characters.";

            return new TraceView(matches, () => explanation) { IsAmbiguous = true };
        }

        var span = trace.SpansById[matches[0]];
        var node = SpanTree.Build(trace.Spans).TryGetNode(span.Id);

        return new TraceView(span, () => RenderSpan(span, node));
    }

    private static string RenderSpan(SpanData span, SpanNode? node)
    {
        var text = new StringBuilder(TextFormatter.SpanDetail(span));

        if (node is not null)
        {
            var ancestors = new List<string>();

            for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                ancestors.Add(ancestor.Span.Name + " (" + ancestor.Span.Id + ")");
            }

            ancestors.Reverse();

            text.AppendLine();
            text.Append("ancestors: ")
                .AppendLine(ancestors.Count is 0 ? "(none — this is a forest top)" : string.Join(" → ", ancestors));

            text.Append("children: ").Append(Units.Count(node.Children.Length))
                .Append(", self time ").AppendLine(Units.Duration(node.SelfTimeMs));

            foreach (var child in node.Children.Take(20))
            {
                text.Append("  ").Append(Units.Duration(child.DurationMs))
                    .Append("  ").Append(child.Span.Name)
                    .Append("  [").Append(child.Span.Id).Append(']')
                    .AppendLine();
            }
        }

        return text.ToString();
    }

    internal TraceView SearchSpans(SpanQuery query, string? traceId, int limit)
    {
        var traces = traceId is null
            ? mTraces.Traces.OrderByDescending(trace => trace.Start).AsEnumerable()
            : mTraces.TryGetTrace(traceId) is { } single ? [single] : [];

        var matches = new List<(TraceItem Trace, SpanData Span)>();
        var truncated = false;

        foreach (var trace in traces)
        {
            foreach (var span in trace.Spans)
            {
                if (!query.Matches(span))
                {
                    continue;
                }

                if (matches.Count == limit)
                {
                    truncated = true;

                    break;
                }

                matches.Add((trace, span));
            }

            if (truncated)
            {
                break;
            }
        }

        var results = matches
            .Select(match => new SpanSearchResult() { TraceId = match.Trace.Id, Span = match.Span })
            .ToImmutableArray();

        return new TraceView(results, () => RenderSearchResults(results, truncated, limit));
    }

    private static string RenderSearchResults(
        ImmutableArray<SpanSearchResult> results,
        bool truncated,
        int limit
    )
    {
        var text = new StringBuilder();

        text.AppendLine("# trace id  span id  duration  name");

        foreach (var result in results)
        {
            text.Append(result.TraceId).Append("  ").Append(result.Span.Id)
                .Append("  ").Append(result.Span.DurationText)
                .Append("  ").Append(result.Span.Name);

            if (result.Span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error)
            {
                text.Append("  ERROR");
            }

            text.AppendLine();
        }

        if (results.IsEmpty)
        {
            text.AppendLine("(nothing matched)");
        }

        if (truncated)
        {
            text.Append("stopped at ").Append(Units.Count(limit))
                .AppendLine(" matches — narrow the query or raise limit=.");
        }

        return text.ToString();
    }

    /// <summary>The one span id starting with <paramref name="prefix"/>, or null when it is not exactly one.</summary>
    internal static string? ResolveSpanId(TraceItem trace, string prefix)
    {
        var matches = MatchSpanIds(trace, prefix);

        return matches.Length is 1 ? matches[0] : null;
    }

    private static ImmutableArray<string> MatchSpanIds(TraceItem trace, string prefix) =>
        trace.SpansById.ContainsKey(prefix)
            ? [prefix]
            :
            [
                .. trace.SpansById.Keys.Where(id =>
                    id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ),
            ];
}
