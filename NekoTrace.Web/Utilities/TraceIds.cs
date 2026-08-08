namespace NekoTrace.Web.Utilities;

using Google.Protobuf;
using System.Diagnostics.CodeAnalysis;

// Trace and span ids are stored and displayed as lowercase hex, matching the W3C Trace Context traceparent
// header and the OTLP/JSON encoding. That is the form every OpenTelemetry SDK writes into an application's own
// logs, so an id copied from there pastes straight into NekoTrace. It is also URL safe, unlike base64.
internal static class TraceIds
{
    public const int TRACE_ID_BYTE_LENGTH = 16;
    public const int SPAN_ID_BYTE_LENGTH = 8;

    public static string ToHex(ByteString id) => Convert.ToHexStringLower(id.Span);

    /// <summary>
    /// Returns <paramref name="value"/> as lowercase hex, accepting either hex or the base64 that
    /// NekoTrace wrote before it moved to hex. Anything that decodes as neither is passed through
    /// unchanged, so an unrecognised id still loads as a self-consistent opaque key.
    /// </summary>
    public static string NormalizeToHex(string value, int byteLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (IsHexId(value, byteLength))
        {
            // Round tripped rather than lowercased in place, since the OTLP spec allows either case.
            return Convert.ToHexStringLower(Convert.FromHexString(value));
        }

        Span<byte> bytes = stackalloc byte[TRACE_ID_BYTE_LENGTH];
        if (Convert.TryFromBase64String(value, bytes, out var written) && written == byteLength)
        {
            return Convert.ToHexStringLower(bytes[..written]);
        }

        return value;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is the hex form of an id of <paramref name="byteLength"/> bytes.
    /// Length is what separates hex from base64 — a 16 byte id is 32 hex characters but only 24 base64 ones,
    /// and an 8 byte id is 16 against 12 — so both halves of this check carry weight, and every caller that
    /// tells the two encodings apart must go through here rather than reimplementing the rule.
    /// </summary>
    public static bool IsHexId([NotNullWhen(true)] string? value, int byteLength)
    {
        if (value is null || value.Length != byteLength * 2)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
