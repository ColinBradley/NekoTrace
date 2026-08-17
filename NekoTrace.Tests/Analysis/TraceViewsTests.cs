namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Formatting;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

/// <summary>
/// Which views have a flat rendering, and what the tree's does with a request that would have collapsed.
/// </summary>
public sealed class TraceViewsTests : IDisposable
{
    private readonly TracesRepository mTraces = Fake.TracesRepository();

    [Fact]
    public void Tree_FlatPrintsEverySpanEvenWhenTheRequestWouldHaveCollapsed()
    {
        var views = this.Views(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "call"),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "call", startMs: 20),
            Fake.Span(id: "d4", parentSpanId: "a1", name: "call", startMs: 40)
        );

        var view = Tree(views, TreeViewOptions.Default);

        // The same request, rendered both ways: the text tree merges the three into a ×N line at the default
        // threshold, and the flat one does not, because one line per span is what it is for.
        Assert.Contains("×3 call", view.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("×3", view.Flat ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(
            4,
            (view.Flat ?? string.Empty)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => !line.StartsWith('#'))
        );
    }

    [Fact]
    public void Tree_FlatStillHonoursEverythingElseTheRequestAskedFor()
    {
        var views = this.Views(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "middle", durationMs: 50),
            Fake.Span(id: "c3", parentSpanId: "b2", name: "leaf")
        );

        var view = Tree(views, TreeViewOptions.Default with { MaxDepth = 1 });

        Assert.DoesNotContain("leaf", view.Flat ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("past the depth limit", view.Flat ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ListTraces_AndSearchSpans_HaveAFlatRendering()
    {
        var views = this.Views(Fake.Span(id: "a1", name: "root"));

        Assert.NotNull(views.ListTraces(TraceFilter.Empty, 50).Flat);
        Assert.NotNull(
            views.SearchSpans(
                SpanQuery.Empty,
                traceId: null,
                50,
                AttributeSelector.Default,
                SpanRenderOptions.Default
            ).Flat
        );
    }

    [Fact]
    public void SearchSpans_PrintsWhatTheMatchesShareAndWhatTheyDoNot()
    {
        // The gap this closed: a search answering with ids alone made "all 1,975 of these hit one table" a
        // claim you could only reach by opening a handful with get_span and generalising from the sample.
        var views = this.Views(
            Fake.Span(id: "a1", name: "GET", durationMs: 100, attributes: new()
            {
                ["service.name"] = "checkout",
                ["url.full"] = "http://host/tables/abc/data",
            }),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "GET", attributes: new()
            {
                ["service.name"] = "checkout",
                ["url.full"] = "http://host/tables/def/data",
            })
        );

        var view = views.SearchSpans(
            new SpanQuery() { Name = "GET" },
            Otlp.TRACE_ID,
            50,
            AttributeSelector.Default,
            SpanRenderOptions.Default
        );

        // service.name is on both matches with the same value, so it hoists out and is stated once.
        // url.full differs, so it stays on the lines — which is what makes the difference greppable.
        Assert.Contains("attributes identical on all 2 matches", view.Text, StringComparison.Ordinal);
        Assert.Contains("service.name=checkout", view.Text, StringComparison.Ordinal);

        // Before the matches, not after them: on a 1,975 match search this block is the answer, and a reader
        // who has to get past every row to reach it has been made to earn it twice.
        Assert.True(
            view.Text.IndexOf("service.name=checkout", StringComparison.Ordinal)
                < view.Text.IndexOf(Otlp.TRACE_ID, StringComparison.Ordinal),
            "the common attribute block should precede the first match line"
        );

        var lines = (view.Flat ?? string.Empty)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .ToArray();

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(8, line.Split('\t').Length));
        Assert.EndsWith("url.full=http://host/tables/abc/data", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("url.full=http://host/tables/def/data", lines[1], StringComparison.Ordinal);

        // And the hoisted one is not repeated on them.
        Assert.All(lines, line => Assert.DoesNotContain("service.name", line, StringComparison.Ordinal));
    }

    [Fact]
    public void SearchSpans_CountsAndHoistsOverEveryMatchRatherThanThePrintedPage()
    {
        // Four matches sharing service.name. Ask for one of them.
        var views = this.Views(
            Fake.Span(id: "a1", name: "GET", durationMs: 100, attributes: new() { ["service.name"] = "checkout" }),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "GET", attributes: new() { ["service.name"] = "checkout" }),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "GET", startMs: 20, attributes: new() { ["service.name"] = "checkout" }),
            Fake.Span(id: "d4", parentSpanId: "a1", name: "GET", startMs: 40, attributes: new() { ["service.name"] = "checkout" })
        );

        var view = views.SearchSpans(
            new SpanQuery() { Name = "GET" },
            Otlp.TRACE_ID,
            limit: 1,
            AttributeSelector.Default,
            SpanRenderOptions.Default
        );

        // The regression: both of these used to describe the one row that got printed, which made limit — a
        // knob about how much to render — silently change what the answer said about the query. A caller
        // asking whether 1,975 spans all hit one URL could then only learn it about the ones they paid for.
        Assert.Contains("4 matches, showing 1", view.Text, StringComparison.Ordinal);
        Assert.Contains("identical on all 4 matches", view.Text, StringComparison.Ordinal);
        Assert.Contains("identical on all 4 matches", view.Flat ?? string.Empty, StringComparison.Ordinal);

        Assert.Single(
            (view.Flat ?? string.Empty)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith('#'))
        );
    }

    [Fact]
    public void SearchSpans_StopsHoistingWhenAMatchOutsideThePageDisagrees()
    {
        // The other half of it. The printed page agrees on url.full; a match beyond the limit does not, so
        // the key must stay on the lines — reporting it as common would assert it of a span that refutes it.
        var views = this.Views(
            Fake.Span(id: "a1", name: "GET", durationMs: 100, attributes: new() { ["url.full"] = "http://host/a" }),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "GET", attributes: new() { ["url.full"] = "http://host/a" }),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "GET", startMs: 20, attributes: new() { ["url.full"] = "http://host/DIFFERENT" })
        );

        var view = views.SearchSpans(
            new SpanQuery() { Name = "GET" },
            Otlp.TRACE_ID,
            limit: 2,
            AttributeSelector.Default,
            SpanRenderOptions.Default
        );

        Assert.Contains("3 matches, showing 2", view.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("identical on all", view.Text, StringComparison.Ordinal);
        Assert.Contains("url.full=http://host/a", view.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchSpans_JsonModelCarriesWhatTheRenderingsCarry()
    {
        // The gap: format=json served the bare match array, so the format meant for a caller that intends to
        // process the result gave it less than the one meant for a caller reading prose — no count, and no
        // block of what the matches agreed on, which are the two things that make the answer cover the whole
        // set rather than the page.
        var views = this.Views(
            Fake.Span(id: "a1", name: "GET", durationMs: 100, attributes: new() { ["url.path"] = "/data" }),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "GET", attributes: new() { ["url.path"] = "/data" }),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "GET", startMs: 20, attributes: new() { ["url.path"] = "/data" })
        );

        var model = Assert.IsType<SpanSearchResults>(
            views.SearchSpans(
                new SpanQuery() { Name = "GET" },
                Otlp.TRACE_ID,
                limit: 1,
                AttributeSelector.Default,
                SpanRenderOptions.Default
            ).Model
        );

        Assert.Equal(3, model.Total);
        Assert.Single(model.Matches);
        Assert.Equal("/data", model.Common["url.path"]);

        // And the match keeps its whole attribute map rather than having the common keys stripped, so
        // .common answers the question the search asked and .matches stays a complete span for anything else.
        Assert.Equal("/data", Assert.Single(model.Matches).Span.Attributes["url.path"]);
    }

    [Fact]
    public void SearchSpans_JsonModelLeavesCommonEmptyWhenTheMatchesDisagree()
    {
        var views = this.Views(
            Fake.Span(id: "a1", name: "GET", durationMs: 100, attributes: new() { ["url.path"] = "/one" }),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "GET", attributes: new() { ["url.path"] = "/two" })
        );

        var model = Assert.IsType<SpanSearchResults>(
            views.SearchSpans(
                new SpanQuery() { Name = "GET" },
                Otlp.TRACE_ID,
                limit: 50,
                AttributeSelector.Default,
                SpanRenderOptions.Default
            ).Model
        );

        Assert.Equal(2, model.Total);
        Assert.False(model.Common.ContainsKey("url.path"));
    }

    [Fact]
    public void SearchSpans_DropsTheAttributesWhenAskedTo()
    {
        var views = this.Views(
            Fake.Span(id: "a1", name: "GET", attributes: new() { ["url.full"] = "http://host/a" })
        );

        var view = views.SearchSpans(
            new SpanQuery() { Name = "GET" },
            Otlp.TRACE_ID,
            50,
            AttributeSelector.Default,
            new SpanRenderOptions() { IncludeAttributes = false }
        );

        Assert.DoesNotContain("url.full", view.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("url.full", view.Flat ?? string.Empty, StringComparison.Ordinal);

        // Still eight fields, with the empty one marked, or the columns stop lining up between two runs that
        // differ only in a rendering switch.
        var line = Assert.Single(
            (view.Flat ?? string.Empty)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(candidate => !candidate.StartsWith('#'))
        );

        Assert.Equal(8, line.Split('\t').Length);
        Assert.EndsWith("\t-", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_AndSpan_HaveNoFlatRendering()
    {
        var views = this.Views(Fake.Span(id: "a1", name: "root"));

        // Not an oversight, and the API turns it into a 400 naming the formats they do have. The summary is
        // a fixed size report with no single row type, and a single span is the whole of one span rather
        // than a list — flat would truncate the attributes and events it exists to show. Answering in some
        // near-enough shape would leave a caller piping something that only looks like what it asked for.
        Assert.Null(views.Summary(Otlp.TRACE_ID, TraceSummaryOptions.Default)?.Flat);
        Assert.Null(views.Span(Otlp.TRACE_ID, "a1")?.Flat);
    }

    [Fact]
    public void Profile_FlatPutsTheCallPathInAColumnInsteadOfIndentation()
    {
        var views = this.Views(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "middle", durationMs: 50),
            Fake.Span(id: "c3", parentSpanId: "b2", name: "leaf", durationMs: 10)
        );

        var flat = views.Profile(Otlp.TRACE_ID, 5)?.Flat ?? string.Empty;

        var rows = flat
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .ToArray();

        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Equal(10, row.Split('\t').Length));

        // The path column is what makes a filtered line still mean something — the tree's indentation says
        // the same thing and does not survive a grep.
        Assert.Equal(["root", "root;middle", "root;middle;leaf"], rows.Select(row => row.Split('\t')[9]));
        Assert.Equal(["0", "1", "2"], rows.Select(row => row.Split('\t')[8]));
    }


    [Fact]
    public void Profile_JsonModelIsFlatSoDepthCannotBreakSerialisation()
    {
        // The regression, found by pinning an arm to --format json in run 4: the model was the nested
        // ProfileNode tree, and the 230,313 span trace recurses 25 levels — two JSON levels each, against
        // System.Text.Json's limit of 32. The endpoint answered 500 with half a document already written.
        // Raising the limit only moves the cliff, and moves it towards a stack overflow rather than an error.
        var spans = new List<SpanData>() { Fake.Span(id: "s0", name: "n0", durationMs: 400) };

        for (var depth = 1; depth < 60; depth++)
        {
            spans.Add(
                Fake.Span(
                    id: "s" + depth,
                    parentSpanId: "s" + (depth - 1),
                    name: "n" + depth,
                    startMs: depth,
                    durationMs: 300
                )
            );
        }

        var views = this.Views([.. spans]);
        var model = Assert.IsType<ImmutableArray<ProfileRow>>(views.Profile(Otlp.TRACE_ID, 5)?.Model);

        Assert.Equal(60, model.Length);
        Assert.Equal(59, model[^1].Depth);

        // Serialises at any depth, because there is none.
        var json = JsonSerializer.Serialize(model);

        Assert.Contains("n59", json, StringComparison.Ordinal);

        // Rows arrive depth first, so a parent always precedes its children and the tree is rebuildable.
        Assert.Equal("n0;n1;n2", model[3 - 1].Path);
    }

    public void Dispose()
    {
        mTraces.Dispose();
    }

    private TraceViews Views(params SpanData[] spans)
    {
        mTraces.GetOrAddTrace(Otlp.TRACE_ID).AddSpans(spans);

        return new TraceViews(mTraces);
    }

    private static NekoTrace.Web.Analysis.Results.TraceView Tree(TraceViews views, TreeViewOptions options) =>
        views.Tree(Otlp.TRACE_ID, options, AttributeSelector.Default, SpanRenderOptions.Default, 5)
            ?? throw new InvalidOperationException("The trace was not found.");
}
