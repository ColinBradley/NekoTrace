namespace NekoTrace.Web.Mcp;

/// <summary>
/// What the MCP server says about itself when a client connects.
/// </summary>
/// <remarks>
/// The one place the CLI is mentioned. Server instructions rather than a tool description or appended output:
/// a client shows these once at connection, so it costs nothing per call and needs no tool call to find —
/// and a CLI is no use to a caller that only hears about it after doing the work another way.
/// </remarks>
internal static class McpInstructions
{
    public static string Build() =>
        "Read traces collected by NekoTrace. Start with list_traces for an id, then get_trace_summary, "
        + "which is sized to be read whole however large the trace is. Times are UTC.\n"
        + Cli();

    private static string Cli() =>
        CliLocation.Path is not { } path
            ? string.Empty
            : "\nIf you can run commands on this machine, NekoTrace also ships a CLI at:\n\n"
                + path + "\n\n"
                + "It calls the same analysis over HTTP and reaches what these tools cannot. `--format flat` "
                + "gives one row per span or per call path, tab separated, with the structure in columns "
                + "rather than in indentation — so grep, awk, sort and uniq can do the filtering before "
                + "anything reaches your context, and a filtered line still says what it hung off. Prefer it "
                + "when the question is a count, a grouping, or a filter over thousands of spans; prefer "
                + "these tools when you want the compact summary of one. `NekoTrace.Cli --help` documents the "
                + "rest, and every subcommand here has one there under the same name.";
}
