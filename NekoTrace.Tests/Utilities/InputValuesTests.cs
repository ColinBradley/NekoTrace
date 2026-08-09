namespace NekoTrace.Tests.Utilities;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using NekoTrace.Web.Utilities;
using System.Globalization;
using Xunit;

public sealed class InputValuesTests
{
    // German groups with a dot and separates decimals with a comma, so it reads every invariant number wrongly
    // rather than failing to read it — which is what makes this worth a test.
    private static readonly CultureInfo sCommaCulture = new("de-DE");

    [Fact]
    public void TryParseDouble_ReadsAnInvariantValueUnderACommaCulture()
    {
        // The regression this defends: request localization sets CurrentCulture from Accept-Language, and a bare
        // double.TryParse then reads the "1.5" a number input always sends as one thousand five hundred — under
        // de-DE the dot is a group separator. The duration filter silently became a thousand times too large.
        using (SwitchCultureTo(sCommaCulture))
        {
            Assert.Equal(1.5, InputValues.TryParseDouble("1.5"));
        }
    }

    [Fact]
    public void TryParseDouble_RejectsACommaDecimal()
    {
        // Nothing should ever send this: the browser localises what it *displays*, not what it reports. Refusing
        // it keeps the one accepted format honest rather than guessing between two readings of "1,5".
        using (SwitchCultureTo(sCommaCulture))
        {
            Assert.Null(InputValues.TryParseDouble("1,5"));
        }
    }

    [Fact]
    public void TryParseInt32_ReadsAnInvariantValueUnderACommaCulture()
    {
        using (SwitchCultureTo(sCommaCulture))
        {
            Assert.Equal(1500, InputValues.TryParseInt32("1500"));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a number")]
    public void TryParse_ReturnsNullForAnythingUnparseable(object? value)
    {
        Assert.Null(InputValues.TryParseDouble(value));
        Assert.Null(InputValues.TryParseInt32(value));
    }

    [Fact]
    public void QueryParametersAreWrittenInvariantly()
    {
        // The other half of the round trip, and the reason the parse above can stay invariant: the pages hand a
        // double straight to GetUriWithQueryParameter, and [SupplyParameterFromQuery] reads it back invariantly.
        // A cultured write would put "1,5" in the URL and the value would be dropped on the next load.
        var navigation = new TestNavigationManager();

        using (SwitchCultureTo(sCommaCulture))
        {
            var uri = navigation.GetUriWithQueryParameter("DurationMinimum", (double?)1.5);

            Assert.EndsWith("?DurationMinimum=1.5", uri, StringComparison.Ordinal);
        }
    }

    private static CultureScope SwitchCultureTo(CultureInfo culture) => new(culture);

    /// <summary>
    /// Sets <see cref="CultureInfo.CurrentCulture"/> for the duration of a test. Safe alongside the rest of the
    /// suite because the setter is backed by an async-local, so it doesn't reach xUnit's other parallel threads.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo mPrevious = CultureInfo.CurrentCulture;

        public CultureScope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = mPrevious;
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            this.Initialize("http://localhost:8347/", "http://localhost:8347/");
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
        }
    }
}
