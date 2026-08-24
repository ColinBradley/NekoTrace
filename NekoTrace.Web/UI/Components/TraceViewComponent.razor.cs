namespace NekoTrace.Web.UI.Components;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;
using NekoTrace.Web.Repositories.Traces;
using System.Collections.Immutable;

public sealed partial class TraceViewComponent : IDisposable
{
    public const string DEFAULT_SPAN_COLOR_SELECTOR = "otel.library.name";
    public const string SELECTED_SPAN_ID_PARAMETER = "selectedSpanId";

    private static readonly ImmutableArray<string> sTraceViewOptionParameters =
    [
        "groupSpans",
        "adjustClockSkew",
        SELECTED_SPAN_ID_PARAMETER,
        "hiddenSpanNames",
        "hiddenSpanIds",
        "hiddenAttributeNames",
    ];

    private ImmutableArray<SpanData> mClientSpans;
    private DotNetObjectReference<TraceViewComponent>? mSelfReference;

    [Parameter, EditorRequired]
    public required string TraceId { get; set; }

    [Parameter]
    public string? SpanColorSelector { get; set; }

    [Parameter]
    public bool IsSmallMode { get; set; }

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required IJSRuntime JSRuntime { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    private ElementReference? TraceViewElement { get; set; }

    private IJSObjectReference? TraceModule { get; set; }

    private TraceItem? Trace =>
        this.TraceId is null
            ? null
            : this.TracesRepo.TryGetTrace(this.TraceId);

    private string EffectiveSpanColorSelector =>
        this.SpanColorSelector ?? DEFAULT_SPAN_COLOR_SELECTOR;

    private string FullViewUri
    {
        get
        {
            var currentOptions = QueryHelpers.ParseQuery(new Uri(this.Navigation.Uri).Query);

            return QueryHelpers.AddQueryString(
                $"traces/{Uri.EscapeDataString(this.TraceId)}",
                sTraceViewOptionParameters
                    .Where(currentOptions.ContainsKey)
                    .Select(name => new KeyValuePair<string, StringValues>(name, currentOptions[name]))
            );
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        this.Navigation.LocationChanged += this.Navigation_LocationChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            this.TraceModule = await this.JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "/js/traceViewInterop.js"
            );

            mSelfReference = DotNetObjectReference.Create(this);
        }

        var trace = this.Trace;
        if (
            this.TraceModule is null
            || trace is null
            || this.TraceViewElement is null
            || mClientSpans.Equals(trace.Spans)
        )
        {
            if (trace is null || this.TraceViewElement is null)
            {
                mClientSpans = [];
            }

            return;
        }

        mClientSpans = trace.Spans;

        await this.TraceModule.InvokeVoidAsync(
            "initialize",
            this.TraceViewElement,
            new TraceViewData()
            {
                // Use a slim version of spans to reduce the amount of data sent to the browser.
                // But also because parsing JSON is "slow".
                Spans = mClientSpans.Select(
                    s =>
                    new SpanDataSlim()
                    {
                        Id = s.Id,
                        ParentSpanId = s.ParentSpanId,
                        Name = s.Name,
                        Kind = s.Kind,
                        Attributes = s.Attributes,
                        StartTimeMs = s.StartTimeMs,
                        EndTimeMs = s.EndTimeMs,
                        StatusCode = s.StatusCode,
                        StatusMessage = s.StatusMessage,
                        Events = s.Events,
                    }
                ),
                MaxSpanDurationMsByName = this.GetSpanNameMaxDurations(trace),
            },
            mSelfReference,
            nameof(this.Navigate)
        );
    }

    /// <summary>
    /// Called after the view has changed the URL, so <see cref="NavigationManager"/> does not go stale.
    /// </summary>
    [JSInvokable]
    // JS interop hands over a string and NavigateTo takes one, so a Uri would only be a parse and back.
#pragma warning disable CA1054 // URI-like parameters should not be strings
    public void Navigate(string url)
#pragma warning restore CA1054
    {
        this.Navigation.NavigateTo(url, replace: true);
    }

    private void Navigation_LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Ensure FullViewUri is fresh
        _ = this.InvokeAsync(this.StateHasChanged);
    }

    private Dictionary<string, double> GetSpanNameMaxDurations(TraceItem trace)
    {
        var maxDurations = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var name in trace.Spans.Select(s => s.Name).Distinct(StringComparer.Ordinal))
        {
            if (this.TracesRepo.SpanRepositoriesByName.TryGetValue(name, out var spanRepository))
            {
                maxDurations[name] = spanRepository.MaxDuration.TotalMilliseconds;
            }
        }

        return maxDurations;
    }

    private void RemoveButton_Click()
    {
        if (this.Trace is null)
        {
            return;
        }

        this.TracesRepo.RemoveTrace(this.Trace);
    }

    public void Dispose()
    {
        this.Navigation.LocationChanged -= this.Navigation_LocationChanged;

        mSelfReference?.Dispose();
    }
}
