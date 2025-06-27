namespace InfoCat.Web.Repositories;

using OpenTelemetry.Proto.Common.V1;
using System.Collections.Immutable;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

public sealed record SpanData
{
    public required string Id { get; init; }
    
    public required string? ParentSpanId { get; init; }

    public required string Name { get; init; }

    public required SpanKind Kind { get; init; }
    
    public required Dictionary<string, AnyValue> Attributes { get; init; }
    
    public required DateTimeOffset StartTime { get; init; }
    
    public required DateTimeOffset EndTime { get; init; }
    
    public required StatusCode StatusCode { get; init; }
    
    public required string? StatusMessage { get; init; }
    
    public required string? TraceState { get; init; }
    
    public required ImmutableArray<SpanEvent> Events { get; init; }
    
    public required uint Flags { get; init; }
    
    
    public required ImmutableArray<Dictionary<string, AnyValue>> Links { get; init; }
}