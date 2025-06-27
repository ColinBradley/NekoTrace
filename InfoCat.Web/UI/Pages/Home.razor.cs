namespace InfoCat.Web.UI.Pages;

using InfoCat.Web.Repositories;
using Microsoft.AspNetCore.Components;

public partial class Home
{
    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Parameter]
    public string? SelectedTraceId { get; set; }

    private TraceData? SelectedTrace =>
        this.SelectedTraceId is null 
            ? null 
            : this.TracesRepo.TryGetTrace(this.SelectedTraceId);
}
