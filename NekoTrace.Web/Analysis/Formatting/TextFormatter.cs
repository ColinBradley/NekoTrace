namespace NekoTrace.Web.Analysis.Formatting;

using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using System.Text;

/// <summary>
/// The default rendering: an indented text tree, one line per node.
/// </summary>
/// <remarks>
/// Text rather than JSON because nested JSON costs roughly two to three times the tokens for the same
/// structure, and structure is most of what these views are. The JSON rendering is there for callers that
/// want to post-process locally.
/// </remarks>
internal static class TextFormatter
{
    private const string INDENT = "  ";

    public static string Summary(TraceSummary summary)
    {
        var text = new StringBuilder();

        text.Append("trace ").Append(summary.TraceId);

        if (summary.RootSpanName is { } rootName)
        {
            text.Append(" \"").Append(rootName).Append('"');
        }

        text.Append("  ").Append(Units.Timestamp(summary.Start))
            .Append("  ").Append(Units.Duration(summary.DurationMs))
            .Append("  ").Append(Units.Count(summary.SpanCount)).Append(" spans")
            .Append("  ").Append(Units.Count(summary.DistinctSpanNames)).Append(" names")
            .AppendLine();

        if (!summary.Services.IsEmpty)
        {
            text.Append("services: ").AppendJoin(", ", summary.Services).AppendLine();
        }

        if (summary.ForestTopCount > 1)
        {
            text.Append(FormattableString.Invariant(
                $"forest: {summary.ForestTopCount} tops, {summary.OrphanCount} of them spans whose parent never arrived"
            )).AppendLine();
        }

        if (summary.CycleCount > 0)
        {
            text.Append(FormattableString.Invariant(
                $"malformed: {summary.CycleCount} span(s) were in a parent cycle and have been treated as tops"
            )).AppendLine();
        }

        AppendErrors(text, summary);
        AppendTimeByName(text, summary);
        AppendOutliers(text, summary);
        AppendShape(text, summary);
        AppendGaps(text, summary);
        AppendCommonAttributes(text, summary);

        return text.ToString();
    }

    public static string Profile(ImmutableArray<ProfileNode> roots, int minimumSamplesForSpread)
    {
        var text = new StringBuilder();

        text.AppendLine("# total self n=spans  name       (total and self are summed over the merged spans)");

        // Explicit stack, like everything else that walks one of these: depth is whatever was collected.
        var stack = new Stack<(ProfileNode Node, int Depth)>();

        for (var index = roots.Length - 1; index >= 0; index--)
        {
            stack.Push((roots[index], 0));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            for (var index = 0; index < depth; index++)
            {
                text.Append(INDENT);
            }

            text.Append(Units.Duration(node.Durations.TotalMs))
                .Append(' ').Append(Units.Duration(node.SelfMs))
                .Append(" n=").Append(Units.Count(node.Count))
                .Append("  ").Append(node.Name);

            AppendSpread(text, node.Durations, minimumSamplesForSpread);

            if (node.ErrorCount > 0)
            {
                text.Append(FormattableString.Invariant($" errors={node.ErrorCount}"));
            }

            if (node.DistinctChildShapes > 1)
            {
                text.Append(FormattableString.Invariant($" shapes={node.DistinctChildShapes}"));
            }

            text.AppendLine();

            for (var index = node.Children.Length - 1; index >= 0; index--)
            {
                stack.Push((node.Children[index], depth + 1));
            }
        }

        return text.ToString();
    }

    public static string Tree(
        TreeViewResult result,
        SpanIdShortener ids,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render,
        int minimumSamplesForSpread
    )
    {
        var text = new StringBuilder();

        text.AppendLine("# offset duration [id] name — a ×N line is many sibling spans merged, not one span");

        var stack = new Stack<(TreeNode Node, int Depth)>();

        for (var index = result.Roots.Length - 1; index >= 0; index--)
        {
            stack.Push((result.Roots[index], 0));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            Indent(text, depth);

            switch (node)
            {
                case TreeGroup group:
                    AppendGroup(text, group, ids, depth, minimumSamplesForSpread);

                    break;

                case TreeSpan span:
                    AppendSpanLine(text, span, ids, attributes, selector, render);

                    for (var index = span.Children.Length - 1; index >= 0; index--)
                    {
                        stack.Push((span.Children[index], depth + 1));
                    }

                    break;

                default:
                    break;
            }
        }

        AppendTreeFooter(text, result, render.IncludeAttributes ? selector : null);

        return text.ToString();
    }

