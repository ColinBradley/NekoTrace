namespace NekoTrace.Web.UI.Pages.Spans;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using NekoTrace.Web.Repositories;

public partial class SpansPage : IDisposable
{
    private ImmutableHashSet<string> mIgnoredSpanNamesSet = [];
    private string? mIgnoredSpanNamesRaw = null;

    private ImmutableHashSet<string> mExclusiveSpanNamesSet = [];
    private string? mExclusiveSpanNamesRaw = null;

    private bool mHasPendingRefresh = false;

    [Inject]
    public required TracesRepository TracesRepo { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMinimum { get; set; }

    [SupplyParameterFromQuery]
    private double? DurationMaximum { get; set; }

    [SupplyParameterFromQuery]
    private bool? HasError { get; set; }

    [SupplyParameterFromQuery]
    private bool? RootSpansOnly { get; set; }

    [SupplyParameterFromQuery]
    private string? IgnoredSpanNames { get; set; }

    [SupplyParameterFromQuery]
    private string? ExclusiveSpanNames { get; set; }

    [SupplyParameterFromQuery]
    private string? SpanAttributeFilter { get; set; }

    private ImmutableHashSet<string> IgnoredSpanNamesSet
    {
        get
        {
            if (
                !string.Equals(
                    this.IgnoredSpanNames,
                    mIgnoredSpanNamesRaw,
                    StringComparison.Ordinal
                )
            )
            {
                mIgnoredSpanNamesSet = [.. this.IgnoredSpanNames?.Split('|') ?? []];
                mIgnoredSpanNamesRaw = this.IgnoredSpanNames;
            }

            return mIgnoredSpanNamesSet;
        }
    }

    private ImmutableHashSet<string> ExclusiveSpanNamesSet
    {
        get
        {
            if (
                !string.Equals(
                    this.ExclusiveSpanNames,
                    mExclusiveSpanNamesRaw,
                    StringComparison.Ordinal
                )
            )
            {
                mExclusiveSpanNamesSet = [.. this.ExclusiveSpanNames?.Split('|') ?? []];
                mExclusiveSpanNamesRaw = this.ExclusiveSpanNames;
            }

            return mExclusiveSpanNamesSet;
        }
    }

    private GridSort<SpanRepository> SpanErrorGridSort { get; } =
        GridSort<SpanRepository>.ByAscending(s => s.ErrorSpans.Count);

    private GridSort<SpanRepository> SpanNameGridSort { get; } =
        GridSort<SpanRepository>.ByAscending(s => s.Name);

    private IQueryable<SpanRepository> FilteredSpans =>
        this.TracesRepo.SpanRepositories
            .AsQueryable()
            .Where(s => this.RootSpansOnly == null || !this.RootSpansOnly.Value || s.IsRootSpan)
            .Where(s => (this.DurationMinimum ?? 0) <= s.MinDuration.TotalSeconds)
            .Where(s => (this.DurationMaximum ?? double.MaxValue) >= s.MaxDuration.TotalSeconds)
            .Where(s => this.HasError == null || this.HasError.Value == (s.ErrorSpans.Count > 0))
            .Where(s =>
                this.IgnoredSpanNames == null
                || !this.IgnoredSpanNames.Contains(s.Name)
            )
            .Where(s =>
                this.ExclusiveSpanNames == null
                || this.ExclusiveSpanNamesSet.Contains(s.Name)
            );

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

    private void RootSpansOnly_Change(ChangeEventArgs e)
    {
        var newValue = e.Value as bool? ?? false;
        
        this.Navigation.NavigateTo(
            this.Navigation.GetUriWithQueryParameter(nameof(this.RootSpansOnly), newValue ? newValue : null),
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

    public void Dispose()
    {
        this.TracesRepo.TracesChanged -= this.TracesRepo_TracesChanged;
    }
}
