namespace NekoTrace.Web.Utilities;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

// The OTLP/JSON encoding sends trace_id, span_id and parent_span_id as lowercase hex strings, but
// Google.Protobuf's JSON parser decodes every `bytes` field as base64. A 32 character hex trace id is itself
// valid base64, so it silently decodes into the wrong 24 bytes rather than failing. Rewriting those fields to
// base64 before handing the body to the protobuf parser gets us the ids the sender actually meant.
// See https://opentelemetry.io/docs/specs/otlp/#json-protobuf-encoding.
internal static class OtlpJsonIdNormalizer
{
    // Both the lowerCamelCase and the original proto spellings, since the protobuf JSON parser accepts either.
    private static readonly Dictionary<string, int> sIdByteLengthsByFieldName =
        new(StringComparer.Ordinal)
        {
            ["traceId"] = TraceIds.TRACE_ID_BYTE_LENGTH,
            ["trace_id"] = TraceIds.TRACE_ID_BYTE_LENGTH,
            ["spanId"] = TraceIds.SPAN_ID_BYTE_LENGTH,
            ["span_id"] = TraceIds.SPAN_ID_BYTE_LENGTH,
            ["parentSpanId"] = TraceIds.SPAN_ID_BYTE_LENGTH,
            ["parent_span_id"] = TraceIds.SPAN_ID_BYTE_LENGTH,
        };

    /// <summary>
    /// Returns <paramref name="json"/> with any hex encoded id fields replaced by their base64 equivalent.
    /// Ids that are already base64, and bodies that don't parse as JSON, are returned untouched so the
    /// protobuf parser stays the one that decides what is valid.
    /// </summary>
    public static string NormalizeIds(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        return root is not null && Normalize(root)
            ? root.ToJsonString()
            : json;
    }

    private static bool Normalize(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                return NormalizeObject(jsonObject);

            case JsonArray jsonArray:
                var arrayChanged = false;

                foreach (var item in jsonArray)
                {
                    if (item is not null)
                    {
                        arrayChanged |= Normalize(item);
                    }
                }

                return arrayChanged;

            default:
                return false;
        }
    }

    private static bool NormalizeObject(JsonObject jsonObject)
    {
        var changed = false;

        // Replacements are applied after enumerating, as mutating a JsonObject while iterating it throws.
        List<KeyValuePair<string, string>>? replacements = null;

        foreach (var property in jsonObject)
        {
            if (property.Value is null)
            {
                continue;
            }

            if (sIdByteLengthsByFieldName.TryGetValue(property.Key, out var byteLength)
                && property.Value is JsonValue value
                && value.TryGetValue<string>(out var text)
                && TryConvertHexToBase64(text, byteLength, out var base64)
            )
            {
                (replacements ??= []).Add(new(property.Key, base64));
                continue;
            }

            changed |= Normalize(property.Value);
        }

        if (replacements is not null)
        {
            foreach (var (key, base64) in replacements)
            {
                jsonObject[key] = base64;
            }

            changed = true;
        }

        return changed;
    }

    private static bool TryConvertHexToBase64(
        string? value,
        int expectedByteLength,
        [NotNullWhen(true)] out string? base64
    )
    {
        // Anything of hex length was meant to be hex; anything else is left for the protobuf parser to judge.
        if (!TraceIds.IsHexId(value, expectedByteLength))
        {
            base64 = null;

            return false;
        }

        base64 = Convert.ToBase64String(Convert.FromHexString(value));

        return true;
    }
}
