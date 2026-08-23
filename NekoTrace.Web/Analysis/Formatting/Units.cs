namespace NekoTrace.Web.Analysis.Formatting;

using System.Globalization;

/// <summary>
/// Renders times the way the rest of NekoTrace does — <c>SpanData.DurationText</c>'s µs/ms/s scale — but for
/// the loose milliseconds the analysis types deal in rather than for a span.
/// </summary>
internal static class Units
{
    /// <summary>A duration, at three significant figures or so, with its unit attached.</summary>
    public static string Duration(double milliseconds) =>
        milliseconds switch
        {
            < 0 => "-" + Duration(-milliseconds),
            < 1 => Math.Round(milliseconds * 1000, 1).ToString(CultureInfo.InvariantCulture) + "µs",
            >= 60_000 => Math.Round(milliseconds / 60_000, 2).ToString(CultureInfo.InvariantCulture) + "m",
            >= 1000 => Math.Round(milliseconds / 1000, 2).ToString(CultureInfo.InvariantCulture) + "s",
            _ => Math.Round(milliseconds, 1).ToString(CultureInfo.InvariantCulture) + "ms",
        };

    /// <summary>An offset from the start of the trace, signed so it cannot be read as a duration.</summary>
    public static string Offset(double milliseconds) =>
        milliseconds <= 0 ? "+0" : "+" + Duration(milliseconds);

    /// <summary>
    /// Bare milliseconds, no unit and no grouping, for <see cref="FlatFormatter"/>'s numeric columns.
    /// </summary>
    /// <remarks>
    /// <see cref="Duration"/> is for reading and picks a unit per value, which is exactly what stops the
    /// column being sortable: <c>340ms</c> sorts above <c>1.2s</c> under every numeric sort there is. A format
    /// whose whole purpose is being fed to <c>sort</c> and <c>awk</c> prints one unit and says which in the
    /// column name.
    /// </remarks>
    public static string Milliseconds(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Percent(double value) =>
        Math.Round(value, value < 10 ? 1 : 0).ToString(CultureInfo.InvariantCulture) + "%";

    public static string Count(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A count with no grouping separator, for the flat format's columns.</summary>
    public static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An absolute timestamp, always UTC and always ISO 8601.
    /// </summary>
    /// <remarks>
    /// The read API converts to no other zone and offers no way to ask it to. Nothing here has a browser to
    /// ask, so a zone could only come from the host — which in a container means nothing — or from a caller
    /// that already knows the zone and can therefore convert perfectly well itself. Zone rules also change,
    /// and a server carrying a stale tzdata is worse than one that never claims to know. The UI's
    /// browser-zone rule is about rendering to a person; this is not that.
    /// The format round trips into the <c>startedAfter</c> and <c>startedBefore</c> filters unchanged.
    /// </remarks>
    public static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
