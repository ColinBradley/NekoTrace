namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// <c>GET api/traces/{traceId}/spans/{spanId}</c>. One span, everything on it.
/// </summary>
internal static class SpanCommand
{
    public static Command Create()
    {
        var spanId = new Argument<string>("spanId")
        {
            Description =
                "Span id, or the shortened form printed in square brackets by `tree`. Any prefix matching "
                + "exactly one span in the trace is accepted; a prefix matching several is answered with the "
                + "candidates rather than a shrug.",
        };

        var traceId = new Option<string>("--trace-id", "--trace")
        {
            Description =
                "The trace the span belongs to. Required, unless --file is given — the trace in the file is "
                + "then the one meant.",
        };

        var command = new Command(
            "span",
            "One span with everything on it — all attributes, all events including exception type, message "
            + "and stack, its chain of ancestors and its immediate children. Times are UTC. This is where a "
            + "span id read out of `summary`, `tree` or `search` gets cashed in. No flat form: it is one "
            + "span in full, which is the opposite of one line per span."
        );

        command.Arguments.Add(spanId);
        command.Options.Add(traceId);

        // Caught by the parser rather than at request time, so `NekoTrace.Cli span <id>` fails on the spelling of
        // the command instead of after a round trip, and so --help says which options are not optional. It is
        // a validator rather than Required because --file supplies the same thing, and a flag marked required
        // that is sometimes not required is worse than one that explains itself.
        command.Validators.Add(result =>
        {
            if (result.GetValue(traceId) is not { Length: > 0 } && result.GetValue(GlobalOptions.File) is null)
            {
                result.AddError(
                    "--trace-id is required: a span id only means something inside a trace. Get one from "
                    + "`NekoTrace.Cli traces`, or use --file to upload the trace it came from."
                );
            }
        });

        command.SetSessionAction((parseResult, session, cancellationToken) =>
            session.WriteAsync(
                "api/traces/" + Uri.EscapeDataString(session.RequireTraceId(parseResult.GetValue(traceId)))
                    + "/spans/" + Uri.EscapeDataString(parseResult.GetValue(spanId) ?? string.Empty),
                new Query(),
                cancellationToken
            )
        );

        return command;
    }
}
