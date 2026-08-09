namespace NekoTrace.Web.Analysis.Results;

/// <summary>A stretch inside the trace's window with no span running, offset from the trace's start.</summary>
internal sealed record DeadTime
{
    public required double StartMs { get; init; }

    public required double DurationMs { get; init; }
}
