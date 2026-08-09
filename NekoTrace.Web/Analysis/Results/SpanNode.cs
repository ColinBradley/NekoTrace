namespace NekoTrace.Web.Analysis.Results;

using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

/// <summary>
/// A span in its place in the trace's tree. Built by <see cref="SpanTree"/> and mutated only while that is
/// building; treat one as immutable once you have hold of a <see cref="SpanTree"/>.
/// </summary>
internal sealed class SpanNode
{
    internal SpanNode(SpanData span)
    {
        this.Span = span;
    }

    public SpanData Span { get; }

    public SpanNode? Parent { get; internal set; }

    /// <summary>Chronological, earliest start first.</summary>
    public ImmutableArray<SpanNode> Children { get; internal set; } = [];

    /// <summary>Zero for a forest top.</summary>
    public int Depth { get; internal set; }

    /// <summary>
    /// Duration minus the union of the child intervals — not minus their sum. These traces are heavily
    /// asynchronous, so children routinely overlap each other, and subtracting the sum takes most spans
    /// in <c>TestTraces/</c> below zero.
    /// </summary>
    public double SelfTimeMs { get; internal set; }

    /// <summary>
    /// True when the span named a parent that never arrived, which makes it a forest top despite having a
    /// parent id. The 230k span trace in <c>TestTraces/</c> has 4,043 of these, so this is the normal case
    /// for a partially collected trace rather than a corruption.
    /// </summary>
    public bool IsOrphan { get; internal set; }

    public double DurationMs => this.Span.EndTimeMs - this.Span.StartTimeMs;

    public bool HasError =>
        this.Span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error;
}
