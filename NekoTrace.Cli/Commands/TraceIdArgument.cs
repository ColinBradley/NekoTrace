namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// The trace id the per-trace commands take, optional because <c>--file</c> can supply it instead.
/// </summary>
/// <remarks>
/// A fresh instance per command rather than one shared: System.CommandLine binds a value to the symbol it was
/// parsed against, and the same object hung on several commands is one object holding several commands' worth
/// of state.
/// </remarks>
internal static class TraceIdArgument
{
    public static Argument<string> Create() =>
        new("traceId")
        {
            Description =
                "Trace id, as listed by `NekoTrace.Cli traces`. Optional when --file is given: the trace in the "
                + "file is then the one meant.",
            Arity = ArgumentArity.ZeroOrOne,
        };
}
