namespace NekoTrace.Tests.Utilities;

using Google.Protobuf;
using NekoTrace.Tests.TestData;
using NekoTrace.Web.Utilities;
using Xunit;

public sealed class TraceIdsTests
{
    [Fact]
    public void ToHex_Lowercases()
    {
        var bytes = ByteString.CopyFrom(Convert.FromHexString("4BF92F3577B34DA6A3CE929D0E0E4736"));

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", TraceIds.ToHex(bytes));
    }

    [Theory]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736", TraceIds.TRACE_ID_BYTE_LENGTH)]
    [InlineData("00f067aa0ba902b7", TraceIds.SPAN_ID_BYTE_LENGTH)]
    public void IsHexId_AcceptsCorrectLength(string value, int byteLength) =>
        Assert.True(TraceIds.IsHexId(value, byteLength));

    [Theory]
    // Right characters, wrong length — a span id measured against the trace id length.
    [InlineData("00f067aa0ba902b7", TraceIds.TRACE_ID_BYTE_LENGTH)]
    // Right length, but 'g' is not a hex digit.
    [InlineData("gbf92f3577b34da6a3ce929d0e0e4736", TraceIds.TRACE_ID_BYTE_LENGTH)]
    // The base64 form of a trace id: 24 characters, so length alone rules it out.
    [InlineData("S/kvNXezTaajzpKdDg5HNg==", TraceIds.TRACE_ID_BYTE_LENGTH)]
    [InlineData("", TraceIds.TRACE_ID_BYTE_LENGTH)]
    [InlineData(null, TraceIds.TRACE_ID_BYTE_LENGTH)]
    public void IsHexId_RejectsAnythingElse(string? value, int byteLength) =>
        Assert.False(TraceIds.IsHexId(value, byteLength));

    [Fact]
    public void NormalizeToHex_LowercasesHexThatIsAlreadyHex()
    {
        // OTLP allows either case, so an uppercase id is valid input that must still key consistently.
        Assert.Equal(
            "4bf92f3577b34da6a3ce929d0e0e4736",
            TraceIds.NormalizeToHex("4BF92F3577B34DA6A3CE929D0E0E4736", TraceIds.TRACE_ID_BYTE_LENGTH)
        );
    }

    [Fact]
    public void NormalizeToHex_ConvertsLegacyBase64TraceId()
    {
        var base64 = Fake.ToBase64(Otlp.TRACE_ID);

        Assert.Equal(24, base64.Length);
        Assert.Equal(Otlp.TRACE_ID, TraceIds.NormalizeToHex(base64, TraceIds.TRACE_ID_BYTE_LENGTH));
    }

    [Fact]
    public void NormalizeToHex_ConvertsLegacyBase64SpanId()
    {
        var base64 = Fake.ToBase64(Otlp.ROOT_SPAN_ID);

        Assert.Equal(12, base64.Length);
        Assert.Equal(Otlp.ROOT_SPAN_ID, TraceIds.NormalizeToHex(base64, TraceIds.SPAN_ID_BYTE_LENGTH));
    }

    [Fact]
    public void NormalizeToHex_IsIdempotent()
    {
        var once = TraceIds.NormalizeToHex(Fake.ToBase64(Otlp.TRACE_ID), TraceIds.TRACE_ID_BYTE_LENGTH);

        Assert.Equal(once, TraceIds.NormalizeToHex(once, TraceIds.TRACE_ID_BYTE_LENGTH));
    }

    [Theory]
    // Neither encoding at the expected length: passed through so the file still loads as an opaque key.
    [InlineData("not-an-id", TraceIds.TRACE_ID_BYTE_LENGTH)]
    [InlineData("", TraceIds.TRACE_ID_BYTE_LENGTH)]
    // A base64 *trace* id measured as a span id decodes 16 bytes, not 8, so it is left alone rather than
    // being truncated into a plausible-looking but wrong span id.
    [InlineData("S/kvNXezTaajzpKdDg5HNg==", TraceIds.SPAN_ID_BYTE_LENGTH)]
    public void NormalizeToHex_PassesThroughUnrecognisedValues(string value, int byteLength) =>
        Assert.Equal(value, TraceIds.NormalizeToHex(value, byteLength));
}
