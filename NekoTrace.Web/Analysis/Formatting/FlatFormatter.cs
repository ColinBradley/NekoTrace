namespace NekoTrace.Web.Analysis.Formatting;

using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using System.Text;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

/// <summary>
/// One line per span, tab separated, with the same field in the same place on every line.
/// </summary>
/// <remarks>
/// The rendering for a caller that processes the output rather than reads it. <see cref="TextFormatter"/>
/// carries structure in its indentation, which a <c>grep</c> destroys; here it is in columns instead, so
/// <c>depth</c>, <c>parent</c> and <c>path</c> survive being filtered, sorted and counted.
/// <para>
/// Three rules the format keeps:
/// </para>
/// <list type="bullet">
/// <item>The same number of tab separated fields on every row, with the variable length part last and joined
/// into one field, so <c>cut -f5</c> means one thing everywhere.</item>
/// <item>Numbers are bare, invariant and in one unit named by the column, because the format exists to be
/// sorted and <see cref="Units.Duration"/>'s per-value unit sorts wrongly.</item>
/// <item>Anything that is not data is a comment line starting <c>#</c>, so <c>grep -v '^#'</c> leaves exactly
/// the rows and nothing has to be dropped to keep the counts honest.</item>
/// </list>
/// <para>
/// Spans are never merged here whatever the caller asked for: a <c>×N</c> group is a summary, and one row per
/// span is the promise the format makes. <c>TraceViews</c> builds the tree it renders with collapsing off.
/// </para>
/// </remarks>
internal static class FlatFormatter
{
    private const char FIELD = '\t';

    /// <summary>Stands in for an empty field, so no field is ever the empty string.</summary>
    private const char NONE = '-';

    private const int VALUE_LENGTH = 200;
    private const int EVENT_VALUE_LENGTH = 400;

    /// <summary>Room for the deepest path in the profile — the 230,313 span trace reaches 25 levels.</summary>
    private const int PATH_LENGTH = 4000;

    public static string Tree(
        TreeViewResult result,
        SpanIdShortener ids,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var text = new StringBuilder();

        text.AppendLine(
            "# one line per span, tab separated, every span individually. Lines starting # are notes."
        );
        text.AppendLine("# times are bare invariant milliseconds; offsets are from the start of the trace.");

        AppendHeader(
            text,
            "offsetMs", "durationMs", "selfMs", "depth", "id", "parent", "kind", "status", "name", "attributes"
        );

        AppendCommonAttributes(text, attributes, selector, render, "spans");

        var stack = new Stack<(TreeNode Node, int Depth)>();

        for (var index = result.Roots.Length - 1; index >= 0; index--)
        {
            stack.Push((result.Roots[index], 0));
        }

        var orphans = 0;
        var messages = 0;

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            switch (node)
            {
                case TreeSpan span:
                    if (span.Node.IsOrphan)
                    {
                        orphans++;
                    }

                    if (!string.IsNullOrEmpty(span.Node.Span.StatusMessage))
                    {
                        messages++;
                    }

                    AppendSpanLine(text, span, depth, ids, attributes, selector, render);

                    for (var index = span.Children.Length - 1; index >= 0; index--)
                    {
                        stack.Push((span.Children[index], depth + 1));
                    }

                    break;

                case TreeGroup group:
                    // Unreachable through TraceViews, which turns collapsing off for this format. Printed as a
                    // note rather than as a line if it ever is reached: the members are merged by then and
                    // cannot be recovered, and a group rendered as a span would be read as one span that took
                    // the whole group's time.
                    text.Append("# ").Append(Units.Count(group.Merged.Count))
                        .Append(" spans named ").Append(group.Merged.Name)
                        .AppendLine(" were merged before this rendering and cannot be listed individually.");

                    break;

                default:
                    break;
            }
        }

        AppendFooter(text, result, render.IncludeAttributes ? selector : null, orphans, messages, render.IncludeEvents);

