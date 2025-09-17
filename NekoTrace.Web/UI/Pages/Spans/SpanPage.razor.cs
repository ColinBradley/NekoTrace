namespace NekoTrace.Web.UI.Pages.Spans;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using NekoTrace.Web.Repositories;
using NekoTrace.Web.UI.Components;

public sealed partial class SpanPage : IDisposable
{
    private ImmutableDictionary<string, string> mSpanAttributeFilter = ImmutableDictionary<
        string,
        string
    >.Empty;
    private string? mSpanAttributeFilterRaw = null;

    private DateTimeOffset? mStartTime;
    private string? mStartTimeRaw;

    private DateTimeOffset? mEndTime;
    private string? mEndTimeRaw;

    private bool mHasPendingRefresh = false;

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Parameter]
    public required string SpanName { get; set; }

    [SupplyParameterFromQuery]
    public string? TraceId { get; set; }

    [SupplyParameterFromQuery]
    private string? SpanColorSelector { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMinimum { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMaximum { get; set; }

    [SupplyParameterFromQuery]
    private bool? HasError { get; set; }

    [SupplyParameterFromQuery]
    private string? CustomColumns { get; set; }

    [SupplyParameterFromQuery]
    private string? SpanAttributeFilter { get; set; }

    [SupplyParameterFromQuery]
    private string? StartTime { get; set; }

    [SupplyParameterFromQuery]
    private string? EndTime { get; set; }

    private string EffectiveSpanColorSelector =>
        this.SpanColorSelector ?? TraceViewComponent.DEFAULT_SPAN_COLOR_SELECTOR;

    private string? NewColumnValue { get; set; }

    private string[] EffectiveCustomColumns =>
       this.CustomColumns?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private string TracesGridStyle =>
        $"grid-template-columns: min-content minmax(15ch, 1fr) min-content min-content min-content {string.Join(' ', this.EffectiveCustomColumns.Select(_ => "min-content"))};";

    private ImmutableDictionary<string, string> ParsedSpanAttributesFilter
    {
        get
        {
            if (
                !string.Equals(
                    this.SpanAttributeFilter,
                    mSpanAttributeFilterRaw,
                    StringComparison.Ordinal
                )
            )
            {
                if (!string.IsNullOrWhiteSpace(this.SpanAttributeFilter))
                {
                    mSpanAttributeFilter = ImmutableDictionary<string, string>.Empty.AddRange(
                        this.SpanAttributeFilter.Split(';')
                            .Select<string, KeyValuePair<string, string>?>(f =>
                                f.Split(':') switch
                                {
                                    [string k, string v] => new KeyValuePair<string, string>(
                                        k.Trim(),
                                        v.Trim()
                                    ),
                                    _ => null,
                                }
                            )
                            .Where(p => p is not null)
                            .Select(p => p!.Value)
                            .DistinctBy(p => p.Key)
                    );
                }

                mSpanAttributeFilterRaw = this.SpanAttributeFilter;
            }

            return mSpanAttributeFilter;
        }
    }

    private DateTimeOffset? EffectiveStartTime
    {
        get
        {
            if (!string.Equals(this.StartTime, mStartTimeRaw, StringComparison.OrdinalIgnoreCase))
            {
                mStartTime = DateTimeOffset.TryParse(this.StartTime, out var result) ? result : null;
                mStartTimeRaw = this.StartTime;
            }

            return mStartTime;
        }
    }

    private DateTimeOffset? EffectiveEndTime
    {
        get
        {
            if (!string.Equals(this.EndTime, mEndTimeRaw, StringComparison.OrdinalIgnoreCase))
            {
                mEndTime = DateTimeOffset.TryParse(this.EndTime, out var result) ? result : null;
                mEndTimeRaw = this.EndTime;
            }

            return mEndTime;
        }
    }

    private GridSort<SpanData> TraceStartGridSort { get; } = GridSort<SpanData>.ByAscending(t => t.StartTime);

    private GridSort<SpanData> SpanHasErrorGridSort { get; } =
        GridSort<SpanData>.ByAscending(t => t.StatusCode == OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error);

    private ImmutableList<SpanData> Spans => 
        this.TracesRepo.SpanRepositoriesByName.TryGetValue(this.SpanName, out var spanRepository)
            ? spanRepository.Spans
            : [];

    private IQueryable<SpanData> FilteredSpans =>
        this.Spans
            .Where(s => (this.DurationMinimum ?? 0) <= s.Duration.TotalSeconds)
            .Where(s => (this.DurationMaximum ?? double.MaxValue) >= s.Duration.TotalSeconds)
            .Where(s => this.HasError == null || ((s.StatusCode == OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error) == this.HasError))
            .Where(s => this.EffectiveStartTime == null || this.EffectiveStartTime < s.StartTime)
            .Where(s => this.EffectiveEndTime == null || this.EffectiveEndTime > s.StartTime)
            .Where(this.SpanPassesFilter)
            .AsQueryable();

    private IEnumerable<string> SpanAttributeKeys =>
        this.Spans
            .SelectMany(s => s.Attributes.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    protected override void OnInitialized()
    {
        base.OnInitialized();

        this.TracesRepo.TracesChanged += this.TracesRepo_TracesChanged;
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

    private void DurationMinimum_Change(ChangeEventArgs e)
    {
        this.DurationMinimum =
            double.TryParse(e.Value as string, out var value) 
            && value is > 0 
                ? value 
                : null;

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
        this.DurationMaximum = 
            double.TryParse(e.Value as string, out var value) 
            && value is > 0 
                ? value 
                : null;

        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(
                nameof(this.DurationMaximum),
                this.DurationMaximum
            ),
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

    private bool SpanPassesFilter(SpanData span)
    {
        var parsedSpanAttributesFilter = this.ParsedSpanAttributesFilter;
        if (parsedSpanAttributesFilter.Count is 0)
        {
            return true;
        }

        return parsedSpanAttributesFilter.Any(filterPair =>
            span.Attributes.TryGetValue(filterPair.Key, out var spanAttributeValue)
            && string.Equals(
                filterPair.Value,
                spanAttributeValue?.ToString(),
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    public void Dispose()
    {
        this.TracesRepo.TracesChanged -= this.TracesRepo_TracesChanged;
    }
}
