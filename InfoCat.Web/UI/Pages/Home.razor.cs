namespace InfoCat.Web.UI.Pages;

using InfoCat.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public partial class Home
{
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // This is silly and needs to be done as properties change too but eehh
        if (!firstRender || this.TraceFlameCanvas is null)
        {
            return;
        }

        var selectedTrace = this.SelectedTrace;
        if (selectedTrace is null)
        {
            return;
        }

        this.TraceModule = await this.JSRuntime.InvokeAsync<IJSObjectReference>(
            "import", 
            "/js/traceView.js"
        );

        await this.TraceModule.InvokeVoidAsync(
            "initialize", 
            this.TraceFlameCanvas,
            selectedTrace.Spans
        );
    }
}
