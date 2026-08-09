namespace NekoTrace.Web.Analysis.Results;

/// <summary>One assembled view, available as the analysis result itself or as its text rendering.</summary>
/// <remarks>
/// <para>
/// The two are not parallel representations. <see cref="Model"/> is the result; <see cref="Text"/> is a
/// function of it, which is why the rendering is deferred rather than done up front — the HTTP API serves
/// <c>format=json</c> straight from the model, and formatting a 230,000 span trace only to discard the string
/// is real work wasted.
/// </para>
/// <para>
/// Both come out of one place so that the HTTP API and the MCP server cannot drift into answering the same
/// question differently, which is the whole reason <see cref="TraceViews"/> exists.
/// </para>
/// </remarks>
internal sealed class TraceView
{
    private readonly Lazy<string> mText;

    public TraceView(object model, Func<string> render)
    {
        this.Model = model;

        // Not thread safe by choice: a view is built and read on the one request thread that asked for it.
        mText = new Lazy<string>(render, LazyThreadSafetyMode.None);
    }

    public object Model { get; }

    public string Text => mText.Value;

    /// <summary>Set when the request named a span prefix that matched no span, or more than one.</summary>
    public bool IsAmbiguous { get; init; }
}
