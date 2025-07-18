namespace NekoTrace.Web.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using NekoTrace.Web.Repositories;
using NekoTrace.Web.UI.Components;
using System.Collections.Immutable;

public partial class Home : IDisposable
{
    private ImmutableHashSet<string> mIgnoredTraceNamesSet = [];
    private string? mIgnoredTraceNamesRaw = null;

    private ImmutableHashSet<string> mExclusiveTraceNamesSet = [];
    private string? mExclusiveTraceNamesRaw = null;

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

    [SupplyParameterFromQuery]
    private string? ExclusiveTraceNames { get; set; }

    [SupplyParameterFromQuery]
    private string? CustomColumns { get; set; }

    private string EffectiveSpanColorSelector =>
        this.SpanColorSelector ?? TraceViewComponent.DEFAULT_SPAN_COLOR_SELECTOR;

    private string[] EffectiveCustomColumns =>
        this.CustomColumns?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private string TracesGridStyle =>
        $"grid-template-columns: min-content minmax(0, 1fr) min-content min-content min-content {string.Join(' ', this.EffectiveCustomColumns.Select(_ => "min-content"))};";

    private ImmutableHashSet<string> IgnoredTraceNamesSet
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
                mIgnoredTraceNamesSet = [.. this.IgnoredTraceNames?.Split('|') ?? []];
                mIgnoredTraceNamesRaw = this.IgnoredTraceNames;
            }

            return mIgnoredTraceNamesSet;
        }
    }

    private ImmutableHashSet<string> ExclusiveTraceNamesSet
    {
        get
        {
            if (
                !string.Equals(
                    this.ExclusiveTraceNames,
                    mExclusiveTraceNamesRaw,
                    StringComparison.Ordinal
                )
            )
            {
                mExclusiveTraceNamesSet = [.. this.ExclusiveTraceNames?.Split('|') ?? []];
                mExclusiveTraceNamesRaw = this.ExclusiveTraceNames;
            }

            return mExclusiveTraceNamesSet;
        }
    }

    private GridSort<Trace> TraceStartGridSort { get; } = GridSort<Trace>.ByAscending(t => t.Start);

    private GridSort<Trace> TraceHasErrorGridSort { get; } =
        GridSort<Trace>.ByAscending(t => t.HasError);

    private IEnumerable<(string Name, int Count)> TraceNamesWithCounts =>
        this.TracesRepo.Traces
            .AsEnumerable()
            .Where(t => t.RootSpan != null)
            .Select(t => t.RootSpan!.Name)
            .GroupBy(n => n, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count()))
            .OrderBy(g => g.Key);

    private IQueryable<Trace> FilteredTraces =>
        this.TracesRepo.Traces
            .Where(t => (this.SpansMinimum ?? 0) <= t.Spans.Count)
            .Where(t => (this.DurationMinimum ?? 0) <= t.Duration.TotalSeconds)
            .Where(t => (this.DurationMaximum ?? double.MaxValue) >= t.Duration.TotalSeconds)
            .Where(t => this.HasError == null || t.HasError == this.HasError)
            .Where(t =>
                t.RootSpan == null
                || this.IgnoredTraceNames == null
                || !this.IgnoredTraceNames.Contains(t.RootSpan!.Name)
            )
            .Where(t =>
                this.ExclusiveTraceNames == null
                || (t.RootSpan != null && this.ExclusiveTraceNamesSet.Contains(t.RootSpan.Name))
            );

    private IEnumerable<string> RootSpanAttributeKeys =>
        this.TracesRepo.Traces
            .SelectMany(t =>
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

    private string? NewColumnValue { get; set; }

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
    }
}
