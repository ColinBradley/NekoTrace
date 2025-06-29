namespace InfoCat.Web.UI.Pages;

using InfoCat.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Immutable;

public partial class Home : IDisposable
{
    private ImmutableList<SpanData>? mClientSpans;
    private DotNetObjectReference<Home>? mSelfReference;

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required IJSRuntime JSRuntime { get; set; }

    [Parameter]
    public string? SelectedTraceId { get; set; }

    private ElementReference? TraceFlameCanvas { get; set; }

    private IJSObjectReference? TraceModule { get; set; }


    private Trace? SelectedTrace =>
        this.SelectedTraceId is null
            ? null
            : this.TracesRepo.TryGetTrace(this.SelectedTraceId);

    private SpanData? SelectedSpan { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        this.TracesRepo.TracesChanged += this.TracesRepo_TracesChanged;
    }

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

        var selectedTrace = this.SelectedTrace;
        if (selectedTrace is null || this.TraceFlameCanvas is null || object.ReferenceEquals(mClientSpans, selectedTrace.Spans))
        {
            return;
        }

        mClientSpans = selectedTrace.Spans;

        await this.TraceModule!.InvokeVoidAsync(
            "initialize",
            this.TraceFlameCanvas,
            mClientSpans ?? [],
            mSelfReference,
            nameof(SetSelectedSpanId)
        );
    }

    private void TracesRepo_TracesChanged(string traceId)
    {
        this.InvokeAsync(this.StateHasChanged);
    }

    [JSInvokable]
    public void SetSelectedSpanId(string? spanId)
    {
        if (string.IsNullOrEmpty(spanId))
        {
            this.SelectedSpan = null;
        }
        else
        {
            this.SelectedSpan = this.SelectedTrace?.Spans.FirstOrDefault(s => string.Equals(s.Id, spanId, StringComparison.Ordinal));
        }

        this.InvokeAsync(this.StateHasChanged);
    }

    public void Dispose()
    {
        this.TracesRepo.TracesChanged -= this.TracesRepo_TracesChanged;
    }
}
