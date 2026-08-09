namespace NekoTrace.Web.UI.Layout;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NekoTrace.Web.Services;

public sealed partial class MainLayout
{
    [Inject]
    public required BrowserTimeZone BrowserTimeZone { get; set; }

    [Inject]
    public required IJSRuntime JSRuntime { get; set; }

    /// <summary>
    /// Carries the zone the prerender read from the cookie into the circuit, which has no
    /// <see cref="HttpContext"/> of its own to read it from. Without this, timestamps come back correct in the
    /// prerendered HTML and then flip to UTC the moment the circuit takes over.
    /// </summary>
    [PersistentState]
    public string? TimeZoneId { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (this.TimeZoneId is null)
        {
            // Prerendering: publish whatever the cookie gave us so the circuit gets it back below.
            this.TimeZoneId = this.BrowserTimeZone.Id;
        }
        else
        {
            this.BrowserTimeZone.TrySet(this.TimeZoneId);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (!firstRender)
        {
            return;
        }

        // Importing the module also writes the cookie the prerender above reads, so this only has anything to
        // correct on a browser's first ever visit, or after its zone changes.
        var timeZoneModule = await this.JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "/js/timeZone.js"
        );

        var timeZoneId = await timeZoneModule.InvokeAsync<string?>("getTimeZone");

        if (this.BrowserTimeZone.TrySet(timeZoneId))
        {
            this.TimeZoneId = timeZoneId;
        }
    }
}
