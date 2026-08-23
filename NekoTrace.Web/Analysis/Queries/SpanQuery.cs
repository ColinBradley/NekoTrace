namespace NekoTrace.Web.Analysis.Queries;

using Microsoft.AspNetCore.Http;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Utilities;
using System.Globalization;
using System.IO.Enumeration;
using static OpenTelemetry.Proto.Trace.V1.Span.Types;

/// <summary>
/// Which spans an endpoint should return. The span level counterpart to <c>TraceFilter</c>, and parsed the
/// same way — out of a query string, ignoring anything it cannot read rather than erroring.
/// </summary>
/// <remarks>
/// Flag shaped on purpose. A TraceQL style expression language is a cost paid by every caller, and filtering
/// on name, duration, status, kind and an attribute pair covers what the endpoints here are for.
/// </remarks>
internal sealed record SpanQuery
{
    public static readonly SpanQuery Empty = new();

    /// <summary>Wildcard pattern over the span name — <c>*</c> and <c>?</c>, case insensitive.</summary>
    public string? Name { get; init; }

    /// <summary>Seconds, matching the units <c>TraceFilter</c> uses for the same thing.</summary>
    public double? DurationMinimum { get; init; }

    public double? DurationMaximum { get; init; }

    public bool? HasError { get; init; }

    public SpanKind? Kind { get; init; }

    /// <summary>Only spans that started at or after this instant.</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>Only spans that started at or before this instant.</summary>
    public DateTimeOffset? StartedBefore { get; init; }

    public AttributeMatcher Attributes { get; init; } = AttributeMatcher.Empty;

    public bool IsEmpty =>
        this.Name is null
        && this.DurationMinimum is null
        && this.DurationMaximum is null
        && this.HasError is null
        && this.Kind is null
        && this.StartedAfter is null
        && this.StartedBefore is null
        && this.Attributes.IsEmpty;

    public static SpanQuery Parse(IQueryCollection query)
    {
        var result = new SpanQuery();

        if (query.TryGetValue("name", out var name) && name.FirstOrDefault() is { Length: > 0 } namePattern)
        {
            result = result with { Name = namePattern };
        }

        // Durations go through InputValues rather than a bare TryParse: request localization sets
        // CurrentCulture from the caller's Accept-Language, and a query string is invariant whatever that
        // says — a bare parse would read '1.5' as fifteen hundred for anyone grouping with a dot, silently.
        if (
            query.TryGetValue("minDuration", out var minimum)
            && InputValues.TryParseDouble(minimum.FirstOrDefault()) is { } minimumSeconds
        )
        {
            result = result with { DurationMinimum = minimumSeconds };
        }

        if (
            query.TryGetValue("maxDuration", out var maximum)
            && InputValues.TryParseDouble(maximum.FirstOrDefault()) is { } maximumSeconds
        )
        {
            result = result with { DurationMaximum = maximumSeconds };
        }

        if (query.TryGetValue("hasError", out var hasError) && bool.TryParse(hasError.FirstOrDefault(), out var error))
        {
            result = result with { HasError = error };
        }

        if (
            query.TryGetValue("kind", out var kind)
            && Enum.TryParse<SpanKind>(kind.FirstOrDefault(), ignoreCase: true, out var parsedKind)
        )
        {
            result = result with { Kind = parsedKind };
        }

        if (
            query.TryGetValue("startedAfter", out var after)
            && DateTimeOffset.TryParse(
                after.FirstOrDefault(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedAfter
            )
        )
        {
            result = result with { StartedAfter = startedAfter };
        }

        if (
            query.TryGetValue("startedBefore", out var before)
            && DateTimeOffset.TryParse(
                before.FirstOrDefault(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedBefore
            )
        )
        {
            result = result with { StartedBefore = startedBefore };
        }

        if (query.TryGetValue("attributeFilter", out var attributes))
        {
            result = result with { Attributes = AttributeMatcher.Parse(attributes.FirstOrDefault()) };
        }

        return result;
    }

    /// <summary>
    /// A timestamp from a caller, read as UTC when it carries no offset of its own. There is no browser on
    /// this side of the app to supply a zone, and reading the host's would make the same request mean
    /// different things on different machines.
    /// </summary>
    public static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed
        )
            ? parsed
            : null;

    public bool Matches(SpanData span)
    {
        if (
            this.Name is { } pattern
            && !FileSystemName.MatchesSimpleExpression(pattern, span.Name, ignoreCase: true)
        )
        {
            return false;
        }

        var durationSeconds = span.Duration.TotalSeconds;

        if (this.DurationMinimum is { } minimum && durationSeconds < minimum)
        {
            return false;
        }

        if (this.DurationMaximum is { } maximum && durationSeconds > maximum)
        {
            return false;
        }

        if (
            this.HasError is { } hasError
            && hasError != (span.StatusCode is OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error)
        )
        {
            return false;
        }

        if (this.Kind is { } kind && span.Kind != kind)
        {
            return false;
        }

        if (this.StartedAfter is { } after && span.StartTime < after)
        {
            return false;
        }

        if (this.StartedBefore is { } before && span.StartTime > before)
        {
            return false;
        }

        return this.Attributes.IsEmpty || this.Attributes.Matches(span);
    }
}
