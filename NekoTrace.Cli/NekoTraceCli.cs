namespace NekoTrace.Cli;

using NekoTrace.Cli.Commands;
using System.CommandLine;

/// <summary>
/// The third front door onto the trace analysis engine, after the HTTP API and the MCP server.
/// </summary>
/// <remarks>
/// <para>
/// A thin HTTP client and nothing more — no embedded engine, no local copy of a trace. NekoTrace is local and
/// cheap to run, so five more questions asked of it beat downloading a 217 MB trace to answer one. Every
/// subcommand is one endpoint, and every option is one query parameter.
/// </para>
/// <para>
/// The descriptions are the interface. What reads them is as likely to be an agent piping the output through
/// <c>grep</c> as a person, and neither has anything else to go on — so they say what a thing is for and what
/// to reach for next, not just what it is called. They are the same words the MCP tools carry, because both
/// surfaces are describing the same endpoint.
/// </para>
/// </remarks>
internal static class NekoTraceCli
{
    public static RootCommand Create()
    {
        var root = new RootCommand(
            "Ask a running NekoTrace about the traces it has collected.\n"
            + "\n"
            + "NekoTrace is an in-memory OpenTelemetry collector with a web UI. This is a client for its "
            + "read API, so one has to be running: start it and leave it running, then point this at it with "
            + "--server or " + GlobalOptions.SERVER_VARIABLE + " (default " + GlobalOptions.DEFAULT_SERVER
            + "). Nothing is analysed here; the answers are the server's.\n"
            + "\n"
            + "Start with `NekoTrace.Cli traces` for an id, then `NekoTrace.Cli summary <id>`, which is sized to be "
            + "read whole however large the trace is. From there `profile` says where the time went, `tree` "
            + "what happened in what order, `search` finds spans across traces, and `span` opens one up. "
            + "`--file trace.json.gz` uploads a saved trace first, so a file on disk answers all of the same "
            + "questions.\n"
            + "\n"
            + "All times are UTC and all numbers are invariant. There is no option to change either: nothing "
            + "on this side of the wire has a browser to ask what zone to use."
        );

        GlobalOptions.AddTo(root);

        root.Subcommands.Add(TracesCommand.Create());
        root.Subcommands.Add(SummaryCommand.Create());
        root.Subcommands.Add(ProfileCommand.Create());
        root.Subcommands.Add(TreeCommand.Create());
        root.Subcommands.Add(SpanCommand.Create());
        root.Subcommands.Add(SearchCommand.Create());

        return root;
    }
}
