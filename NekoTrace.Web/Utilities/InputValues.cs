namespace NekoTrace.Web.Utilities;

using System.Globalization;

/// <summary>
/// Reads the values HTML form controls hand back, which are always invariant however the browser displays them:
/// an <c>&lt;input type="number"&gt;</c> showing a German user <c>1,5</c> still reports <c>1.5</c>.
/// </summary>
/// <remarks>
/// Worth a helper rather than an argument at each call site, because the failure is silent. Request localization
/// sets <see cref="CultureInfo.CurrentCulture"/> to the viewer's, so a bare <c>double.TryParse</c> reads that
/// same <c>1.5</c> as fifteen for anyone whose culture groups with a dot — a filter that quietly does the wrong
/// thing rather than an exception anybody would notice.
/// </remarks>
internal static class InputValues
{
    public static double? TryParseDouble(object? value) =>
        double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static int? TryParseInt32(object? value) =>
        int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
