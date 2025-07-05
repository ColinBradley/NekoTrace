namespace InfoCat.Web.UI.Pages;

using System.Collections.Immutable;
using InfoCat.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.JSInterop;

public partial class Home : IDisposable
{
    private ImmutableList<SpanData>? mClientSpans;
    private DotNetObjectReference<Home>? mSelfReference;

    private readonly HashSet<string> mIgnoredTraceNames = [];

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required IJSRuntime JSRuntime { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [SupplyParameterFromQuery]
    public string? TraceId { get; set; }

    private ElementReference? TraceFlameCanvas { get; set; }

    private IJSObjectReference? TraceModule { get; set; }

    private Trace? SelectedTrace =>
        this.TraceId is null
            ? null
            : this.TracesRepo.TryGetTrace(this.TraceId);

    private SpanData? SelectedSpan { get; set; }

    [SupplyParameterFromQuery]
    private int? SpansMinimum { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMinimum { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMaximum { get; set; }

    [SupplyParameterFromQuery]
    private bool? GroupSpans { get; set; }

    private GridSort<Trace> TraceStartGridSort { get; } = GridSort<Trace>.ByAscending(t => t.Start);

    private IEnumerable<string> TraceNames =>
        this.TracesRepo.Traces
            .Where(t => t.RootSpan != null)
            .Select(t => t.RootSpan!.Name)
            .Distinct()
            .Order();

    private IQueryable<Trace> FilteredTraces => 
        this.TracesRepo.Traces
            .Where(t => (this.SpansMinimum ?? 0) <= t.Spans.Count)
            .Where(t => (this.DurationMinimum ?? 0) <= t.Duration.TotalSeconds)
            .Where(t => (this.DurationMaximum ?? double.MaxValue) >= t.Duration.TotalSeconds)
            .Where(t => t.RootSpan == null || !mIgnoredTraceNames.Contains(t.RootSpan!.Name));

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
        if (this.TraceModule is null || selectedTrace is null || this.TraceFlameCanvas is null || object.ReferenceEquals(mClientSpans, selectedTrace.Spans))
        {
            return;
        }

        mClientSpans = selectedTrace.Spans;

        await this.TraceModule.InvokeVoidAsync(
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

    private void DurationMinimum_Change(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value as string, out var value) && value is > 0)
        {
            this.DurationMinimum = value;
        }
        else
        {
            this.DurationMinimum = null;
        }

        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.DurationMinimum), this.DurationMinimum), replace: true);
    }

    private void DurationMaximum_Change(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value as string, out var value) && value is > 0)
        {
            this.DurationMaximum = value;
        }
        else
        {
            this.DurationMaximum = null;
        }

        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.DurationMaximum), this.DurationMaximum), replace: true);
    }

    private void SpansMinimum_Change(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value as string, out var value) && value is > 1)
        {
            this.SpansMinimum = value;
        }
        else
        {
            this.SpansMinimum = null;
        }

        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.SpansMinimum), this.SpansMinimum), replace: true);
    }

    private void GroupSpans_Change(ChangeEventArgs e)
    {
        this.GroupSpans = (e.Value as bool? ?? false) ? null : false;

        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.GroupSpans), this.GroupSpans), replace: true);
    }

    private void ToggleTraceNameFilter(string traceName)
    {
        if (!mIgnoredTraceNames.Add(traceName))
        {
            mIgnoredTraceNames.Remove(traceName);
        }
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
