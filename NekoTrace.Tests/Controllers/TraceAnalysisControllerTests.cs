namespace NekoTrace.Tests.Controllers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Controllers;
using NekoTrace.Web.Repositories.Traces;
using Xunit;

/// <summary>
/// Which rendering <c>format</c> selects, and what happens when it names one that is not on offer.
/// </summary>
public sealed class TraceAnalysisControllerTests : IDisposable
{
    private readonly TracesRepository mTraces = Fake.TracesRepository();

    public TraceAnalysisControllerTests()
    {
        mTraces.GetOrAddTrace(Otlp.TRACE_ID).AddSpans(
            [
                Fake.Span(id: "a1", name: "root", durationMs: 100),
                Fake.Span(id: "b2", parentSpanId: "a1", name: "child"),
            ]
        );
    }

    [Fact]
    public void Tree_ServesTheFlatRenderingWhenAskedForIt()
    {
        var content = Assert.IsType<ContentResult>(Controller("?format=flat").Tree(Otlp.TRACE_ID));

        Assert.Equal("text/plain; charset=utf-8", content.ContentType);
        Assert.Contains("one line per span", content.Content ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_ServesTextWhenNoFormatIsNamed()
    {
        var content = Assert.IsType<ContentResult>(Controller("").Tree(Otlp.TRACE_ID));

        Assert.Contains("a ×N line is many sibling spans merged", content.Content ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_RefusesFlatAndNamesTheFormatsItHas()
    {
        // Rather than quietly serving text. The point of asking for flat is that the output is going to be
        // piped somewhere, and a caller who got the report instead would find out from whatever consumed it.
        var refusal = Assert.IsType<BadRequestObjectResult>(Controller("?format=flat").Summary(Otlp.TRACE_ID));

        Assert.Contains("format=text and format=json", refusal.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RefusesAFormatItDoesNotKnow()
    {
        var refusal = Assert.IsType<BadRequestObjectResult>(Controller("?format=flatt").ListTraces());

        Assert.Contains("Unknown format 'flatt'", refusal.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        mTraces.Dispose();
    }

    private TraceAnalysisController Controller(string queryString)
    {
        var context = new DefaultHttpContext();

        context.Request.QueryString = new QueryString(queryString);

        return new TraceAnalysisController(new TraceViews(mTraces))
        {
            ControllerContext = new ControllerContext() { HttpContext = context },
        };
    }
}
