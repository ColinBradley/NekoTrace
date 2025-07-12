namespace NekoTrace.Web.UI.Components;

using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NekoTrace.Web.Repositories;

public partial class TraceViewComponent
{
    public const string DEFAULT_SPAN_COLOR_SELECTOR = "otel.library.name";

    private ImmutableList<SpanData>? mClientSpans;
    private DotNetObjectReference<TraceViewComponent>? mSelfReference;

    [Parameter, EditorRequired]
    public required string TraceId { get; set; }

    [Parameter]
    public string? SpanColorSelector { get; set; }

    [Parameter]
    public bool IsSmallMode { get; set; }

    [SupplyParameterFromQuery]
    public bool? GroupSpans { get; set; }

    [SupplyParameterFromQuery]
    public string? SelectedSpanId { get; set; }

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required IJSRuntime JSRuntime { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    private ElementReference? TraceFlameCanvas { get; set; }

    private IJSObjectReference? TraceModule { get; set; }

    private Trace? Trace => this.TraceId is null ? null : this.TracesRepo.TryGetTrace(this.TraceId);

    private SpanData? SelectedSpan =>
        this.Trace?.Spans.FirstOrDefault(s =>
            string.Equals(s.Id, this.SelectedSpanId, StringComparison.Ordinal)
        );

    private string EffectiveSpanColorSelector => this.SpanColorSelector ?? DEFAULT_SPAN_COLOR_SELECTOR;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            this.TraceModule = await this.JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "/js/traceView.js"
            );

            mSelfReference = DotNetObjectReference.Create(this);
        }

        var trace = this.Trace;
        if (
            this.TraceModule is null
            || trace is null
            || this.TraceFlameCanvas is null
            || object.ReferenceEquals(mClientSpans, trace.Spans)
        )
        {
            return;
        }

        mClientSpans = trace.Spans;

        await this.TraceModule.InvokeVoidAsync(
            "initialize",
            this.TraceFlameCanvas,
            mClientSpans ?? [],
            mSelfReference,
            nameof(SetSelectedSpanId)
        );
    }

    [JSInvokable]
    public void SetSelectedSpanId(string? spanId)
    {
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.SelectedSpanId), spanId),
            replace: true
        );
    }

    private void GroupSpans_Change(ChangeEventArgs e)
    {
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.GroupSpans), (e.Value as bool? ?? false) ? null : false),
            replace: true
        );
    }
}
