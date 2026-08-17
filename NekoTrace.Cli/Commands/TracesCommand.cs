namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// <c>GET api/traces</c>. The filter dimensions are <c>TraceFilter</c>'s, one option each.
/// </summary>
internal static class TracesCommand
{
    public static Command Create()
    {
        var hasError = new Option<bool?>("--has-error")
        {
            Description = "Only traces that do (true) or do not (false) contain a failed span.",
        };

        var minSpans = new Option<int?>("--min-spans")
        {
            Description = "Only traces holding at least this many spans. Useful for skipping trivial ones.",
        };

        var minDurationSeconds = new Option<double?>("--min-duration-seconds")
        {
            Description = "Only traces lasting at least this long, in seconds. Accepts fractions, e.g. 0.25.",
        };

        var maxDurationSeconds = new Option<double?>("--max-duration-seconds")
        {
            Description = "Only traces lasting at most this long, in seconds.",
        };

        var startedAfter = new Option<string>("--started-after")
        {
            Description =
                "Only traces that started at or after this instant. ISO 8601, e.g. '2026-08-09T14:00:00Z'. "
                + "Read as UTC when no offset is given, which is the form every timestamp here is printed in.",
        };

        var startedBefore = new Option<string>("--started-before")
        {
            Description =
                "Only traces that started at or before this instant. ISO 8601, read as UTC without an offset. "
                + "Both bounds are on when the trace started; neither looks at when it ended.",
        };

        var rootSpanNames = new Option<string>("--root-span-names")
        {
            Description =
                "Only traces whose root span has one of these names, pipe separated — 'GET /|POST /orders'. "
                + "A trace whose root span has not arrived yet is excluded.",
        };

        var excludeRootSpanNames = new Option<string>("--exclude-root-span-names")
        {
            Description = "Traces whose root span has one of these names are left out. Pipe separated.",
        };

        var spanAttributeFilter = new Option<string>("--span-attribute-filter")
        {
            Description =
                "Only traces where at least one span carries one of these attributes, as 'key=value;key=value'. "
                + "Values are compared case insensitively, and a trace matches when any one pair matches.",
        };

        var limit = new Option<int?>("--limit")
        {
            Description = "Maximum traces to return. The server defaults to 50.",
        };

        var command = new Command(
            "traces",
            "Lists collected traces, newest first. Every option is optional and they combine with AND. "
            + "Start here when you do not already have a trace id: the id in the first column is what every "
            + "other command takes."
        );

        command.Options.Add(hasError);
        command.Options.Add(minSpans);
        command.Options.Add(minDurationSeconds);
        command.Options.Add(maxDurationSeconds);
        command.Options.Add(startedAfter);
        command.Options.Add(startedBefore);
        command.Options.Add(rootSpanNames);
        command.Options.Add(excludeRootSpanNames);
        command.Options.Add(spanAttributeFilter);
        command.Options.Add(limit);

        command.SetSessionAction((parseResult, session, cancellationToken) =>
            session.WriteAsync(
                "api/traces",
                // The keys are TraceFilter's own, which is what lets a URL copied out of the UI's address bar
                // mean the same thing here. The option names are the ones the MCP tools use, because those
                // say what the filter does rather than what the field is called: StartTime and EndTime both
                // bound the trace's start, and the old names invite exactly the wrong reading.
                new Query()
                    .Add("HasError", parseResult.GetValue(hasError))
                    .Add("SpansMinimum", parseResult.GetValue(minSpans))
                    .Add("DurationMinimum", parseResult.GetValue(minDurationSeconds))
                    .Add("DurationMaximum", parseResult.GetValue(maxDurationSeconds))
                    .Add("StartTime", Timestamps.ToUtc(parseResult.GetValue(startedAfter), "--started-after"))
                    .Add("EndTime", Timestamps.ToUtc(parseResult.GetValue(startedBefore), "--started-before"))
                    .Add("ExclusiveTraceNames", parseResult.GetValue(rootSpanNames))
                    .Add("IgnoredTraceNames", parseResult.GetValue(excludeRootSpanNames))
                    .Add("SpanAttributeFilter", parseResult.GetValue(spanAttributeFilter))
                    .Add("limit", parseResult.GetValue(limit)),
                cancellationToken
            )
        );

        return command;
    }
}
