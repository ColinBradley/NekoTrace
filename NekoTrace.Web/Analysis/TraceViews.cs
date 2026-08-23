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
                SpanCount = trace.Spans.Length,
                HasError = trace.HasError,
            })
            .ToImmutableArray();

        return new TraceView(
            entries,
            () => RenderTraceList(entries),
            () => FlatFormatter.Traces(entries)
        );
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

        // The flattened rows are the model, not the nested tree: serialising the tree recursed past
        // System.Text.Json's depth limit on the 230,313 span trace and answered a 500 with half a document
        // already written. See ProfileRow. Text still renders from the tree, where the nesting is the point.
        var rows = TraceProfile.Flatten(profile);

        return new TraceView(
            rows,
            () => TextFormatter.Profile(profile, minimumSamplesForSpread),
            () => FlatFormatter.Profile(rows)
        );
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
        // looks its starting point up by exact id. Into its own local rather than back over the parameter,
        // because the flat rendering below closes over it and has to see the resolved one.
        var resolved = options.RootSpanId is { } prefix
            ? options with { RootSpanId = ResolveSpanId(trace, prefix) ?? prefix }
            : options;

        var result = TreeView.Build(tree, resolved);

        // Shared between the two renderings but built by neither unless it runs, which on the 230,313 span
        // trace is the difference between one pass over every id and none.
        var ids = new Lazy<SpanIdShortener>(
            () => render.ShortenSpanIds
                ? SpanIdShortener.For(trace.Spans.Select(span => span.Id))
                : SpanIdShortener.None,
            LazyThreadSafetyMode.None
        );

        var attributes = new Lazy<AttributeSummary>(
            () => AttributeSummary.Build(trace.Spans),
            LazyThreadSafetyMode.None
        );

        return new TraceView(
            result,
            () => TextFormatter.Tree(
                result,
                ids.Value,
                attributes.Value,
                selector,
                render,
                minimumSamplesForSpread
            ),
            // Its own arrangement of the same SpanTree, with collapsing off: the flat format promises one line
            // per span, and a caller who left collapseThreshold at its default would otherwise get a format
            // that silently dropped most of the trace into ×N summaries it has nowhere to print. Building it
            // twice costs one more pass over an already built tree, and only when flat is the format asked for.
            () => FlatFormatter.Tree(
                TreeView.Build(tree, resolved with { CollapseThreshold = 0 }),
                ids.Value,
                attributes.Value,
                selector,
                render
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

    /// <param name="limit">
    /// How many matches to print. It bounds neither the scan nor the hoisting, so <c>limit=1</c> still
    /// answers "how many are there and what do they all share" over the whole result.
    /// </param>
    /// <param name="selector">Which attribute keys the printed matches carry.</param>
    internal TraceView SearchSpans(
        SpanQuery query,
        string? traceId,
        int limit,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var traces = traceId is null
            ? mTraces.Traces.OrderByDescending(trace => trace.Start).AsEnumerable()
            : mTraces.TryGetTrace(traceId) is { } single ? [single] : [];

        var matched = new List<SpanData>();
        var page = new List<SpanSearchResult>();

        // Scanned to the end rather than stopping at the limit, because "50, and there may be more" does not
        // answer how many there are. Costs one predicate call per span the limit would have skipped.
        foreach (var trace in traces)
        {
            foreach (var span in trace.Spans)
            {
                if (!query.Matches(span))
                {
                    continue;
                }

                matched.Add(span);

                if (page.Count < limit)
                {
                    page.Add(new SpanSearchResult() { TraceId = trace.Id, Span = span });
                }
            }
        }

        var results = page.ToImmutableArray();

        // Across every match, not the printed page: hoisting over the page would make the block's meaning
        // depend on `limit`, which is only a rendering knob. Not deferred, since all three formats use it.
        var attributes = AttributeSummary.Build(matched);

        var model = new SpanSearchResults()
        {
            Total = matched.Count,
            Common = attributes.Common,
            Matches = results,
        };

        return new TraceView(
            model,
            () => RenderSearchResults(results, matched.Count, attributes, selector, render),
            () => FlatFormatter.SearchResults(results, matched.Count, attributes, selector, render)
        );
    }

    private static string RenderSearchResults(
        ImmutableArray<SpanSearchResult> results,
        int total,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var text = new StringBuilder();

        text.AppendLine("# trace id  span id  duration  name  attributes");

        // Above the matches, matching the flat rendering: this block is often the answer, and a reader should
        // not have to get past every row to reach it.
        TextFormatter.AppendCommonAttributes(text, attributes, selector, render);

        foreach (var result in results)
        {
            text.Append(result.TraceId).Append("  ").Append(result.Span.Id)
                .Append("  ").Append(result.Span.DurationText)
                .Append("  ").Append(result.Span.Name);

            if (result.Span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error)
            {
                text.Append("  ERROR");
            }

            TextFormatter.AppendAttributes(text, result.Span, attributes, selector, render);

            text.AppendLine();
        }

        text.AppendLine();

        if (render.IncludeAttributes && selector.Explain() is { } explanation)
        {
            text.AppendLine(explanation);
        }

        AppendMatchCount(text, results.Length, total, string.Empty);

        return text.ToString();
    }

    /// <summary>The total, always, and what fraction of it was printed when the limit cut it short.</summary>
    internal static void AppendMatchCount(StringBuilder text, int shown, int total, string prefix)
    {
        text.Append(prefix).Append(Units.Count(total)).Append(total is 1 ? " match" : " matches");

        if (shown < total)
        {
            text.Append(", showing ").Append(Units.Count(shown)).Append(" — raise limit for the rest");
        }

        text.AppendLine(".");
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
