namespace NekoTrace.Web.UI.Pages.Traces;

using Microsoft.AspNetCore.Components;

public partial class TracePage
{
    [Parameter]
    public required string TraceId { get; set; }
}