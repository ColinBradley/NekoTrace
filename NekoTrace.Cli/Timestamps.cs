namespace NekoTrace.Cli;

using System.Globalization;

/// <summary>
/// Reads the instants the time filters take, and hands the server one unambiguous spelling of them.
/// </summary>
/// <remarks>
/// Parsed here rather than passed through for two reasons. A value the server cannot read is dropped by the
/// filter parsers without a word, so a typo would silently widen the query instead of failing; and a time with
/// no offset on it has to be read as UTC to mean the same thing on every machine, which is the rule the rest
/// of this API follows. Normalising to <c>…Z</c> before sending settles both, and
/// it is the same shape every NekoTrace timestamp is printed in, so output feeds back in unchanged.
/// </remarks>
internal static class Timestamps
{
    public static string? ToUtc(string? value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            throw new CliException(
                option + ": '" + value + "' is not a time this understands. Give it in ISO 8601, as "
                + "'2026-08-09T14:00:00Z' or '2026-08-09'. Without an offset it is read as UTC."
            );
        }

        return parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
