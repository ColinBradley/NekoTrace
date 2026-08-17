namespace NekoTrace.Cli;

/// <summary>What the process leaves behind, so a script can tell the three failures apart.</summary>
internal static class ExitCodes
{
    public const int OK = 0;

    /// <summary>
    /// The server was reached and refused: an unknown trace, an ambiguous span id, a filter it would not
    /// take. The reason it gave is on standard error. System.CommandLine uses the same code for a command
    /// line it could not parse, which is the same kind of thing — the request was wrong.
    /// </summary>
    public const int REFUSED = 1;

    /// <summary>Nothing answered on that address. Almost always a NekoTrace that is not running.</summary>
    public const int UNREACHABLE = 2;
}
