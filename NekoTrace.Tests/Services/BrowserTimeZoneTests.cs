namespace NekoTrace.Tests.Services;

using Microsoft.AspNetCore.Http;
using NekoTrace.Web.Services;
using Xunit;

public sealed class BrowserTimeZoneTests
{
    // Europe/London is used throughout because it is UTC+0 in winter and UTC+1 in summer, so a test that only
    // ever passed by treating the zone as a fixed offset shows up as soon as both halves of the year appear.
    private const string LONDON = "Europe/London";

    // 12:07 UTC on a September day, which is 13:07 British Summer Time.
    private static readonly DateTimeOffset sSummerInstant = new(2025, 9, 4, 12, 7, 27, TimeSpan.Zero);

    [Fact]
    public void DefaultsToUtcWhenTheRequestCarriesNoCookie()
    {
        var timeZone = ForCookieHeader(null);

        Assert.Null(timeZone.Id);
        Assert.Equal(TimeZoneInfo.Utc, timeZone.TimeZone);
    }

    [Fact]
    public void ReadsThePercentEncodedCookieTheScriptWrites()
    {
        // scripts/timeZone.ts encodes the value, so the slash in every IANA id arrives as %2F.
        var timeZone = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}=Europe%2FLondon");

        Assert.Equal(LONDON, timeZone.Id);
    }

    [Fact]
    public void TrySet_KeepsTheCurrentZoneWhenTheIdIsUnknown()
    {
        var timeZone = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}={LONDON}");

        Assert.False(timeZone.TrySet("Middle/Earth"));
        Assert.Equal(LONDON, timeZone.Id);
    }

    [Fact]
    public void TrySet_ReportsNoChangeForTheZoneAlreadyHeld()
    {
        var timeZone = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}={LONDON}");

        // MainLayout asks the browser on every circuit and only re-renders on a change, so the usual answer —
        // the same zone the cookie already gave — has to come back as false.
        Assert.False(timeZone.TrySet(LONDON));
    }

    [Fact]
    public void FormatTimeOfDay_ShiftsIntoTheBrowsersZone()
    {
        var london = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}={LONDON}");
        var newYork = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}=America%2FNew_York");

        // Two zones again, so that neither leaving the instant in UTC nor reaching for the host's own zone can
        // satisfy this on whatever machine happens to run it.
        Assert.Equal("13:07:27.000", london.FormatTimeOfDay(sSummerInstant));
        Assert.Equal("08:07:27.000", newYork.FormatTimeOfDay(sSummerInstant));
    }

    [Fact]
    public void FormatTimeOfDay_LeavesInstantsAloneWhileTheZoneIsStillUtc()
    {
        var timeZone = ForCookieHeader(null);

        Assert.Equal("12:07:27.000", timeZone.FormatTimeOfDay(sSummerInstant));
    }

    [Fact]
    public void ParseLocalInput_AttachesTheBrowsersOffsetRatherThanTheServers()
    {
        // The regression this defends: a datetime-local input carries no offset, and parsing it straight into a
        // DateTimeOffset stamped it with whatever the *server* was set to. In a container that is UTC, so
        // filtering for 13:07 local silently filtered for 13:07 UTC — an hour out for anyone on BST.
        //
        // Two zones rather than one, because the old behaviour returns the host's offset both times: asserting
        // a single expected offset would still pass on a machine that happens to sit in that zone.
        var london = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}={LONDON}");
        var newYork = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}=America%2FNew_York");

        var inLondon = london.ParseInputToLocal("2025-09-04T13:07");
        var inNewYork = newYork.ParseInputToLocal("2025-09-04T13:07");

        Assert.Equal(TimeSpan.FromHours(1), inLondon!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), inNewYork!.Value.Offset);

        // The same wall clock text, so the instants they name are five hours apart.
        Assert.Equal(new DateTimeOffset(2025, 9, 4, 12, 7, 0, TimeSpan.Zero), inLondon.Value.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2025, 9, 4, 17, 7, 0, TimeSpan.Zero), inNewYork.Value.ToUniversalTime());
    }

    [Fact]
    public void ParseLocalInput_UsesTheOffsetInForceOnThatDate()
    {
        var london = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}={LONDON}");
        var newYork = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}=America%2FNew_York");

        // Same wall clock text six months apart: GMT in January and BST in July, so the zone cannot be reduced
        // to a fixed offset captured once. Both zones are checked so this still fails on a London host, where
        // the old server-offset behaviour would otherwise produce these very numbers by coincidence.
        Assert.Equal(TimeSpan.Zero, london.ParseInputToLocal("2025-01-04T13:07")!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(1), london.ParseInputToLocal("2025-07-04T13:07")!.Value.Offset);

        Assert.Equal(TimeSpan.FromHours(-5), newYork.ParseInputToLocal("2025-01-04T13:07")!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), newYork.ParseInputToLocal("2025-07-04T13:07")!.Value.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a time")]
    public void ParseLocalInput_ReturnsNullForAnythingUnparseable(string? value)
    {
        var timeZone = ForCookieHeader($"{BrowserTimeZone.COOKIE_NAME}={LONDON}");

        Assert.Null(timeZone.ParseInputToLocal(value));
    }

    private static BrowserTimeZone ForCookieHeader(string? cookieHeader)
    {
        var context = new DefaultHttpContext();
        if (cookieHeader is not null)
        {
            context.Request.Headers.Cookie = cookieHeader;
        }

        return new BrowserTimeZone(new HttpContextAccessor() { HttpContext = context });
    }
}
