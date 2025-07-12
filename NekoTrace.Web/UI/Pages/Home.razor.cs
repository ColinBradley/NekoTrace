namespace NekoTrace.Web.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.JSInterop;
using NekoTrace.Web.Repositories;
using NekoTrace.Web.UI.Components;

public partial class Home : IDisposable
{
    private readonly HashSet<string> mIgnoredTraceNamesSet = new(StringComparer.OrdinalIgnoreCase);
    private string? mIgnoredTraceNamesRaw = null;

    private DateTimeOffset mLastRefreshed = DateTimeOffset.UtcNow;
    private bool mHasPendingRefresh = false;

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [SupplyParameterFromQuery]
    public string? TraceId { get; set; }

    [SupplyParameterFromQuery]
    private int? SpansMinimum { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMinimum { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMaximum { get; set; }

    [SupplyParameterFromQuery]
    private bool? HasError { get; set; }

    [SupplyParameterFromQuery]
    private string? SpanColorSelector { get; set; }

    [SupplyParameterFromQuery]
    private string? IgnoredTraceNames { get; set; }

    private string EffectiveSpanColorSelector => this.SpanColorSelector ?? TraceViewComponent.DEFAULT_SPAN_COLOR_SELECTOR;

    private HashSet<string> IgnoredTraceNamesSet
    {
        get
        {
            if (
                !string.Equals(
                    this.IgnoredTraceNames,
                    mIgnoredTraceNamesRaw,
                    StringComparison.Ordinal
                )
            )
            {
                mIgnoredTraceNamesSet.Clear();
                foreach (var traceName in this.IgnoredTraceNames?.Split('|') ?? [])
                {
                    mIgnoredTraceNamesSet.Add(traceName);
                }

                mIgnoredTraceNamesRaw = this.IgnoredTraceNames;
            }

            return mIgnoredTraceNamesSet;
        }
    }

    private GridSort<Trace> TraceStartGridSort { get; } = GridSort<Trace>.ByAscending(t => t.Start);

    private GridSort<Trace> TraceHasErrorGridSort { get; } = GridSort<Trace>.ByAscending(t => t.HasError);

    private IEnumerable<string> TraceNames =>
        this
            .TracesRepo.Traces.Where(t => t.RootSpan != null)
            .Select(t => t.RootSpan!.Name)
            .Distinct()
            .Order();

    private IQueryable<Trace> FilteredTraces =>
        this
            .TracesRepo.Traces.Where(t => (this.SpansMinimum ?? 0) <= t.Spans.Count)
            .Where(t => (this.DurationMinimum ?? 0) <= t.Duration.TotalSeconds)
            .Where(t => (this.DurationMaximum ?? double.MaxValue) >= t.Duration.TotalSeconds)
            .Where(t => this.HasError == null || t.HasError == this.HasError)
            .Where(t =>
                t.RootSpan == null
                || this.IgnoredTraceNames == null
                || !this.IgnoredTraceNames.Contains(t.RootSpan!.Name)
            );

    private IEnumerable<string> RootSpanAttributeKeys =>
        this
            .TracesRepo.Traces.SelectMany(t =>
                t.RootSpan == null ? Array.Empty<string>() : t.RootSpan.Attributes.Keys.ToArray()
            )
            .Distinct(StringComparer.OrdinalIgnoreCase);

    protected override void OnInitialized()
    {
        base.OnInitialized();

        this.TracesRepo.TracesChanged += this.TracesRepo_TracesChanged;
    }

    private async void TracesRepo_TracesChanged(string traceId)
    {
        if (mLastRefreshed < DateTimeOffset.UtcNow.AddSeconds(-0.5))
        {
            mLastRefreshed = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref mHasPendingRefresh, false);

            await this.InvokeAsync(this.StateHasChanged);
        }
        else if (!Interlocked.Exchange(ref mHasPendingRefresh, true))
        {
            await Task.Delay(500);

            mLastRefreshed = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref mHasPendingRefresh, false);

            await this.InvokeAsync(this.StateHasChanged);
        }
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

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(
                nameof(this.DurationMinimum),
                this.DurationMinimum
            ),
            replace: true
        );
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

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(
                nameof(this.DurationMaximum),
                this.DurationMaximum
            ),
            replace: true
        );
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

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.SpansMinimum), this.SpansMinimum),
            replace: true
        );
    }

    private void ToggleHasError(bool value)
    {
        this.HasError = (this.HasError, value) switch
        {
            (null, false) => false,
            (null, true) => true,
            (false, false) => null,
            (false, true) => true,
            (true, false) => false,
            (true, true) => null,
        };

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.HasError), this.HasError),
            replace: true
        );
    }

    private void SpanColorSelector_Change(ChangeEventArgs e)
    {
        var newValue = e.Value as string;
        if (string.IsNullOrWhiteSpace(newValue) || newValue is TraceViewComponent.DEFAULT_SPAN_COLOR_SELECTOR)
        {
            newValue = null;
        }

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(
                nameof(this.SpanColorSelector),
                newValue
            ),
            replace: true
        );
    }

    private void ToggleTraceNameFilter(string traceName)
    {
        var set = this.IgnoredTraceNamesSet;
        if (!set.Add(traceName))
        {
            set.Remove(traceName);
        }

        mIgnoredTraceNamesRaw = string.Join('|', set.Order(StringComparer.Ordinal));

        this.IgnoredTraceNames = mIgnoredTraceNamesRaw;

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(
                nameof(this.IgnoredTraceNames),
                this.IgnoredTraceNames
            ),
            replace: true
        );
    }

    public void Dispose()
    {
        this.TracesRepo.TracesChanged -= this.TracesRepo_TracesChanged;
    }
}