    /// <summary>One span with everything on it. Used for the error samples and the single span endpoint.</summary>
    /// <param name="hoisted">
    /// When given, the attributes it holds are skipped — the summary already lists them once as the block
    /// every span shares, and repeating them under each error sample is the duplication this whole thing
    /// exists to remove. Null renders every attribute, which is what the single span endpoint wants.
    /// </param>
    public static string SpanDetail(SpanData span, AttributeSummary? hoisted = null)
    {
        var text = new StringBuilder();

        text.Append(span.Name).Append("  ").Append(span.Id)
            .Append("  ").Append(Units.Timestamp(span.StartTime))
            .Append("  ").Append(Units.Duration((span.EndTime - span.StartTime).TotalMilliseconds))
            .Append("  kind=").Append(span.Kind)
            .Append("  status=").Append(span.StatusCode)
            .AppendLine();

        if (!string.IsNullOrEmpty(span.StatusMessage))
        {
            text.Append(INDENT).Append("message: ").Append(span.StatusMessage).AppendLine();
        }

        if (span.ParentSpanId is { } parentId)
        {
            text.Append(INDENT).Append("parent: ").Append(parentId).AppendLine();
        }

        var attributes = hoisted is null
            ? span.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            : hoisted.Varying(span);

        foreach (var (key, value) in attributes)
        {
            text.Append(INDENT).Append(key).Append('=').Append(value).AppendLine();
        }

        foreach (var spanEvent in span.Events)
        {
            text.Append(INDENT).Append("event ").Append(spanEvent.Name)
                .Append(" @ ").Append(Units.Timestamp(spanEvent.Time))
                .AppendLine();

            foreach (var (key, value) in spanEvent.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append(INDENT).Append(INDENT).Append(key).Append('=').Append(value).AppendLine();
            }
        }

        return text.ToString();
    }

    private static void AppendErrors(StringBuilder text, TraceSummary summary)
    {
        if (summary.ErrorSpanCount is 0 && summary.HandledExceptionCount is 0)
        {
            text.AppendLine().AppendLine("errors: none");

            return;
        }

        text.AppendLine();
        text.Append(FormattableString.Invariant(
            $"errors: {summary.ErrorSpanCount} span(s) in {summary.ErrorClasses.Length} class(es)"
        )).AppendLine();

        foreach (var errorClass in summary.ErrorClasses)
        {
            text.Append(INDENT)
                .Append(Units.Count(errorClass.Count)).Append(" × ").Append(errorClass.SpanName);

            if (errorClass.ErrorType is { } errorType)
            {
                text.Append("  type=").Append(errorType);
            }

            if (errorClass.HttpStatusCode is { } statusCode)
            {
                text.Append("  http=").Append(statusCode);
            }

            if (!string.IsNullOrEmpty(errorClass.Message))
            {
                text.Append("  \"").Append(Inline(errorClass.Message, 160)).Append('"');
            }

            text.AppendLine();
        }

        if (summary.HandledExceptionCount > 0)
        {
            text.Append(FormattableString.Invariant(
                $"handled exceptions: {summary.HandledExceptionCount} span(s) carry an exception event but are not marked failed"
            )).AppendLine();
        }
    }

    private static void AppendTimeByName(StringBuilder text, TraceSummary summary)
    {
        if (summary.TimeByName.IsEmpty)
        {
            return;
        }

        text.AppendLine();
        text.Append("self time by name (share of the ")
            .Append(Units.Duration(summary.DurationMs))
            .AppendLine(" the trace took):");

        foreach (var cost in summary.TimeByName)
        {
            text.Append(INDENT)
                .Append(Units.Duration(cost.SelfMs))
                .Append("  ").Append(Units.Percent(cost.SelfPercent))
                .Append("  n=").Append(Units.Count(cost.Durations.Count))
                .Append("  ").Append(cost.Name);

            if (cost.ErrorCount > 0)
            {
                text.Append(FormattableString.Invariant($"  errors={cost.ErrorCount}"));
            }

            text.AppendLine();
        }
    }

