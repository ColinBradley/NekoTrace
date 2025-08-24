﻿namespace NekoTrace.Web.Repositories;

using System.Collections.Immutable;

public sealed record Trace
{
    private readonly ReaderWriterLockSlim mLock = new();

    public required string Id { get; init; }

    public required TracesRepository Repository { get; init; }

    public ImmutableList<SpanData> Spans { get; private set; } = [];

    public ImmutableDictionary<string, SpanData> SpansById { get; private set; } = ImmutableDictionary.Create<string, SpanData>(StringComparer.Ordinal);

    public SpanData? RootSpan { get; private set; }

    public DateTimeOffset Start { get; private set; } = DateTimeOffset.MaxValue;

    public DateTimeOffset End { get; private set; } = DateTimeOffset.MinValue;

    public TimeSpan Duration { get; private set; }

    public bool HasError { get; private set; }

    public string? TryGetRootSpanAttribute(string name)
    {
        return this.RootSpan?.Attributes.TryGetValue(name, out var value) is true
            ? value switch
            {
                string stringValue => stringValue,
                bool v => v.ToString(),
                int v => v.ToString(),
                double v => v.ToString(),
                _ => null,
            }
            : null;
    }

    internal void AddSpan(SpanData span)
    {
        mLock.EnterWriteLock();

        this.AddSpanCore(span);

        mLock.ExitWriteLock();

        this.Repository.OnTraceChanged();
    }

    internal void AddSpans(IEnumerable<SpanData> spans)
    {
        mLock.EnterWriteLock();

        foreach (var span in spans)
        {
            this.AddSpanCore(span);
        }

        mLock.ExitWriteLock();

        this.Repository.OnTraceChanged();
    }

    private void AddSpanCore(SpanData span)
    {
        var insertIndex = this.Spans.FindLastIndex(s => s.StartTime < span.StartTime);

        this.Spans = insertIndex >= 0 ? this.Spans.Insert(insertIndex, span) : this.Spans.Add(span);
        this.SpansById = this.SpansById.SetItem(span.Id, span);

        this.HasError =
            this.HasError
            || span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error;

        if (string.IsNullOrEmpty(span.ParentSpanId))
        {
            this.RootSpan = span;
        }

        var durationChanged = false;
        if (span.StartTime < this.Start)
        {
            this.Start = span.StartTime;
            durationChanged = true;
        }

        if (span.EndTime > this.End)
        {
            this.End = span.EndTime;
            durationChanged = true;
        }

        if (durationChanged)
        {
            this.Duration = this.End - this.Start;
        }

        this.Repository.AddSpan(span);
    }
}
