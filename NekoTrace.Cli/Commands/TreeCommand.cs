namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// <c>GET api/traces/{id}/tree</c>. The one command where <c>--format flat</c> is the point.
/// </summary>
internal static class TreeCommand
{
    public static Command Create()
    {
        var traceId = TraceIdArgument.Create();

        var startAtSpanId = new Option<string>("--start-at-span-id")
        {
            Description =
                "Render only this span and its descendants, rather than the whole trace. Give a span id, or "
                + "the shortened form printed in square brackets by an earlier call.",
        };

        var collapseThreshold = new Option<int?>("--collapse-threshold")
        {
            Description =
                "How many siblings sharing a name it takes before they are merged into one ×N line. 0 renders "
                + "every span individually. The server defaults to 3. Ignored under --format flat, which is "
                + "one line per span by definition.",
        };

        var expandNames = new Option<string>("--expand-names")
        {
            Description =
                "Names whose merged groups should be listed span by span instead. Pipe separated. Use after "
                + "seeing a ×N line you want the members of.",
        };

        var hiddenSpanNames = new Option<string>("--hidden-span-names")
        {
            Description =
                "Span names to leave out along with everything beneath them, pipe separated. For pruning "
                + "branches already ruled out. What was removed is reported rather than quietly subtracted.",
        };

        var hiddenSpanIds = new Option<string>("--hidden-span-ids")
        {
            Description =
                "Span ids to leave out along with everything beneath them, pipe separated. Same name and "
                + "meaning as the trace viewer's, so a URL copied out of the UI transfers unchanged.",
        };

        var maxSpanDepth = new Option<int?>("--max-span-depth")
        {
            Description =
                "How deep to go. The starting span is depth 0 and its direct children are depth 1, so 1 "
                + "shows the starting span and its children only. Omit for the whole tree; spans left out "
                + "are counted on the line above them.",
        };

        var attributeKeys = new Option<string>("--attribute-keys")
        {
            Description =
                "Which span attributes to print, as comma separated key prefixes — 'http.,db.' or 'url.full'. "
                + "'*' prints every attribute. Omit for the default, which prints all of them except "
                + "otel.library.* and telemetry.sdk.*, and says so in the footer.",
        };

        var includeAttributes = new Option<bool?>("--include-attributes")
        {
            Description = "Print span attributes at all. Defaults to true; pass 'false' to drop them.",
        };

        var includeEvents = new Option<bool?>("--include-events")
        {
            Description =
                "Print span events and their attributes, which is where exception type, message and stack "
                + "live for most SDKs. Defaults to false: most spans have none and a stack trace is long.",
        };

        var shortenSpanIds = new Option<bool?>("--shorten-span-ids")
        {
            Description =
                "Shorten span ids to the shortest prefix unique in this response, git style. Defaults to "
                + "true, and any unambiguous prefix is accepted back wherever a span id is taken. Pass "
                + "'false' for whole ids, e.g. to correlate with something outside NekoTrace.",
        };

        var command = new Command(
            "tree",
            "The literal span tree in the order things happened. Siblings sharing a name are merged into a "
            + "×N line once there are enough of them — those lines are a group, never a single span. Times "
            + "are UTC and offsets are from the start of the trace. Prefer `profile` unless the order of "
            + "events is what you need. This is the command --format flat exists for: it prints every span "
            + "on its own line with depth and parent in columns, which is what makes a grep of it still "
            + "readable."
        );

        command.Arguments.Add(traceId);
        command.Options.Add(startAtSpanId);
        command.Options.Add(collapseThreshold);
        command.Options.Add(expandNames);
        command.Options.Add(hiddenSpanNames);
        command.Options.Add(hiddenSpanIds);
        command.Options.Add(maxSpanDepth);
        command.Options.Add(attributeKeys);
        command.Options.Add(includeAttributes);
        command.Options.Add(includeEvents);
        command.Options.Add(shortenSpanIds);

        command.SetSessionAction((parseResult, session, cancellationToken) =>
            session.WriteAsync(
                "api/traces/" + Uri.EscapeDataString(session.RequireTraceId(parseResult.GetValue(traceId)))
                    + "/tree",
                new Query()
                    .Add("startAtSpanId", parseResult.GetValue(startAtSpanId))
                    .Add("collapseThreshold", parseResult.GetValue(collapseThreshold))
                    .Add("expandNames", parseResult.GetValue(expandNames))
                    // Capitalised, unlike the rest: these two are spelled the way arrangeSpans spells them in
                    // the trace viewer's query string, so a URL from the UI transfers unchanged.
                    .Add("HiddenSpanNames", parseResult.GetValue(hiddenSpanNames))
                    .Add("HiddenSpanIds", parseResult.GetValue(hiddenSpanIds))
                    .Add("maxSpanDepth", parseResult.GetValue(maxSpanDepth))
                    .Add("attributeKeys", parseResult.GetValue(attributeKeys))
                    .Add("includeAttributes", parseResult.GetValue(includeAttributes))
                    .Add("includeEvents", parseResult.GetValue(includeEvents))
                    .Add("shortenSpanIds", parseResult.GetValue(shortenSpanIds)),
                cancellationToken
            )
        );

        return command;
    }
}
