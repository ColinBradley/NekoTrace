namespace NekoTrace.Web.Repositories.Traces;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads attribute values back as the CLR primitives ingest produces, rather than the
/// <see cref="JsonElement"/> System.Text.Json hands back for every <c>object</c> by default.
/// </summary>
/// <remarks>
/// Span attributes are <c>Dictionary&lt;string, object?&gt;</c>, so without this a trace read from a file
/// holds different CLR types than the identical trace read from OTLP. That is invisible to anything which
/// only calls ToString, which is most of the UI — but not to code that switches on the type, and it is the
/// sort of difference that quietly makes uploaded traces second-class.
/// </remarks>
internal sealed class AttributeValueJsonConverter : JsonConverter<object?>
{
    public override object? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            // OTLP keeps integer and double attributes apart, and so does ingest, so the distinction is
            // worth preserving. IntValue is a long, which is what an integral attribute must come back as.
            // The cast is load bearing: without it the conditional's type is the best common type of long
            // and double, so every integer is widened to double on its way into the box.
            JsonTokenType.Number => reader.TryGetInt64(out var integer)
                ? (object)integer
                : reader.GetDouble(),
            // Arrays and objects come from OTLP's ArrayValue and KvlistValue, which have no CLR equivalent
            // here to be restored to. Left as a JsonElement, which still renders through ToString.
            _ => JsonElement.ParseValue(ref reader),
        };

    public override void Write(
        Utf8JsonWriter writer,
        object? value,
        JsonSerializerOptions options
    )
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        // Dispatched on the runtime type, both to match what the default object handling writes and so this
        // converter isn't selected again and left recursing.
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
