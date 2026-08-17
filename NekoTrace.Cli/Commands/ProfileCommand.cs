namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// <c>GET api/traces/{id}/profile</c>. The aggregated call tree.
/// </summary>
internal static class ProfileCommand
{
    public static Command Create()
    {
        var traceId = TraceIdArgument.Create();

        var command = new Command(
            "profile",
            "The aggregated call tree: every span reaching the same place in the tree merged into one node "
            + "with its count, total time, self time and spread. Grows with the number of distinct call "
            + "paths rather than the number of spans, so it stays readable on a trace of any size — 230,000 "
            + "spans render as under a thousand lines. This is the one to use to find what is slow. Self "
            + "time is the duration minus the union of the children's, not their sum, because these "
            + "workloads are asynchronous and children overlap. No flat form: its lines are merged paths "
            + "rather than spans."
        );

        command.Arguments.Add(traceId);

        command.SetSessionAction((parseResult, session, cancellationToken) =>
            session.WriteAsync(
                "api/traces/" + Uri.EscapeDataString(session.RequireTraceId(parseResult.GetValue(traceId)))
                    + "/profile",
                new Query(),
                cancellationToken
            )
        );

        return command;
    }
}
