#pragma warning disable CA1031 // Do not catch general exception types

namespace NekoTrace.Web.Repositories.Traces;

using Microsoft.Extensions.Configuration;
using NekoTrace.Web.Configuration;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

public sealed class TraceDiskWriter : IAsyncDisposable
{
    private readonly TracesRepository mTracesRepository;
    private readonly IConfiguration mConfiguration;

    private readonly ConcurrentDictionary<string, TraceWriteState> mTrackedTraces = new();

    private Task? mLoopTask;
    private CancellationTokenSource mDisposalCancellationTokenSource = new();
    private bool mIsDisposed;

    private string? mLastTraceSaveFilterRaw;
    private TraceFilter mParsedTraceSaveFilter = TraceFilter.Empty;

    public TraceDiskWriter(TracesRepository tracesRepository, IConfiguration configuration)
    {
        mTracesRepository = tracesRepository;
        mConfiguration = configuration;
    }

    public Task Start()
    {
        mLoopTask = Task.Run(
            async () =>
            {
                try
                {
                    while (!mDisposalCancellationTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(
                            NekoTraceConfiguration.Get(mConfiguration).TraceSaveInterval,
                            mDisposalCancellationTokenSource.Token
                        );

                        await this.Timer_Tick();
                    }
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException
                        or OperationCanceledException
                        or ObjectDisposedException)
                    {
                        return;
                    }

                    await Console.Error.WriteLineAsync(ex.ToString());
                }
            }
        );

        return mLoopTask;
    }

    private async ValueTask Timer_Tick()
    {
        var config = NekoTraceConfiguration.Get(mConfiguration);
        var saveDirectory = config.TraceSaveDirectory;

        if (string.IsNullOrWhiteSpace(saveDirectory))
        {
            return;
        }

        // Parse TraceSaveFilter if changed
        if (!string.Equals(config.TraceSaveFilter, mLastTraceSaveFilterRaw, StringComparison.Ordinal))
        {
            mParsedTraceSaveFilter = TraceFilter.Parse(config.TraceSaveFilter);
            mLastTraceSaveFilterRaw = config.TraceSaveFilter;
        }

        var filter = mParsedTraceSaveFilter;

        if (string.IsNullOrWhiteSpace(saveDirectory))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
                await Console.Out.WriteLineAsync($"[TraceDiskWriter] Created trace save directory: {saveDirectory}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"[TraceDiskWriter] Failed to create directory {saveDirectory}: {ex.Message}");
            return;
        }

        // Remove any tracked traces that no longer exist in the repository
        foreach (var oldTrace in mTrackedTraces.Where(e => mTracesRepository.TryGetTrace(e.Key) is null).ToArray())
        {
            mTrackedTraces.Remove(oldTrace.Key, out _);
        }

        await Parallel.ForEachAsync(
            mTracesRepository.Traces,
            async (trace, _) =>
            {
                var state = mTrackedTraces.GetOrAdd(
                        trace.Id,
                        _ => new TraceWriteState()
                        {
                            Trace = trace,
                        }
                    );

                // If the trace is definitively rejected and we have a file, delete it
                if (filter.IsRejected(trace) && state.Filename is not null)
                {
                    var rejectedFilePath = Path.Combine(saveDirectory, state.Filename);
                    try
                    {
                        File.Delete(rejectedFilePath);
                        await Console.Out.WriteLineAsync($"[TraceDiskWriter] Deleted rejected trace file: {state.Filename}");
                    }
                    catch
                    {
                        // Best effort
                    }

                    mTrackedTraces.TryRemove(new KeyValuePair<string, TraceWriteState>(trace.Id, state));
                    return;
                }

                // If the trace doesn't match the filter, skip writing
                if (!filter.Matches(trace))
                {
                    return;
                }

                if (state.LastWrittenSpanCount == trace.Spans.Count)
                {
                    return;
                }

                var possibleFileName = GenerateFilename(trace);
                if (state.Filename is null || !string.Equals(possibleFileName, state.Filename, StringComparison.Ordinal))
                {
                    if (state.Filename is not null)
                    {
                        // Better name found for trace (root span has arrived)
                        try
                        {
                            File.Delete(Path.Combine(saveDirectory, state.Filename));
                        }
                        catch
                        {
                            // Nothing to do
                        }
                    }

                    state.Filename = possibleFileName;
                }

                var fullPath = Path.Combine(saveDirectory, state.Filename);
                var tempPath = fullPath + ".tmp";

                try
                {
                    var data = new TraceSerializableData()
                    {
                        Id = trace.Id,
                        Spans = [.. trace.Spans]
                    };

                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        using (var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize, leaveOpen: true))
                        {
                            await JsonSerializer.SerializeAsync(gzipStream, data, cancellationToken: CancellationToken.None);
                        }
                    }

                    File.Move(tempPath, fullPath, overwrite: true);

                    state.LastWrittenSpanCount = trace.Spans.Count;
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Nothing to do
                    }

                    await Console.Error.WriteLineAsync($"[TraceDiskWriter] Failed to write trace {trace.Id}. {ex.Message}");
                }
            }
        );
    }

    private static string GenerateFilename(TraceItem trace)
    {
        var timestamp = trace.Start.ToString("yyMMddTHHmmss", CultureInfo.InvariantCulture);
        var rootName = trace.RootSpan?.Name ?? "UnknownTrace";
        var safeRootName = SanitizeFilename(rootName);
        var safeId = SanitizeFilename(trace.Id);

        var shortId = safeId.Length > 16 ? safeId.Substring(0, 16) : safeId;

        return $"NekoTrace-{timestamp}-{safeRootName}-{shortId}.json.gz";
    }

    private static string SanitizeFilename(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "unknown";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            input.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()
        );

        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized;
    }

    public async ValueTask DisposeAsync()
    {
        if (mIsDisposed)
        {
            return;
        }

        mIsDisposed = true;

        await mDisposalCancellationTokenSource.CancelAsync();

        await (mLoopTask ?? Task.CompletedTask);

        mDisposalCancellationTokenSource.Dispose();

        await this.Timer_Tick();
    }

    private sealed record TraceWriteState
    {
        public required TraceItem Trace { get; init; }

        public int LastWrittenSpanCount { get; set; }

        public string? Filename { get; set; }
    }
}
