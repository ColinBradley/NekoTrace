namespace NekoTrace.Web.Analysis.Results;

/// <summary>One assembled view, available as the analysis result itself or as one of its renderings.</summary>
/// <remarks>
/// <para>
/// These are not parallel representations. <see cref="Model"/> is the result; <see cref="Text"/> and
/// <see cref="Flat"/> are functions of it, which is why each rendering is deferred rather than done up front —
/// the HTTP API serves <c>format=json</c> straight from the model, and formatting a 230,000 span trace only to
/// discard the string is real work wasted. A request asks for exactly one of the three.
/// </para>
/// <para>
/// Both come out of one place so that the HTTP API and the MCP server cannot drift into answering the same
/// question differently, which is the whole reason <see cref="TraceViews"/> exists.
/// </para>
/// </remarks>
internal sealed class TraceView
{
    private readonly Lazy<string> mText;
    private readonly Lazy<string>? mFlat;

    /// <param name="renderFlat">
    /// Null for a view that is not a list of spans. The summary, the profile and a single span have no one
    /// line per span form, and answering them in some near-enough shape rather than saying so would leave a
    /// caller piping something that only looks like the format they asked for.
    /// </param>
    public TraceView(object model, Func<string> render, Func<string>? renderFlat = null)
    {
        this.Model = model;

        // Not thread safe by choice: a view is built and read on the one request thread that asked for it.
        mText = new Lazy<string>(render, LazyThreadSafetyMode.None);
        mFlat = renderFlat is null ? null : new Lazy<string>(renderFlat, LazyThreadSafetyMode.None);
    }

    public object Model { get; }

    public string Text => mText.Value;

    /// <summary>The one line per span rendering, or null where the view has no such form.</summary>
    public string? Flat => mFlat?.Value;

    /// <summary>Set when the request named a span prefix that matched no span, or more than one.</summary>
    public bool IsAmbiguous { get; init; }
}
