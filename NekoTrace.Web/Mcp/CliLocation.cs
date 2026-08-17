namespace NekoTrace.Web.Mcp;

using System.Runtime.InteropServices;

/// <summary>
/// Where the <c>nekotrace</c> CLI is, if it is beside this server.
/// </summary>
/// <remarks>
/// The publish puts it there — see <c>PublishNekoTraceCli</c> in <c>NekoTrace.Web.csproj</c> — so the server
/// can hand an MCP caller the absolute path rather than leave a model to guess at it. Checked rather than
/// assumed: a source build or a container image has no CLI beside it, and advertising a path that is not
/// there is worse than advertising nothing.
/// </remarks>
internal static class CliLocation
{
    /// <summary>The absolute path to the CLI, or null when it is not next to this server.</summary>
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        // AppContext.BaseDirectory rather than Environment.ProcessPath: under `dotnet run` the process is
        // the host, and its directory is not where this app's files are.
        var candidate =
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "NekoTrace.Cli.exe"
                    : "NekoTrace.Cli"
            );

        return File.Exists(candidate) ? candidate : null;
    }
}
