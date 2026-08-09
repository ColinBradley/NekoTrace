namespace NekoTrace.Web.Services;

using System.Globalization;

/// <summary>
/// Getting the browser's time zone for server rendering.
/// </summary>
/// <remarks>
/// Scoped, so there is one of these per prerender and per circuit.
/// Populated from three places, in the order they become available:
/// Cookie <c>scripts/timeZone.ts</c> wrote on an earlier visit,
/// the prerendered value carried across by <see cref="UI.Layout.MainLayout"/>'s persisted state,
/// and finally the browser over JS interop.
/// </remarks>
public sealed class BrowserTimeZone
{
    public const string COOKIE_NAME = "nekotrace-time-zone";

    public BrowserTimeZone(IHttpContextAccessor httpContextAccessor)
    {
        this.TrySet(
            httpContextAccessor.HttpContext?.Request.Cookies[COOKIE_NAME]
        );
    }

    /// <summary>
    /// Raised when the zone changes. In practice this fires at most once per browser — every visit after the
    /// first starts from the cookie and the browser only confirms what is already set.
    /// </summary>
    public event Action? Changed;

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// The IANA id the zone came from, or null while it is still the UTC fallback.
    /// </summary>
    public string? Id { get; private set; }

    /// <summary>
    /// Adopts an IANA zone id as reported by <c>Intl.DateTimeFormat</c>, returning whether it changed anything.
    /// Unknown and malformed ids leave the current zone alone rather than throwing.
    /// </summary>
    public bool TrySet(string? ianaId)
    {
        if (string.IsNullOrEmpty(ianaId) || string.Equals(ianaId, this.Id, StringComparison.Ordinal))
        {
            return false;
        }

        TimeZoneInfo timeZone;
        try
        {
            // Since .NET 6 this takes IANA ids on any host OS, converting to the platform's own format when the
            // id isn't found natively, so no mapping table is needed to make this work on Windows.
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }

        this.Id = ianaId;
        this.TimeZone = timeZone;

        this.Changed?.Invoke();

        return true;
    }

    public DateTimeOffset ToBrowserTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, this.TimeZone);

    /// <summary>
    /// Formats the time of day of <paramref name="value"/> for a grid column. Deliberately invariant and 24
    /// hour: these columns are read by eye against each other, so fixed width and millisecond precision matter
    /// more than honouring a 12 hour preference.
    /// </summary>
    public string FormatTimeOfDay(DateTimeOffset value) =>
        this.ToBrowserTime(value).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the value of an <c>&lt;input type="datetime-local"&gt;</c>. Those carry no offset at all, and the
    /// browser means them in its own zone — so that is the offset to attach. Parsing them straight into a
    /// <see cref="DateTimeOffset"/> instead picks up the server's offset and silently shifts the filter.
    /// </summary>
    public DateTimeOffset? ParseInputToLocal(string? value)
    {
        // The value is always ISO-ish (yyyy-MM-ddTHH:mm) whatever the browser displays, hence invariant.
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        return new DateTimeOffset(parsed, this.TimeZone.GetUtcOffset(parsed));
    }
}
