namespace NekoTrace.Web.UI.Pages.Metrics;

using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using NekoTrace.Web.Repositories.Metrics;
using OpenTelemetry.Proto.Metrics.V1;
using System.Linq;

public partial class MetricsPage
{
    private MetricItemBase? mPreviouslySelectedMetric;

    public MetricsPage()
    {
        this.HistogramChartOptions.Theme = this.MetricChartOptions.Theme = new Theme()
        {
            Mode = Mode.Dark,
            Palette = PaletteType.Palette1,
        };
        //this.MetricChartOptions.Chart.Background = "0000";
        this.HistogramChartOptions.Chart.Animations = this.MetricChartOptions.Chart.Animations = new Animations()
        {
            Enabled = false,
        };
        this.HistogramChartOptions.Xaxis = new XAxis()
        {
            Type = XAxisType.Category,
        };
        this.MetricChartOptions.Xaxis = new XAxis()
        {
            Type = XAxisType.Datetime,
            Labels = new XAxisLabels()
            {
                DatetimeUTC = true,
            },
        };
        this.MetricChartOptions.Stroke = new Stroke()
        {
            Curve = Curve.Smooth,
            Width = 3,
        };
        this.MetricChartOptions.Tooltip = new Tooltip()
        {
            X = new TooltipX()
            {
                Format = "HH:mm:ss",
                Show = false,
            }
        };
        this.HistogramChartOptions.Grid = this.MetricChartOptions.Grid = new Grid()
        {
            BorderColor = "#333",
        };
    }

    [Inject]
    public required MetricsRepository MetricsRepo { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [SupplyParameterFromQuery]
    public string? MetricName { get; set; }

    [SupplyParameterFromQuery]
    public string? ResourceKey { get; set; }

    [SupplyParameterFromQuery]
    public bool? ShowResource { get; set; }

    [SupplyParameterFromQuery]
    public bool? ShowScopeName { get; set; }

    private ApexChart<NumberDataPoint>? MetricChart { get; set; }

    private ApexChart<HistogramItem>? HistogramChart { get; set; }

    private MetricItemBase? SelectedMetric =>
        this.Metrics.FirstOrDefault(m =>
            string.Equals(m.Name, this.MetricName, StringComparison.OrdinalIgnoreCase)
            && (
                string.IsNullOrEmpty(this.ResourceKey)
                || string.Equals(m.Resource.Key.Value, this.ResourceKey, StringComparison.OrdinalIgnoreCase)
            )
        );

    private IQueryable<MetricItemBase> Metrics =>
        this.MetricsRepo.Sums
            .Cast<MetricItemBase>()
            .Concat(this.MetricsRepo.Gauges)
            .Concat(this.MetricsRepo.Histograms)
            .AsQueryable();

    private GridSort<MetricItemBase> MetricNameGridSort { get; } =
        GridSort<MetricItemBase>.ByAscending(i => i.Name);

    private ApexChartOptions<NumberDataPoint> MetricChartOptions { get; } = new();

    private ApexChartOptions<HistogramItem> HistogramChartOptions { get; } = new();

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);

        mPreviouslySelectedMetric?.Updated -= this.Refresh;

        mPreviouslySelectedMetric = this.SelectedMetric;

        mPreviouslySelectedMetric?.Updated += this.Refresh;
    }

    private void Refresh()
    {
        _ = this.InvokeAsync(
            () =>
            {
                this.StateHasChanged();

                this.MetricChart?.RenderAsync();
                this.HistogramChart?.RenderAsync();
            }
        );
    }

    private async void MetricLink_Click()
    {
        // I dislike ApexChart's need for this
        await Task.Delay(10);

        _ = this.MetricChart?.RenderAsync();
        _ = this.HistogramChart?.RenderAsync();
    }

    private void ShowResource_Input(ChangeEventArgs e)
    {
        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.ShowResource), (bool)e.Value!));
    }

    private void ShowScopeName_Input(ChangeEventArgs e)
    {
        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.ShowScopeName), (bool)e.Value!));
    }

    private sealed record HistogramItem(ulong Count, double Bound);
}