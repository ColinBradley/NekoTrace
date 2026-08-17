namespace NekoTrace.Cli;

using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;

/// <summary>
/// One run of one subcommand: the server it talks to, the format it asked for, and anything
/// <c>--file</c> put there on the way in.
/// </summary>
/// <remarks>
/// The whole client is here because there is not much of one. NekoTrace does the analysis; this fetches the
/// answer and puts it on standard output without reading it. That is the point of the CLI being a thin HTTP
/// client rather than a second copy of the engine — a trace stays where it was collected, and asking four
/// more questions of a local server beats downloading it once.
/// </remarks>
internal sealed class Session : IDisposable
{
    private readonly HttpClient mHttp;

    private Session(HttpClient http, string format)
    {
        mHttp = http;
        this.Format = format;
    }

    public string Format { get; }

    /// <summary>The trace <c>--file</c> loaded, or null when there was no <c>--file</c>.</summary>
    public string? UploadedTraceId { get; private set; }

    public static async Task<Session> OpenAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var address = parseResult.GetValue(GlobalOptions.Server) ?? GlobalOptions.DEFAULT_SERVER;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var server) || !server.Scheme.StartsWith("http", StringComparison.Ordinal))
        {
            throw new CliException(
                "--server: '" + address + "' is not an http address. It wants a whole one, like "
                + GlobalOptions.DEFAULT_SERVER + "."
            );
        }

        var http = new HttpClient()
        {
            BaseAddress = new Uri(server.GetLeftPart(UriPartial.Authority) + "/"),

            // Well past anything local, but bounded: ingesting the 230,313 span trace out of a file takes
            // real time, and a caller staring at a stalled pipe should eventually be told rather than left.
            Timeout = TimeSpan.FromMinutes(10),
        };

        var session = new Session(http, parseResult.GetValue(GlobalOptions.Format) ?? "text");

        if (parseResult.GetValue(GlobalOptions.File) is { } file)
        {
            session.UploadedTraceId = await session.UploadAsync(file, cancellationToken);
        }

        return session;
    }

    /// <summary>
    /// The trace to work on: the one named, else the one <c>--file</c> just uploaded.
    /// </summary>
    public string RequireTraceId(string? given)
    {
        if (given is { Length: > 0 })
        {
            return given;
        }

        return this.UploadedTraceId
            ?? throw new CliException(
                "No trace id. Give one, or --file a saved trace to work on. `NekoTrace.Cli traces` lists what "
                + "the server is holding."
            );
    }

    /// <summary>
    /// Fetches one endpoint and copies the body to standard output as it arrives.
    /// </summary>
    /// <remarks>
    /// Streamed rather than read into a string: flat over the 230,313 span trace is tens of megabytes, and
    /// the caller is piping it somewhere that can start work on line one. The bytes go to the standard output
    /// stream unaltered, so what a pipe receives is exactly what the server sent.
    /// </remarks>
    public async Task<int> WriteAsync(string path, Query query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path + this.WithFormat(query));
        using var response = await this.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return await RefusedAsync(
                await response.Content.ReadAsStringAsync(cancellationToken),
                response.ReasonPhrase
            );
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = Console.OpenStandardOutput();

        await body.CopyToAsync(output, cancellationToken);

        return ExitCodes.OK;
    }

    public void Dispose()
    {
        mHttp.Dispose();
    }

    private Query WithFormat(Query query) =>
        query.Add("format", this.Format);

    private async Task<string?> UploadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();

        await using var contents = file.OpenRead();
        using var filePart = new StreamContent(contents);

        filePart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // The field name the UI's import form uses. The endpoint reads every file on the form whatever they
        // are called, but matching it keeps the two paths the same one.
        form.Add(filePart, "spans", file.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/trace-files") { Content = form };

        // Which is also what tells the endpoint to answer with the ids rather than the 204 the browser's
        // import form needs. See TraceFilesController.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await this.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var reason = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new CliException(
                "--file: " + file.Name + " was not accepted. "
                + (string.IsNullOrWhiteSpace(reason) ? response.ReasonPhrase : reason.Trim())
            );
        }

        var ids = await response.Content.ReadFromJsonAsync<string[]>(cancellationToken);

        if (ids is not { Length: > 0 })
        {
            throw new CliException(
                "--file: " + file.Name + " held no trace. A trace file is the JSON the UI's download button "
                + "or NekoTrace's own trace directory writes, gzipped or not."
            );
        }

        // To standard error on purpose: standard output is the answer to the command, and a note about the
        // upload landing in the middle of it would be one line of prose in something being parsed.
        await Console.Error.WriteLineAsync(
            "# uploaded " + file.Name + " — trace " + string.Join(", ", ids)
        );

        return ids[0];
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await mHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException failure)
        {
            throw new CliException(
                "Nothing answered at " + mHttp.BaseAddress + " (" + failure.Message + "). NekoTrace has to "
                + "be running for this to have anything to ask: start it, or point --server at where it is.",
                ExitCodes.UNREACHABLE
            );
        }
        catch (TaskCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliException(
                "Timed out waiting for " + mHttp.BaseAddress + " (" + failure.Message + ").",
                ExitCodes.UNREACHABLE
            );
        }
    }

    private static async Task<int> RefusedAsync(string body, string? reasonPhrase)
    {
        await Console.Error.WriteLineAsync(string.IsNullOrWhiteSpace(body) ? reasonPhrase : body.TrimEnd());

        return ExitCodes.REFUSED;
    }
}
