namespace NekoTrace.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Formatting;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Utilities;
using System.Collections.Immutable;

/// <summary>
/// The read side an agent or the CLI talks to. See <c>docs/ai-access.md</c>.
/// </summary>
/// <remarks>
/// Public only because ASP.NET Core does not discover internal controllers; everything it hands around is
/// internal to the assembly.
/// </remarks>
[Route("api")]
[ApiController]
public sealed class TraceAnalysisController : ControllerBase
{
    private const int DEFAULT_LIMIT = 50;
    private const int MINIMUM_SAMPLES_FOR_SPREAD = 5;

    private readonly TraceViews mViews;

    public TraceAnalysisController(TraceViews views)
    {
        mViews = views;
    }

    /// <summary>
    /// The trace list. Filtered by <c>TraceFilter</c> read straight off this request's own query string, so a
    /// URL copied out of the UI's address bar means here exactly what it means there — see
    /// <c>docs/filtering.md</c>.
    /// </summary>
    [HttpGet("traces")]
    public IActionResult ListTraces() =>
        this.Render(
            mViews.ListTraces(
                TraceFilter.Parse(this.Request.QueryString.Value),
                this.ReadInt("limit", DEFAULT_LIMIT)
            )
        );

    [HttpGet("traces/{traceId}/summary")]
    public IActionResult Summary(string traceId) =>
        this.Render(
            mViews.Summary(
                traceId,
                new TraceSummaryOptions()
                {
                    ErrorLimit = this.ReadInt("errorLimit", TraceSummaryOptions.Default.ErrorLimit),
                    TopCount = this.ReadInt("top", TraceSummaryOptions.Default.TopCount),
                    ErrorExclusions = AttributeMatcher.Parse(this.Read("errorAttributeFilter")),
                }
            ),
            traceId
        );

    [HttpGet("traces/{traceId}/profile")]
    public IActionResult Profile(string traceId) =>
        this.Render(mViews.Profile(traceId, MINIMUM_SAMPLES_FOR_SPREAD), traceId);

    [HttpGet("traces/{traceId}/tree")]
    public IActionResult Tree(string traceId) =>
        this.Render(
            mViews.Tree(
                traceId,
                new TreeViewOptions()
                {
                    CollapseThreshold = this.ReadInt(
                        "collapseThreshold",
                        TreeViewOptions.Default.CollapseThreshold
                    ),
                    HiddenSpanNames = this.ReadPipeSeparated("HiddenSpanNames"),
                    HiddenSpanIds = this.ReadPipeSeparated("HiddenSpanIds"),
                    ExpandedNames = this.ReadPipeSeparated("expandNames"),
                    MaxDepth = this.ReadOptionalInt("maxSpanDepth"),
                    RootSpanId = this.Read("startAtSpanId"),
                },
                AttributeSelector.Parse(this.Read("attributeFilter")),
                new SpanRenderOptions()
                {
                    IncludeAttributes = this.ReadBool("includeAttributes", true),
                    IncludeEvents = this.ReadBool("includeEvents", false),
                    ShortenSpanIds = this.ReadBool("shortenSpanIds", true),
                },
                MINIMUM_SAMPLES_FOR_SPREAD
            ),
            traceId
        );

    [HttpGet("traces/{traceId}/spans")]
    public IActionResult Spans(string traceId) =>
        this.Render(
            mViews.SearchSpans(
                SpanQuery.Parse(this.Request.Query),
                traceId,
                this.ReadInt("limit", DEFAULT_LIMIT)
            )
        );

    [HttpGet("traces/{traceId}/spans/{spanId}")]
    public IActionResult Span(string traceId, string spanId)
    {
        var view = mViews.Span(traceId, spanId);

        if (view is null)
        {
            return this.NotFound("No trace with id '" + traceId + "'.");
        }

        // A prefix that named no span, or several, is the caller's mistake rather than a missing trace, and
        // the body lists the candidates — so it is a 400 carrying help, not a bare 404.
        return view.IsAmbiguous ? this.BadRequest(view.Text) : this.Render(view);
    }

    /// <summary>Across every trace held, unless <c>traceId</c> narrows it to one.</summary>
    [HttpGet("spans")]
    public IActionResult SearchSpans() =>
        this.Render(
            mViews.SearchSpans(
                SpanQuery.Parse(this.Request.Query),
                this.Read("traceId"),
                this.ReadInt("limit", DEFAULT_LIMIT)
            )
        );

    private IActionResult Render(TraceView? view, string? traceId = null) =>
        view is null
            ? this.NotFound("No trace with id '" + traceId + "'.")
            : string.Equals(this.Read("format"), "json", StringComparison.OrdinalIgnoreCase)
                ? this.Ok(view.Model)
                : this.Content(view.Text, "text/plain; charset=utf-8");

    private string? Read(string key) =>
        this.Request.Query.TryGetValue(key, out var values) ? values.FirstOrDefault() : null;

    private int ReadInt(string key, int fallback) => this.ReadOptionalInt(key) ?? fallback;

    // Through InputValues rather than a bare TryParse: UseRequestLocalization sits in front of this app, so
    // CurrentCulture is the caller's, and a query string is invariant however the caller's locale reads.
    // See docs/localization.md.
    private int? ReadOptionalInt(string key) => InputValues.TryParseInt32(this.Read(key));

    private ImmutableHashSet<string> ReadPipeSeparated(string key) =>
        this.Read(key)?.Split('|', StringSplitOptions.RemoveEmptyEntries).ToImmutableHashSet(StringComparer.Ordinal)
            ?? ImmutableHashSet<string>.Empty;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(this.Read(key), out var value) ? value : fallback;
}
