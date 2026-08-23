namespace NekoTrace.Web.Repositories.Traces;

using Microsoft.AspNetCore.WebUtilities;
using System.Collections.Immutable;
using System.Globalization;

public sealed record TraceFilter
{
    public static readonly TraceFilter Empty = new();

    public int? SpansMinimum { get; init; }

    public double? DurationMinimum { get; init; }

    public double? DurationMaximum { get; init; }

    public bool? HasError { get; init; }

    public ImmutableHashSet<string> IgnoredTraceNames { get; init; } = ImmutableHashSet<string>.Empty;

    public ImmutableHashSet<string>? ExclusiveTraceNames { get; init; }

    public ImmutableDictionary<string, string> SpanAttributeFilter { get; init; } = ImmutableDictionary<string, string>.Empty;

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public bool IsEmpty =>
        this.SpansMinimum is null
        && this.DurationMinimum is null
        && this.DurationMaximum is null
        && this.HasError is null
        && this.IgnoredTraceNames.IsEmpty
        && this.ExclusiveTraceNames is null
        && this.SpanAttributeFilter.IsEmpty
        && this.StartTime is null
        && this.EndTime is null;

    public bool Matches(TraceItem trace)
    {
        if (this.SpansMinimum.HasValue && trace.Spans.Length < this.SpansMinimum.Value)
        {
            return false;
        }

        if (this.DurationMinimum.HasValue && trace.Duration.TotalSeconds < this.DurationMinimum.Value)
        {
            return false;
        }

        if (this.DurationMaximum.HasValue && trace.Duration.TotalSeconds > this.DurationMaximum.Value)
        {
            return false;
        }

        if (this.HasError.HasValue && trace.HasError != this.HasError.Value)
        {
            return false;
        }

        if (this.StartTime.HasValue && trace.Start <= this.StartTime.Value)
        {
            return false;
        }

        if (this.EndTime.HasValue && trace.Start >= this.EndTime.Value)
        {
            return false;
        }

        // IgnoredTraceNames - pass when RootSpan is null or name not in ignored list
        if (trace.RootSpan != null
            && !this.IgnoredTraceNames.IsEmpty
            && this.IgnoredTraceNames.Contains(trace.RootSpan.Name))
        {
            return false;
        }

        // ExclusiveTraceNames - when set, require non-null root span with name in the set
        if (this.ExclusiveTraceNames is not null
            && (trace.RootSpan is null || !this.ExclusiveTraceNames.Contains(trace.RootSpan.Name)))
        {
            return false;
        }

        // SpanAttributeFilter - match if any span has any filter pair (OrdinalIgnoreCase)
        if (!this.SpanAttributeFilter.IsEmpty)
        {
            var hasMatch = trace.Spans.Any(span =>
                this.SpanAttributeFilter.Any(filterPair =>
                    span.Attributes.TryGetValue(filterPair.Key, out var spanAttributeValue)
                    && string.Equals(
                        filterPair.Value,
                        spanAttributeValue?.ToString(),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );

            if (!hasMatch)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsRejected(TraceItem trace)
    {
        if (trace.RootSpan != null
            && !this.IgnoredTraceNames.IsEmpty
            && this.IgnoredTraceNames.Contains(trace.RootSpan.Name))
        {
            return true;
        }

        if (this.ExclusiveTraceNames is not null
            && trace.RootSpan is not null
            && !this.ExclusiveTraceNames.Contains(trace.RootSpan.Name))
        {
            return true;
        }

        if (this.DurationMaximum.HasValue && trace.Duration.TotalSeconds > this.DurationMaximum.Value)
        {
            return true;
        }

        if (this.StartTime.HasValue && trace.Start != DateTimeOffset.MaxValue && trace.Start <= this.StartTime.Value)
        {
            return true;
        }

        if (this.EndTime.HasValue && trace.Start != DateTimeOffset.MaxValue && trace.Start >= this.EndTime.Value)
        {
            return true;
        }

        if (this.HasError == false && trace.HasError)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a filter out of a query string.
    /// </summary>
    /// <remarks>
    /// Every value is parsed with the invariant culture, and times with no offset of their own are read as
    /// UTC. None of the three callers — <c>GET /api/traces</c>, <c>TraceIngestFilter</c> and
    /// <c>TraceSaveFilter</c> — has a browser to ask, so the host's culture and zone are the only other
    /// candidates and both make the same string mean different things on different machines. The UI does not
    /// come through here: it builds a filter directly through <c>BrowserTimeZone.ParseInputToLocal</c>, which
    /// is where the viewer's zone belongs.
    /// </remarks>
    public static TraceFilter Parse(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return Empty;
        }

        if (queryString.StartsWith('?'))
        {
            queryString = queryString[1..];
        }

        var query = QueryHelpers.ParseQuery(queryString);

        var filter = new TraceFilter();

        if (query.TryGetValue("SpansMinimum", out var spansMinimumValue)
            && int.TryParse(spansMinimumValue.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var spansMinimum)
            && spansMinimum > 0)
        {
            filter = filter with { SpansMinimum = spansMinimum };
        }

        if (query.TryGetValue("DurationMinimum", out var durationMinimumValue)
            && double.TryParse(durationMinimumValue.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationMinimum)
            && durationMinimum > 0)
        {
            filter = filter with { DurationMinimum = durationMinimum };
        }

        if (query.TryGetValue("DurationMaximum", out var durationMaximumValue)
            && double.TryParse(durationMaximumValue.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationMaximum)
            && durationMaximum > 0)
        {
            filter = filter with { DurationMaximum = durationMaximum };
        }

        if (query.TryGetValue("HasError", out var hasErrorValue)
            && bool.TryParse(hasErrorValue.FirstOrDefault(), out var hasError))
        {
            filter = filter with { HasError = hasError };
        }

        if (query.TryGetValue("IgnoredTraceNames", out var ignoredTraceNamesValue))
        {
            var names = ignoredTraceNamesValue.FirstOrDefault();
            if (!string.IsNullOrEmpty(names))
            {
                filter = filter with
                {
                    IgnoredTraceNames = names.Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .ToImmutableHashSet(StringComparer.Ordinal)
                };
            }
        }

        if (query.TryGetValue("ExclusiveTraceNames", out var exclusiveTraceNamesValue))
        {
            var names = exclusiveTraceNamesValue.FirstOrDefault();
            if (!string.IsNullOrEmpty(names))
            {
                filter = filter with
                {
                    ExclusiveTraceNames = names.Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .ToImmutableHashSet(StringComparer.Ordinal)
                };
            }
        }

        if (query.TryGetValue("SpanAttributeFilter", out var spanAttributeFilterValue))
        {
            var filterString = spanAttributeFilterValue.FirstOrDefault();
            if (!string.IsNullOrEmpty(filterString))
            {
                var pairs = filterString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .Select(parts => new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()))
                    .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
                    .DistinctBy(kvp => kvp.Key, StringComparer.Ordinal);

                filter = filter with
                {
                    SpanAttributeFilter = pairs.ToImmutableDictionary(StringComparer.Ordinal)
                };
            }
        }

        if (query.TryGetValue("StartTime", out var startTimeValue)
            && TryParseTimestamp(startTimeValue.FirstOrDefault(), out var startTime))
        {
            filter = filter with { StartTime = startTime };
        }

        if (query.TryGetValue("EndTime", out var endTimeValue)
            && TryParseTimestamp(endTimeValue.FirstOrDefault(), out var endTime))
        {
            filter = filter with { EndTime = endTime };
        }

        return filter;
    }

    /// <summary>A time with no offset of its own is UTC, not the host machine's idea of local.</summary>
    private static bool TryParseTimestamp(string? value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed
        );
}
