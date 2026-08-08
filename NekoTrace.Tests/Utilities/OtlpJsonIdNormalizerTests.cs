namespace NekoTrace.Tests.Utilities;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Utilities;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

/// <summary>
/// Regression cover for f909517. Google.Protobuf decodes every <c>bytes</c> field as base64, but OTLP/JSON
/// sends ids as hex — and a 32 character hex trace id is itself valid base64, so it used to decode into the
/// wrong 24 bytes silently instead of failing.
/// </summary>
public sealed class OtlpJsonIdNormalizerTests
{
    private static readonly string sTraceIdBase64 = Fake.ToBase64(Otlp.TRACE_ID);
    private static readonly string sRootSpanIdBase64 = Fake.ToBase64(Otlp.ROOT_SPAN_ID);
    private static readonly string sChildSpanIdBase64 = Fake.ToBase64(Otlp.CHILD_SPAN_ID);

    [Fact]
    public void RewritesHexIdsToBase64()
    {
        var normalized = Normalize(
            $$"""
            {
                "traceId": "{{Otlp.TRACE_ID}}",
                "spanId": "{{Otlp.CHILD_SPAN_ID}}",
                "parentSpanId": "{{Otlp.ROOT_SPAN_ID}}"
            }
            """
        );

        Assert.Equal(sTraceIdBase64, (string?)normalized["traceId"]);
        Assert.Equal(sChildSpanIdBase64, (string?)normalized["spanId"]);
        Assert.Equal(sRootSpanIdBase64, (string?)normalized["parentSpanId"]);
    }

    [Fact]
    public void RewritesSnakeCaseFieldNames()
    {
        // The protobuf JSON parser accepts the original proto spellings as well as lowerCamelCase.
        var normalized = Normalize(
            $$"""
            {
                "trace_id": "{{Otlp.TRACE_ID}}",
                "span_id": "{{Otlp.CHILD_SPAN_ID}}",
                "parent_span_id": "{{Otlp.ROOT_SPAN_ID}}"
            }
            """
        );

        Assert.Equal(sTraceIdBase64, (string?)normalized["trace_id"]);
        Assert.Equal(sChildSpanIdBase64, (string?)normalized["span_id"]);
        Assert.Equal(sRootSpanIdBase64, (string?)normalized["parent_span_id"]);
    }

    [Fact]
    public void RewritesIdsNestedInsideTheOtlpEnvelope()
    {
        var normalized = Normalize(
            $$"""
            {
                "resourceSpans": [{
                    "scopeSpans": [{
                        "spans": [
                            { "traceId": "{{Otlp.TRACE_ID}}", "spanId": "{{Otlp.ROOT_SPAN_ID}}" },
                            { "traceId": "{{Otlp.TRACE_ID}}", "spanId": "{{Otlp.CHILD_SPAN_ID}}" }
                        ]
                    }]
                }]
            }
            """
        );

        var spans = normalized["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]!.AsArray();

        Assert.Equal(sTraceIdBase64, (string?)spans[0]!["traceId"]);
        Assert.Equal(sRootSpanIdBase64, (string?)spans[0]!["spanId"]);
        Assert.Equal(sTraceIdBase64, (string?)spans[1]!["traceId"]);
        Assert.Equal(sChildSpanIdBase64, (string?)spans[1]!["spanId"]);
    }

    [Fact]
    public void LeavesCorrectlyEncodedBase64Alone()
    {
        // A sender emitting proto3 JSON to the letter must survive untouched.
        var body =
            $$"""
            {"traceId":"{{sTraceIdBase64}}","spanId":"{{sRootSpanIdBase64}}"}
            """;

        Assert.Equal(body, OtlpJsonIdNormalizer.NormalizeIds(body));
    }

    [Fact]
    public void LeavesBodiesWithoutIdsAlone()
    {
        var body = """{"resourceSpans":[{"scopeSpans":[{"spans":[]}]}]}""";

        Assert.Equal(body, OtlpJsonIdNormalizer.NormalizeIds(body));
    }

    [Fact]
    public void PassesThroughBodiesThatAreNotJson()
    {
        // The protobuf parser stays the one that decides what is valid, so nothing is swallowed here.
        const string BODY = "this is not json";

        Assert.Equal(BODY, OtlpJsonIdNormalizer.NormalizeIds(BODY));
    }

    [Theory]
    // An id field holding something other than a hex string of the right length is left for the parser.
    [InlineData("""{"traceId":12345}""")]
    [InlineData("""{"traceId":null}""")]
    [InlineData("""{"traceId":""}""")]
    [InlineData("""{"traceId":{"nested":"object"}}""")]
    [InlineData("""{"spanId":"zzzzzzzzzzzzzzzz"}""")]
    public void PassesThroughValuesThatAreNotHexIds(string body) =>
        Assert.Equal(body, OtlpJsonIdNormalizer.NormalizeIds(body));

    [Fact]
    public void RewritesOnlySpanIdWhenTraceIdIsAlreadyBase64()
    {
        // Mixed encodings shouldn't confuse it: each field is judged on its own length.
        var normalized = Normalize(
            $$"""
            {"traceId":"{{sTraceIdBase64}}","spanId":"{{Otlp.ROOT_SPAN_ID}}"}
            """
        );

        Assert.Equal(sTraceIdBase64, (string?)normalized["traceId"]);
        Assert.Equal(sRootSpanIdBase64, (string?)normalized["spanId"]);
    }

    private static JsonObject Normalize(string body)
    {
        var normalized = OtlpJsonIdNormalizer.NormalizeIds(body);

        return JsonNode.Parse(normalized) as JsonObject
            ?? throw new JsonException($"Expected a JSON object, got: {normalized}");
    }
}
