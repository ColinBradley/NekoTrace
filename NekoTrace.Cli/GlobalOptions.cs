namespace NekoTrace.Cli;

using System.CommandLine;

/// <summary>The three options every subcommand takes, declared once and marked recursive.</summary>
internal static class GlobalOptions
{
    public const string DEFAULT_SERVER = "http://localhost:8347";

    public const string SERVER_VARIABLE = "NEKOTRACE_URL";

    public static Option<string> Server { get; } =
        new("--server")
        {
            Description =
                "Where NekoTrace is listening. Defaults to " + SERVER_VARIABLE + " if that is set, otherwise "
                + DEFAULT_SERVER + " — the port the UI is on, not either of the OTLP collection ports.",
            DefaultValueFactory = _ =>
                Environment.GetEnvironmentVariable(SERVER_VARIABLE) is { Length: > 0 } configured
                    ? configured
                    : DEFAULT_SERVER,
            Recursive = true,
        };

    public static Option<string> Format { get; } =
        new("--format", "-f")
        {
            Description =
                "text is indented and meant to be read. flat is one line per span, tab separated, with the "
                + "tree's shape in a depth column instead of in whitespace — the one to pipe to grep, awk, "
                + "cut or wc, and the only one where a line survives being filtered out of its context. json "
                + "is the whole model, for jq. summary, profile and span have no flat form and will say so.",
            DefaultValueFactory = _ => "text",
            Recursive = true,
        };

    public static Option<FileInfo> File { get; } =
        new("--file")
        {
            Description =
                "A saved trace (.json.gz or .json) to upload before running the command, as the UI's Import "
                + "does. The trace it holds is then the one the command works on unless a trace id is given, "
                + "so `NekoTrace.Cli summary --file build.json.gz` needs nothing else. Uploading the same file "
                + "twice is harmless; the spans land in the trace they already belong to.",
            Recursive = true,
        };

    /// <summary>Puts the three on a root command. Called once, by <see cref="NekoTraceCli"/>.</summary>
    public static void AddTo(RootCommand root)
    {
        File.AcceptExistingOnly();

        Format.AcceptOnlyFromAmong("text", "flat", "json");

        root.Options.Add(Server);
        root.Options.Add(Format);
        root.Options.Add(File);
    }
}
