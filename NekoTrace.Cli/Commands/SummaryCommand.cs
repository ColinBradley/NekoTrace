namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// <c>GET api/traces/{id}/summary</c>. The first call to make about a trace.
/// </summary>
internal static class SummaryCommand
{
    public static Command Create()
    {
        var traceId = TraceIdArgument.Create();

        var errorLimit = new Option<int?>("--error-limit")
        {
            Description =
                "How many error spans to render in full, spread across the distinct causes so the budget "
                + "shows different problems rather than copies of one. The server defaults to 10.",
        };

        var errorAttributeFilter = new Option<string>("--error-attribute-filter")
        {
            Description =
                "Excludes errors matching these attributes, as 'key=value;key=value'. Use "
                + "'http.response.status_code=404' when 404s are being reported as failures and are not the "
                + "problem.",
        };

        var top = new Option<int?>("--top")
        {
            Description =
                "How many span names to list under where the time went, and how many to consider for the "
                + "outliers. The server defaults to 10.",
        };

        var command = new Command(
            "summary",
            "A fixed size report on one trace whatever its size: errors grouped by cause with a sample of "
            + "each in full, where the time went by span name, names whose worst case sits far above their "
            + "typical one, the tree's shape, stretches where nothing at all was running, and the attributes "
            + "every span shares. Ask for this before anything else about a trace. Times are UTC. Follow it "
            + "with `profile` for where the time went, or `span` for any id it printed. No flat form: this "
            + "is a report, not a list of spans."
        );

        command.Arguments.Add(traceId);
        command.Options.Add(errorLimit);
        command.Options.Add(errorAttributeFilter);
        command.Options.Add(top);

        command.SetSessionAction((parseResult, session, cancellationToken) =>
            session.WriteAsync(
                "api/traces/" + Uri.EscapeDataString(session.RequireTraceId(parseResult.GetValue(traceId)))
                    + "/summary",
                new Query()
                    .Add("errorLimit", parseResult.GetValue(errorLimit))
                    .Add("errorAttributeFilter", parseResult.GetValue(errorAttributeFilter))
                    .Add("top", parseResult.GetValue(top)),
                cancellationToken
            )
        );

        return command;
    }
}
