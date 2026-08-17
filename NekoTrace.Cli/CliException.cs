namespace NekoTrace.Cli;

/// <summary>
/// A failure with a message already written for the person reading it, and the exit code to leave behind.
/// </summary>
/// <remarks>
/// Thrown rather than returned so that the checks can sit where the knowledge is — the option that could not
/// be parsed, the upload that came back 413 — instead of every one of them threading a result back up through
/// a command action that has nothing to add to it. <see cref="NekoTraceCli"/> catches them in one place.
/// </remarks>
internal sealed class CliException : Exception
{
    public CliException(string message, int exitCode)
        : base(message)
    {
        this.ExitCode = exitCode;
    }

    public CliException()
        : this(string.Empty, ExitCodes.REFUSED)
    {
    }

    public CliException(string message)
        : this(message, ExitCodes.REFUSED)
    {
    }

    public CliException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.ExitCode = ExitCodes.REFUSED;
    }

    public int ExitCode { get; }
}