    private static void AppendOutliers(StringBuilder text, TraceSummary summary)
    {
        if (summary.Outliers.IsEmpty)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("outliers — names whose worst self time sits well above their typical one:");

        foreach (var cost in summary.Outliers)
        {
            text.Append(INDENT).Append(cost.Name)
                .Append("  n=").Append(Units.Count(cost.SelfDurations.Count))
                .Append("  self p50=").Append(Units.Duration(cost.SelfDurations.MedianMs))
                .Append(" p95=").Append(Units.Duration(cost.SelfDurations.P95Ms))
                .Append(" max=").Append(Units.Duration(cost.SelfDurations.MaxMs))
                .Append(FormattableString.Invariant($" ({Math.Round(cost.SelfDurations.TailRatio)}× p50)"))
                .Append("  slowest=").Append(cost.SlowestSpanId)
                .AppendLine();
        }
    }

    private static void AppendShape(StringBuilder text, TraceSummary summary)
    {
        text.AppendLine();
        text.Append(FormattableString.Invariant($"shape: depth {summary.MaxDepth}"));

        if (summary.WidestFanOutName is { } widestName)
        {
            text.Append(FormattableString.Invariant(
                $", widest fan out {summary.WidestFanOut} children of {widestName} ({summary.WidestFanOutSpanId})"
            ));
        }

        text.AppendLine();

        if (!summary.RecursiveNames.IsEmpty)
        {
            text.Append("recursion (names appearing twice on one path): ")
                .AppendJoin(", ", summary.RecursiveNames)
                .AppendLine();
        }
    }

    private static void AppendGaps(StringBuilder text, TraceSummary summary)
    {
        if (summary.Gaps.IsEmpty)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("dead time — nothing at all was running:");

        foreach (var gap in summary.Gaps)
        {
            text.Append(INDENT)
                .Append(Units.Duration(gap.DurationMs))
                .Append(" at ").Append(Units.Offset(gap.StartMs))
                .AppendLine();
        }
    }

    private static void AppendCommonAttributes(StringBuilder text, TraceSummary summary)
    {
        if (summary.Attributes.Common.IsEmpty)
        {
            return;
        }

        text.AppendLine();
        text.Append("attributes identical on all ").Append(Units.Count(summary.SpanCount))
            .AppendLine(" spans (omitted everywhere else):");

        foreach (var (key, value) in summary.Attributes.Common)
        {
            text.Append(INDENT).Append(key).Append('=').Append(value).AppendLine();
        }
    }

    private static void AppendGroup(
        StringBuilder text,
        TreeGroup group,
        SpanIdShortener ids,
        int baseDepth,
        int minimumSamplesForSpread
    )
    {
        var merged = group.Merged;

        text.Append(Units.Offset(group.OffsetMs))
            .Append("  ×").Append(Units.Count(merged.Count)).Append(' ').Append(merged.Name)
            .Append("  total=").Append(Units.Duration(merged.Durations.TotalMs))
            .Append(" self=").Append(Units.Duration(merged.SelfMs));

        AppendSpread(text, merged.Durations, minimumSamplesForSpread);

        text.Append(" slowest=").Append(ids.Shorten(merged.SlowestSpanId));

        if (merged.ErrorCount > 0)
        {
            text.Append(FormattableString.Invariant($" errors={merged.ErrorCount}"));
        }

        if (merged.DistinctChildShapes > 1)
        {
            text.Append(FormattableString.Invariant(
                $" shapes={merged.DistinctChildShapes} (members differ; expand to see them)"
            ));
        }

        text.AppendLine();

        // The merged subtree, indented under the group, so a collapsed branch still shows what it contained.
        var stack = new Stack<(ProfileNode Node, int Depth)>();

        for (var index = merged.Children.Length - 1; index >= 0; index--)
        {
            stack.Push((merged.Children[index], 1));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            // Offset by the group's own depth as well as the depth within it, or a merged subtree renders
            // flush against whatever contained the group and reads as its sibling.
            Indent(text, baseDepth + depth);

            text.Append('×').Append(Units.Count(node.Count)).Append(' ').Append(node.Name)
                .Append("  total=").Append(Units.Duration(node.Durations.TotalMs))
                .Append(" self=").Append(Units.Duration(node.SelfMs));

            if (node.ErrorCount > 0)
            {
                text.Append(FormattableString.Invariant($" errors={node.ErrorCount}"));
            }

            text.AppendLine();

            for (var index = node.Children.Length - 1; index >= 0; index--)
            {
                stack.Push((node.Children[index], depth + 1));
            }
        }
    }

