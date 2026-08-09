namespace NekoTrace.Web.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Services;
using NekoTrace.Web.UI.Components;
using NekoTrace.Web.Utilities;
using System.Collections.Immutable;
using System.Linq;

public sealed partial class Home : IDisposable
{
    private static readonly string sAssemblyVersion = "v" + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "Unknown");

    private TraceFilter mCurrentFilter = TraceFilter.Empty;
    private string? mCurrentFilterCacheKey;

    private bool mHasPendingRefresh;

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Inject]
    public required BrowserTimeZone BrowserTimeZone { get; set; }

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

    [SupplyParameterFromQuery]
    private string? ExclusiveTraceNames { get; set; }

    [SupplyParameterFromQuery]
    private string? CustomColumns { get; set; }

    [SupplyParameterFromQuery]
    private string? SpanAttributeFilter { get; set; }

    [SupplyParameterFromQuery]
    private string? StartTime { get; set; }

    [SupplyParameterFromQuery]
    private string? EndTime { get; set; }

    private string? NewColumnValue { get; set; }

    private string EffectiveSpanColorSelector =>
        this.SpanColorSelector ?? TraceViewComponent.DEFAULT_SPAN_COLOR_SELECTOR;

    private string[] EffectiveCustomColumns =>
        this.CustomColumns?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private string TracesGridStyle =>
        $"grid-template-columns: min-content minmax(15ch, 1fr) min-content min-content min-content {string.Join(' ', this.EffectiveCustomColumns.Select(_ => "min-content"))};";

    private TraceFilter CurrentFilter
    {
        get
        {
            // Build a cache key from all filter parameters. The time zone is in there because the start and end
            // times are read in the browser's zone, so the same text means a different instant once it changes.
            var cacheKey = $"{SpansMinimum}|{DurationMinimum}|{DurationMaximum}|{HasError}|{IgnoredTraceNames}|{ExclusiveTraceNames}|{SpanAttributeFilter}|{StartTime}|{EndTime}|{this.BrowserTimeZone.Id}";

            if (!string.Equals(cacheKey, mCurrentFilterCacheKey, StringComparison.Ordinal))
            {
                mCurrentFilter = new TraceFilter()
                {
                    SpansMinimum = this.SpansMinimum,
                    DurationMinimum = this.DurationMinimum,
                    DurationMaximum = this.DurationMaximum,
                    HasError = this.HasError,
                    IgnoredTraceNames = string.IsNullOrEmpty(this.IgnoredTraceNames)
                        ? ImmutableHashSet<string>.Empty
                        : this.IgnoredTraceNames.Split('|', StringSplitOptions.RemoveEmptyEntries)
                            .ToImmutableHashSet(StringComparer.Ordinal),
                    ExclusiveTraceNames = string.IsNullOrEmpty(this.ExclusiveTraceNames)
                        ? null
                        : this.ExclusiveTraceNames.Split('|', StringSplitOptions.RemoveEmptyEntries)
                            .ToImmutableHashSet(StringComparer.Ordinal),
                    SpanAttributeFilter = string.IsNullOrEmpty(this.SpanAttributeFilter)
                        ? ImmutableDictionary<string, string>.Empty
                        : this.SpanAttributeFilter.Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(pair => pair.Split('=', 2))
                            .Where(parts => parts.Length == 2)
                            .Select(parts => new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()))
                            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
                            .DistinctBy(kvp => kvp.Key, StringComparer.Ordinal)
                            .ToImmutableDictionary(StringComparer.Ordinal),
                    StartTime = this.BrowserTimeZone.ParseInputToLocal(this.StartTime),
                    EndTime = this.BrowserTimeZone.ParseInputToLocal(this.EndTime),
                };

                mCurrentFilterCacheKey = cacheKey;
            }

            return mCurrentFilter;
        }
    }

    private ImmutableHashSet<string> IgnoredTraceNamesSet => this.CurrentFilter.IgnoredTraceNames;

    private ImmutableHashSet<string> ExclusiveTraceNamesSet => this.CurrentFilter.ExclusiveTraceNames ?? ImmutableHashSet<string>.Empty;

    private GridSort<TraceItem> TraceStartGridSort { get; } =
        GridSort<TraceItem>.ByAscending(t => t.Start);

    private GridSort<TraceItem> TraceHasErrorGridSort { get; } =
        GridSort<TraceItem>.ByAscending(t => t.HasError);

    private IEnumerable<(string Name, int Count)> TraceNamesWithCounts =>
        this.TracesRepo.Traces
            .AsEnumerable()
            .Where(t => t.RootSpan != null)
            .Select(t => t.RootSpan!.Name)
            .GroupBy(n => n, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count()))
            .OrderBy(g => g.Key);

    private IQueryable<TraceItem> FilteredTraces =>
        this.TracesRepo.Traces.Where(t => this.CurrentFilter.Matches(t));

    private IEnumerable<string> RootSpanAttributeKeys =>
        this.TracesRepo.Traces
            .SelectMany(t =>
                t.RootSpan == null ? Array.Empty<string>() : t.RootSpan.Attributes.Keys.ToArray()
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order();

    protected override void OnInitialized()
    {
        base.OnInitialized();

        this.TracesRepo.TracesChanged += this.TracesRepo_TracesChanged;
        this.BrowserTimeZone.Changed += this.StateHasChanged;
    }

    private async void TracesRepo_TracesChanged()
    {
        if (!Interlocked.Exchange(ref mHasPendingRefresh, true))
        {
            await this.InvokeAsync(this.StateHasChanged);

            await Task.Delay(500);

            await this.InvokeAsync(this.StateHasChanged);
            Interlocked.Exchange(ref mHasPendingRefresh, false);
        }
    }

    private void DurationMinimum_Change(ChangeEventArgs e)
    {
        this.DurationMinimum = InputValues.TryParseDouble(e.Value) is double value and > 0 ? value : null;

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
        this.DurationMaximum = InputValues.TryParseDouble(e.Value) is double value and > 0 ? value : null;

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
        this.SpansMinimum = InputValues.TryParseInt32(e.Value) is int value and > 1 ? value : null;

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
        if (
            string.IsNullOrWhiteSpace(newValue)
            || newValue is TraceViewComponent.DEFAULT_SPAN_COLOR_SELECTOR
        )
        {
            newValue = null;
        }

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.SpanColorSelector), newValue),
            replace: true
        );
    }

    private void SpanAttributeFilter_Change(ChangeEventArgs e)
    {
        var newValue = e.Value as string;
        if (string.IsNullOrWhiteSpace(newValue))
        {
            newValue = null;
        }

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.SpanAttributeFilter), newValue),
            replace: true
        );
    }

    private void StartTime_Input(ChangeEventArgs e)
    {
        var newValue = e.Value as string;
        if (string.IsNullOrWhiteSpace(newValue))
        {
            newValue = null;
        }

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.StartTime), newValue),
            replace: true
        );
    }

    private void EndTime_Input(ChangeEventArgs e)
    {
        var newValue = e.Value as string;
        if (string.IsNullOrWhiteSpace(newValue))
        {
            newValue = null;
        }

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.EndTime), newValue),
            replace: true
        );
    }

    private void AddColumnForm_Submit()
    {
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(
                nameof(this.CustomColumns),
                string.Join(';', this.EffectiveCustomColumns.Concat([this.NewColumnValue]))
            ),
            replace: true
        );

        this.NewColumnValue = null;
    }

    private void RemoveColumnButton_Click(string columnName)
    {
        var newValue = string.Join(
            ';',
            this.EffectiveCustomColumns.Where(c =>
                !string.Equals(c, columnName, StringComparison.Ordinal)
            )
        );

        if (newValue.Length is 0)
        {
            newValue = null;
        }

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.CustomColumns), newValue),
            replace: true
        );
    }

    public void Dispose()
    {
        this.TracesRepo.TracesChanged -= this.TracesRepo_TracesChanged;
        this.BrowserTimeZone.Changed -= this.StateHasChanged;
    }
}
