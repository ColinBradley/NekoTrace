namespace NekoTrace.Cli;

using System.CommandLine;

internal static class CommandExtensions
{
    /// <summary>
    /// Wires an action that wants a <see cref="Session"/>, opening one first and turning the failures the
    /// whole CLI shares into a message and an exit code.
    /// </summary>
    /// <remarks>
    /// One place for the catch, so no command has to remember to handle a server that is not running. The
    /// message goes to standard error rather than standard output, which belongs to the answer.
    /// </remarks>
    public static void SetSessionAction(
        this Command command,
        Func<ParseResult, Session, CancellationToken, Task<int>> run
    )
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                using var session = await Session.OpenAsync(parseResult, cancellationToken);

                return await run(parseResult, session, cancellationToken);
            }
            catch (CliException failure)
            {
                await Console.Error.WriteLineAsync(failure.Message);

                return failure.ExitCode;
            }
            catch (IOException failure)
            {
                await Console.Error.WriteLineAsync(failure.Message);

                return ExitCodes.REFUSED;
            }
        });
    }
}
