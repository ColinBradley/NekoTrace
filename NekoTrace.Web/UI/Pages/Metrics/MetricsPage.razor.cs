namespace NekoTrace.Web.UI.Pages.Metrics;

using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using NekoTrace.Web.Repositories.Metrics;
using OpenTelemetry.Proto.Metrics.V1;
using System.Linq;

public partial class MetricsPage
{
    public MetricsPage()
    {
        this.ChartOptions.Theme = new Theme()
        {
            Mode = Mode.Dark,
            Palette = PaletteType.Palette1,
        };
        //this.ChartOptions.Chart.Background = "0000";
        this.ChartOptions.Chart.Animations = new Animations()
        {
            Enabled = false,
        };
        this.ChartOptions.Xaxis = new XAxis()
        {
            Type = XAxisType.Datetime,
            Labels = new XAxisLabels()
            {
                DatetimeUTC = true,
            },
        };
        this.ChartOptions.Stroke = new Stroke()
        {
            Curve = Curve.Smooth,
            Width = 3,
        };
        this.ChartOptions.Tooltip = new Tooltip()
        {
            X = new TooltipX()
            {
                Format = "HH:mm:ss",
                Show = false,
            }
        };
        this.ChartOptions.Grid = new Grid()
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

    private ApexChart<NumberDataPoint>? Chart { get; set; }

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

    private ApexChartOptions<NumberDataPoint> ChartOptions { get; } = new();

    private async void MetricLink_Click()
    {
        // I dislike ApexChart's need for this
        await Task.Delay(10);

        _ = this.Chart?.RenderAsync();
    }

    private void ShowResource_Input(ChangeEventArgs e)
    {
        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.ShowResource), (bool)e.Value!));
    }

    private void ShowScopeName_Input(ChangeEventArgs e)
    {
        this.Navigation.NavigateTo(this.Navigation.GetUriWithQueryParameter(nameof(this.ShowScopeName), (bool)e.Value!));
    }
}