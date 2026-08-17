namespace NekoTrace.Tests.Analysis;

using NekoTrace.Tests.TestData;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Analysis.Formatting;
using NekoTrace.Web.Analysis.Queries;
using NekoTrace.Web.Analysis.Results;
using NekoTrace.Web.Repositories.Traces;
using System.Globalization;
using Xunit;
using static OpenTelemetry.Proto.Trace.V1.Status.Types;

/// <summary>
/// The flat format's whole value is that a shell can address it by column, so these are mostly about the
/// column contract holding when the data gets awkward rather than about the wording of any one line.
/// </summary>
public sealed class FlatFormatterTests
{
    private const int TREE_FIELDS = 10;
    private const int NAME_FIELD = 8;
    private const int ATTRIBUTES_FIELD = 9;

    [Fact]
    public void Tree_PrintsOneLinePerSpanAndNothingElse()
    {
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "child"),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "child", startMs: 20),
            Fake.Span(id: "d4", parentSpanId: "a1", name: "child", startMs: 40)
        );

        // Which is what makes `wc -l` on it mean something, and it holds at the default collapse threshold:
        // three siblings sharing a name would be one ×N line in the text tree.
        Assert.Equal(4, Spans(flat).Length);
    }

    [Fact]
    public void Tree_PutsEveryNoteBehindAHash()
    {
        var flat = Flat(Fake.Span(id: "a1", name: "root"));

        // `grep -v '^#'` has to leave exactly the data, or every count taken off this output is wrong. The
        // legend, the column names and the footer are all notes.
        Assert.All(Lines(flat), line => Assert.True(line.StartsWith('#') || line.Split('\t').Length is TREE_FIELDS));
    }

    [Fact]
    public void Tree_KeepsTheFieldCountWhenASpanHasNoAttributes()
    {
        var flat = Flat(Fake.Span(id: "a1", name: "root"));

        var fields = Assert.Single(Spans(flat)).Split('\t');

        Assert.Equal(TREE_FIELDS, fields.Length);

        // Rather than an empty one. A trailing empty field is invisible in the output and easy to lose in
        // whatever splits it, and every other absent value here is spelled the same way.
        Assert.Equal("-", fields[ATTRIBUTES_FIELD]);
    }

    [Fact]
    public void Tree_KeepsTheFieldCountWhenAValueHoldsSpaces()
    {
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(
                id: "b2",
                parentSpanId: "a1",
                name: "GET /things",
                attributes: new() { ["db.statement"] = "SELECT * FROM things WHERE id = 1" }
            )
        );

        var fields = Spans(flat)[1].Split('\t');

        Assert.Equal(TREE_FIELDS, fields.Length);
        Assert.Equal("GET /things", fields[NAME_FIELD]);
        Assert.Contains("SELECT * FROM things", fields[ATTRIBUTES_FIELD], StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_FoldsLineBreaksAndTabsOutOfValues()
    {
        // The one thing that would break the format outright: a newline in a stack trace splitting one span
        // across two lines, or a tab in a value shifting every field after it into the wrong column.
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(
                id: "b2",
                parentSpanId: "a1",
                name: "boom",
                attributes: new() { ["error.stack"] = "at One()\r\n\tat Two()\n\tat Three()" }
            )
        );

        var line = Spans(flat)[1];

        Assert.Equal(TREE_FIELDS, line.Split('\t').Length);
        Assert.Contains("at One()", line, StringComparison.Ordinal);
        Assert.Contains("at Three()", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_PrintsTheAttributesEverySpanSharesInTheHeader()
    {
        // Hoisted attributes are left off every line, and something grepping this has no summary to have
        // read first: a search for service.name=checkout would come back empty across a trace where every
        // span carries it. So the block is printed once, up front, in the same key=value shape.
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100, attributes: new() { ["service.name"] = "checkout" }),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "child", attributes: new() { ["service.name"] = "checkout" })
        );

        Assert.Contains("identical on all 2 spans", flat, StringComparison.Ordinal);
        Assert.Contains("service.name=checkout", flat, StringComparison.Ordinal);
        Assert.All(Spans(flat), line => Assert.Equal("-", line.Split('\t')[ATTRIBUTES_FIELD]));
    }

    [Fact]
    public void Tree_PrintsTimesAsBareMillisecondsThatSortNumerically()
    {
        // 340ms and 1.2s read better than 340 and 1200 and sort backwards, which is the trade this format
        // takes the other way: `sort -k2 -n` is most of the point of having it.
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 2000),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "quick", durationMs: 340),
            Fake.Span(id: "c3", parentSpanId: "a1", name: "slow", startMs: 400, durationMs: 1200)
        );

        var durations = Spans(flat)
            .Select(line => double.Parse(line.Split('\t')[1], CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal([2000d, 340d, 1200d], durations);
    }

    [Fact]
    public void Tree_CarriesTheDepthTheIndentationWouldHave()
    {
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "middle", durationMs: 50),
            Fake.Span(id: "c3", parentSpanId: "b2", name: "leaf")
        );

        Assert.Equal(["0", "1", "2"], Spans(flat).Select(line => line.Split('\t')[3]));
    }

    [Fact]
    public void Tree_NamesTheParentSoAFilteredLineStillKnowsWhereItSat()
    {
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "child")
        );

        var lines = Spans(flat);

        Assert.Equal("-", lines[0].Split('\t')[5]);
        Assert.Equal("a1", lines[1].Split('\t')[5]);
    }

    [Fact]
    public void Tree_MarksASpanWhoseParentNeverArrived()
    {
        var flat = Flat(
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "0123456789abcdef", name: "orphan", startMs: 10)
        );

        var orphan = Spans(flat).Single(line => line.Contains("orphan", StringComparison.Ordinal));

        // Whole, not shortened: shortening is only unique among the spans that are here, and this one names
        // a span that is not. An orphan renders at depth 0 like a real root, so without the marker a
        // partially collected trace reads as one that genuinely has that many tops.
        Assert.Equal("orphan:0123456789abcdef", orphan.Split('\t')[5]);
        Assert.Equal("0", orphan.Split('\t')[3]);
        Assert.Contains("name a parent that never arrived", flat, StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_PutsAFailedSpansMessageInTheAttributesAndSaysSo()
    {
        var failed = Fake.Span(id: "a1", name: "root", status: StatusCode.Error) with
        {
            StatusMessage = "the thing broke",
        };

        var flat = Flat(failed);

        var fields = Assert.Single(Spans(flat)).Split('\t');

        Assert.Equal("ERROR", fields[7]);
        Assert.Equal("status.message=the thing broke", fields[ATTRIBUTES_FIELD]);
        Assert.Contains("status.message=<message>", flat, StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_ReportsWhatWasHiddenRatherThanJustShrinking()
    {
        var flat = Flat(
            TreeViewOptions.Default with { HiddenSpanNames = ["noisy"] },
            Fake.Span(id: "a1", name: "root", durationMs: 100),
            Fake.Span(id: "b2", parentSpanId: "a1", name: "noisy", durationMs: 40),
            Fake.Span(id: "c3", parentSpanId: "b2", name: "under noisy")
        );

        Assert.Single(Spans(flat));
        Assert.Contains("# 2 span(s)", flat, StringComparison.Ordinal);
        Assert.Contains("hidden by HiddenSpanNames", flat, StringComparison.Ordinal);
    }

    private static string Flat(params SpanData[] spans) => Flat(TreeViewOptions.Default, spans);

    private static string Flat(TreeViewOptions options, params SpanData[] spans)
    {
        // Collapsing off, which is what TraceViews does for this format: a ×N group is a summary, and one
        // line per span is the promise the format is making.
        var result = TreeView.Build(SpanTree.Build(spans), options with { CollapseThreshold = 0 });

        return FlatFormatter.Tree(
            result,
            SpanIdShortener.For(spans.Select(span => span.Id)),
            AttributeSummary.Build(spans),
            AttributeSelector.Default,
            SpanRenderOptions.Default
        );
    }

    private static string[] Lines(string flat) =>
        flat.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static string[] Spans(string flat) =>
        [.. Lines(flat).Where(line => !line.StartsWith('#'))];
}