    private static void AppendSpanLine(
        StringBuilder text,
        TreeSpan span,
        SpanIdShortener ids,
        AttributeSummary attributes,
        AttributeSelector selector,
        SpanRenderOptions render
    )
    {
        var node = span.Node;

        text.Append(Units.Offset(span.OffsetMs))
            .Append("  ").Append(Units.Duration(node.DurationMs));

        if (node.SelfTimeMs < node.DurationMs)
        {
            text.Append(" self=").Append(Units.Duration(node.SelfTimeMs));
        }

        text.Append("  ").Append(node.Span.Name)
            .Append("  [").Append(ids.Shorten(node.Span.Id)).Append(']');

        if (node.HasError)
        {
            text.Append(" ERROR");

            if (!string.IsNullOrEmpty(node.Span.StatusMessage))
            {
                text.Append(" \"").Append(Inline(node.Span.StatusMessage, 160)).Append('"');
            }
        }

        if (render.IncludeAttributes)
        {
            foreach (var (key, value) in attributes.Varying(node.Span))
            {
                if (selector.Includes(key))
                {
                    text.Append(' ').Append(key).Append('=').Append(Inline(value?.ToString(), 200));
                }
            }
        }

        if (node.IsOrphan)
        {
            text.Append("  (parent ").Append(node.Span.ParentSpanId).Append(" was never received)");
        }

        if (span.UnrenderedDescendants > 0)
        {
            text.Append(FormattableString.Invariant(
                $"  … {span.UnrenderedDescendants} descendant(s) below the depth limit"
            ));
        }

        text.AppendLine();

        if (render.IncludeEvents)
        {
            foreach (var spanEvent in node.Span.Events)
            {
                text.Append(INDENT).Append("event ").Append(spanEvent.Name);

                foreach (var (key, value) in spanEvent.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    text.Append(' ').Append(key).Append('=').Append(Inline(value?.ToString(), 400));
                }

                text.AppendLine();
            }
        }
    }

    /// <param name="selector">Null when attributes were switched off outright, which explains itself.</param>
    private static void AppendTreeFooter(
        StringBuilder text,
        TreeViewResult result,
        AttributeSelector? selector
    )
    {
        text.AppendLine();

        if (selector?.Explain() is { } explanation)
        {
            text.AppendLine(explanation);
        }

        if (result.CollapsedGroupCount > 0)
        {
            text.Append(FormattableString.Invariant(
                $"{result.CollapsedSpanCount} span(s) merged into {result.CollapsedGroupCount} ×N group(s). Expand one with expand=<name>, or collapse=0 for every span."
            )).AppendLine();
        }

        if (result.HiddenSpanCount > 0)
        {
            text.Append(FormattableString.Invariant(
                $"{result.HiddenSpanCount} span(s) worth {Units.Duration(result.HiddenDurationMs)} hidden by HiddenSpanNames/HiddenSpanIds."
            )).AppendLine();
        }

        if (result.UnrenderedByDepth > 0)
        {
            text.Append(FormattableString.Invariant(
                $"{result.UnrenderedByDepth} span(s) not shown, past the depth limit. Raise it with depth=, or start lower with rootSpanId=."
            )).AppendLine();
        }
    }

    /// <summary>
    /// Percentiles only once there are enough samples for them to mean anything — below that the spread is
    /// just min and max wearing a hat.
    /// </summary>
    private static void AppendSpread(StringBuilder text, DurationStatistics durations, int minimumSamples)
    {
        if (durations.Count < minimumSamples)
        {
            return;
        }

        text.Append(" p50=").Append(Units.Duration(durations.MedianMs))
            .Append(" p95=").Append(Units.Duration(durations.P95Ms))
            .Append(" max=").Append(Units.Duration(durations.MaxMs));
    }

    private static void Indent(StringBuilder text, int depth)
    {
        for (var index = 0; index < depth; index++)
        {
            text.Append(INDENT);
        }
    }

    /// <summary>
    /// One line's worth of an attribute value. Line breaks are folded out before truncating, because a value
    /// like <c>error.stack</c> is many lines long and one of those landing in the middle of a tree destroys
    /// the indentation that is carrying the structure.
    /// </summary>
    private static string? Inline(string? value, int length)
    {
        if (value is null)
        {
            return null;
        }

        var folded = value.ReplaceLineEndings(" ⏎ ");

        return folded.Length <= length ? folded : folded[..length] + "…";
    }
}
