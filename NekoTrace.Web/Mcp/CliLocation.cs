namespace NekoTrace.Web.Mcp;

using System.Runtime.InteropServices;

/// <summary>
/// Where the <c>NekoTrace.Cli</c> executable is, if this server can find one.
/// </summary>
/// <remarks>
/// The publish puts it beside the server — see <c>PublishNekoTraceCli</c> in <c>NekoTrace.Web.csproj</c> — so
/// the server can hand an MCP caller the absolute path rather than leave a model to guess at it. Checked
/// rather than assumed: a container image has no CLI beside it, and advertising a path that is not there is
/// worse than advertising nothing.
/// </remarks>
internal static class CliLocation
{
    /// <summary>The absolute path to the CLI, or null when there is none to be found.</summary>
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "NekoTrace.Cli.exe"
            : "NekoTrace.Cli";

        // AppContext.BaseDirectory rather than Environment.ProcessPath: under `dotnet run` the process is
        // the host, and its directory is not where this app's files are.
        var beside = System.IO.Path.Combine(AppContext.BaseDirectory, fileName);

        if (File.Exists(beside))
        {
            return beside;
        }

#if DEBUG
        return FindInCliProjectOutput(fileName);
#else
        return null;
#endif
    }

#if DEBUG
    /// <summary>The CLI in its own project's build output, or null when there isn't one there.</summary>
    /// <remarks>
    /// Debugging the server on its own runs it out of its project's bin directory, which nothing publishes the
    /// CLI into, so without this the CLI would go unadvertised for the whole of development — the one time the
    /// MCP instructions are being changed and want reading. The two projects' outputs mirror each other under
    /// the repository, sharing a <c>bin/{configuration}/{framework}</c> tail, so swapping this app's project
    /// directory for the CLI's in its own output path lands on the CLI's build without naming a configuration
    /// or a framework version here that would quietly go stale on the next SDK bump. Debug only: a shipped
    /// server has no source tree around it to guess at.
    ///
    /// Nothing is built on the CLI's behalf. If it has never been built the path simply isn't there, which is
    /// the same nothing-to-advertise the publish-less case already handles.
    /// </remarks>
    private static string? FindInCliProjectOutput(string fileName)
    {
        const string WEB_PROJECT_DIRECTORY = "NekoTrace.Web";
        const string CLI_PROJECT_DIRECTORY = "NekoTrace.Cli";

        var segments = AppContext.BaseDirectory.Split(
            System.IO.Path.DirectorySeparatorChar
        );

        // The last such segment rather than the first: a checkout can sit anywhere, including under a
        // directory of the same name. Case-insensitively, since a path typed at `dotnet run` keeps the
        // casing it was typed in on Windows — File.Exists is what decides, so being generous costs nothing.
        var projectIndex = Array.FindLastIndex(
            segments,
            segment =>
                string.Equals(
                    segment,
                    WEB_PROJECT_DIRECTORY,
                    StringComparison.OrdinalIgnoreCase
                )
        );

        if (projectIndex < 0)
        {
            return null;
        }

        segments[projectIndex] = CLI_PROJECT_DIRECTORY;

        var candidate = System.IO.Path.Combine(
            string.Join(System.IO.Path.DirectorySeparatorChar, segments),
            fileName
        );

        return File.Exists(candidate) ? candidate : null;
    }
#endif
}