        return text.ToString();
    }

    /// <summary>
    /// One line per merged call path, with the path in a column instead of in indentation.
    /// </summary>
    /// <remarks>
    /// The profile is the other view whose structure lives in its leading whitespace, so it is the other one
    /// a <c>grep</c> ruins: a matched line arrives naming a node with no way to tell what it hung off. Here
    /// the path is spelled out, which also makes the thing the profile is for — finding the biggest self
    /// time — a <c>sort -t$'\t' -k2 -rn</c> rather than a read.
    /// <para>
    /// Paths join on <c>;</c>, so <c>awk -F'\t' '{print $10, $1}'</c> is Brendan Gregg's collapsed-stack
    /// format weighted by total time and feeds a flamegraph directly. Not <c>cut -f10,1</c>, which emits
    /// fields in file order whatever order they are asked for, and so puts the weight first. A span name
    /// containing a <c>;</c> would be ambiguous in that column; none in <c>TestTraces/</c> does, and the
    /// alternatives collide worse — <c>/</c> appears in every Orleans grain name.
    /// </para>
    /// </remarks>
    public static string Profile(ImmutableArray<ProfileRow> rows)
    {
        var text = new StringBuilder();

        text.AppendLine("# one line per merged call path, tab separated. Lines starting # are notes.");
        text.AppendLine(
            "# times are bare invariant milliseconds, summed over the spans merged into each node."
        );

        AppendHeader(
            text,
            "totalMs", "selfMs", "count", "p50Ms", "p95Ms", "maxMs", "errors", "shapes", "depth", "path"
        );

        foreach (var row in rows)
        {
            text.Append(Units.Milliseconds(row.TotalMs)).Append(FIELD)
                .Append(Units.Milliseconds(row.SelfMs)).Append(FIELD)
                .Append(Units.Number(row.Count)).Append(FIELD)
                .Append(Units.Milliseconds(row.MedianMs)).Append(FIELD)
                .Append(Units.Milliseconds(row.P95Ms)).Append(FIELD)
                .Append(Units.Milliseconds(row.MaxMs)).Append(FIELD)
                .Append(Units.Number(row.ErrorCount)).Append(FIELD)
                .Append(Units.Number(row.DistinctChildShapes)).Append(FIELD)
                .Append(Units.Number(row.Depth)).Append(FIELD)
                // Not truncated: the path is the row's identity, and a shortened one makes two different
                // nodes look like the same node.
                .AppendLine(Inline.Value(row.Path, PATH_LENGTH));
        }

        return text.ToString();
    }

    public static string Traces(ImmutableArray<TraceListEntry> entries)
    {
        var text = new StringBuilder();

        text.AppendLine("# one line per trace, tab separated. Lines starting # are notes.");

        AppendHeader(text, "id", "start", "durationMs", "spans", "status", "rootSpanName");

        foreach (var entry in entries)
        {
            text.Append(entry.Id).Append(FIELD)
                .Append(Units.Timestamp(entry.Start)).Append(FIELD)
                .Append(Units.Milliseconds(entry.DurationMs)).Append(FIELD)
                .Append(Units.Number(entry.SpanCount)).Append(FIELD)
                .Append(entry.HasError ? "ERROR" : "OK").Append(FIELD);

            if (Inline.Value(entry.RootSpanName, VALUE_LENGTH) is { Length: > 0 } rootSpanName)
            {
                text.Append(rootSpanName);
            }
            else
            {
                // A trace whose root span has not arrived yet. The text rendering says so in words; here it is
                // the same empty field marker as everywhere else, since something is going to read the column.
                text.Append(NONE);
            }

            text.AppendLine();
        }

        if (entries.IsEmpty)
        {
            text.AppendLine("# no traces matched.");
        }

        return text.ToString();
    }

    public static string SearchResults(
        ImmutableArray<SpanSearchResult> results,
        int total,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var text = new StringBuilder();

        text.AppendLine("# one line per matching span, tab separated. Lines starting # are notes.");

        AppendHeader(
            text,
            "traceId", "spanId", "start", "durationMs", "kind", "status", "name", "attributes"
        );

        AppendCommonAttributes(text, attributes, selector, render, "matches");

        foreach (var result in results)
        {
            var span = result.Span;

            text.Append(result.TraceId).Append(FIELD)
                .Append(span.Id).Append(FIELD)
                .Append(Units.Timestamp(span.StartTime)).Append(FIELD)
                .Append(Units.Milliseconds(span.Duration.TotalMilliseconds)).Append(FIELD)
                .Append(span.Kind).Append(FIELD)
                .Append(Status(span.StatusCode)).Append(FIELD)
                .Append(Inline.Value(span.Name, VALUE_LENGTH)).Append(FIELD);

            AppendAttributeField(text, span, attributes, selector, render);

            text.AppendLine();
        }

        if (render.IncludeAttributes && selector.Explain() is { } explanation)
        {
            text.Append("# ").AppendLine(explanation);
        }

        TraceViews.AppendMatchCount(text, results.Length, total, "# ");

        return text.ToString();
    }

    private static void AppendHeader(StringBuilder text, params string[] columns)
    {
        text.Append('#').AppendJoin(FIELD, columns).AppendLine();
    }

    /// <summary>
    /// The attributes every span in the trace agrees on, printed once, up front and in full.
    /// </summary>
    /// <remarks>
    /// Hoisting them is what the compaction rules are mostly made of, and the tree can leave it at that
    /// because whoever is reading a tree read the summary first. Something grepping this cannot: a search for
    /// <c>service.name=checkout</c> would come back empty across a trace where every single span carries it,
    /// with nothing anywhere saying why. So they go in the header, in the same <c>key=value</c> shape as the
    /// attributes field, which also makes them readable by the same thing that reads the rest.
    /// </remarks>
    /// <param name="noun">What the set being described is — "spans" in a tree, "matches" in a search.</param>
    private static void AppendCommonAttributes(
        StringBuilder text,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render,
        string noun
    )
    {
        if (!render.IncludeAttributes || attributes.Common.IsEmpty)
        {
            return;
        }

        var included = attributes.Common
            .Where(pair => selector.Includes(pair.Key))
            .ToArray();

        if (included.Length is 0)
        {
            return;
        }

        text.Append("# identical on all ").Append(Units.Count(attributes.SpanCount))
            .Append(' ').Append(noun).Append(", so left off every line below: ");

        var written = 0;

        foreach (var (key, value) in included)
        {
            AppendPair(text, ref written, key, value?.ToString(), VALUE_LENGTH);
        }

        text.AppendLine();
    }

    private static void AppendSpanLine(
        StringBuilder text,
        TreeSpan span,
        int depth,
        SpanIdShortener ids,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var node = span.Node;

        text.Append(Units.Milliseconds(span.OffsetMs)).Append(FIELD)
            .Append(Units.Milliseconds(node.DurationMs)).Append(FIELD)
            .Append(Units.Milliseconds(node.SelfTimeMs)).Append(FIELD)
            .Append(Units.Number(depth)).Append(FIELD)
            .Append(ids.Shorten(node.Span.Id)).Append(FIELD);

        AppendParent(text, node, ids);

        text.Append(FIELD)
            .Append(node.Span.Kind).Append(FIELD)
            .Append(Status(node.Span.StatusCode)).Append(FIELD)
            .Append(Inline.Value(node.Span.Name, VALUE_LENGTH)).Append(FIELD);

        AppendAttributeField(text, node.Span, attributes, selector, render);

        text.AppendLine();
    }

    /// <summary>
    /// The parent id, or <c>orphan:</c> and it when the parent never arrived.
    /// </summary>
    /// <remarks>
    /// An orphan is rendered at depth 0 like a real root, so without the marker a partially collected trace
    /// reads as one that genuinely has four thousand tops. The id is left whole for an orphan: shortening is
    /// only unique among the spans that are here, and this one names a span that is not.
    /// </remarks>
    private static void AppendParent(StringBuilder text, SpanNode node, SpanIdShortener ids)
    {
        if (node.Span.ParentSpanId is not { Length: > 0 } parentId)
        {
            text.Append(NONE);
        }
        else if (node.IsOrphan)
        {
            text.Append("orphan:").Append(parentId);
        }
        else
        {
            text.Append(ids.Shorten(parentId));
        }
    }

    private static string Status(StatusCode status) =>
        status switch
        {
            StatusCode.Error => "ERROR",
            StatusCode.Ok => "OK",
            _ => "UNSET",
        };

    private static void AppendAttributeField(
        StringBuilder text,
        SpanData span,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var written = 0;

        // Before the attributes rather than in a column of its own: it is set on a handful of spans in a
        // trace, and a field that is empty on every line but those is a field worth not having.
        if (!string.IsNullOrEmpty(span.StatusMessage))
        {
            AppendPair(text, ref written, "status.message", span.StatusMessage, VALUE_LENGTH);
        }

        if (render.IncludeAttributes)
        {
            foreach (var (key, value) in attributes.Varying(span))
            {
                if (selector.Includes(key))
                {
                    AppendPair(text, ref written, key, value?.ToString(), VALUE_LENGTH);
                }
            }
        }

        if (render.IncludeEvents)
        {
            foreach (var spanEvent in span.Events)
            {
                if (spanEvent.Attributes.Count is 0)
                {
                    AppendPair(text, ref written, "event", spanEvent.Name, VALUE_LENGTH);

                    continue;
                }

                // event.<name>.<key>, even when that doubles up into event.exception.exception.type. Dropping
                // the repeat would read better right up until it collided: the traces in TestTraces/ carry an
                // event named exception holding both `message` and `exception.message`, and folding the name
                // in would render the two under one key.
                foreach (var (key, value) in spanEvent.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    AppendPair(
                        text,
                        ref written,
                        "event." + spanEvent.Name + "." + key,
                        value?.ToString(),
                        EVENT_VALUE_LENGTH
                    );
                }
            }
        }

        if (written is 0)
        {
            text.Append(NONE);
        }
    }

    private static void AppendPair(
        StringBuilder text,
        ref int written,
        string key,
        string? value,
        int length
    )
    {
        if (written > 0)
        {
            text.Append(' ');
        }

        written++;

        text.Append(key).Append('=').Append(Inline.Value(value, length));
    }

    /// <param name="selector">Null when attributes were switched off outright, which explains itself.</param>
    private static void AppendFooter(
        StringBuilder text,
        TreeViewResult result,
        AttributeSelector? selector,
        int orphans,
        int statusMessages,
        bool includeEvents
    )
    {
        if (selector?.Explain() is { } explanation)
        {
            text.Append("# ").AppendLine(explanation);
        }

        if (orphans > 0)
        {
            text.Append(FormattableString.Invariant(
                $"# {orphans} span(s) name a parent that never arrived; their parent field reads orphan:<id>."
            )).AppendLine();
        }

        if (statusMessages > 0)
        {
            text.AppendLine("# status messages are in the attributes field, as status.message=<message>.");
        }

        if (includeEvents)
        {
            text.AppendLine(
                "# span events are in the attributes field too, as event.<eventName>.<attributeKey>=<value>."
            );
        }

        if (result.HiddenSpanCount > 0)
        {
            text.Append(FormattableString.Invariant(
                $"# {result.HiddenSpanCount} span(s) worth {Units.Duration(result.HiddenDurationMs)} hidden by HiddenSpanNames/HiddenSpanIds."
            )).AppendLine();
        }

        if (result.UnrenderedByDepth > 0)
        {
            text.Append(FormattableString.Invariant(
                $"# {result.UnrenderedByDepth} span(s) not shown, past the depth limit. Raise it with maxSpanDepth, or start lower with startAtSpanId."
            )).AppendLine();
        }
    }
}
