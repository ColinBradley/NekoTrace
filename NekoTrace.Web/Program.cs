using ApexCharts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.GrpcServices;
using NekoTrace.Web.Repositories.Metrics;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.UI;

var configFilePath = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.Personal,
        Environment.SpecialFolderOption.DoNotVerify
    ),
    ".nekotrace",
    "config.json"
);

Console.WriteLine($"Config path: {configFilePath}\n");

var webAppBuilder = WebApplication.CreateBuilder(args);
webAppBuilder.Configuration.AddJsonFile(configFilePath, optional: true, reloadOnChange: true);

var nekoTraceConfigurationSection = webAppBuilder.Configuration.GetSection("NekoTrace");
webAppBuilder.Services.Configure<NekoTraceConfiguration>(nekoTraceConfigurationSection);
var nekoTraceConfiguration = new NekoTraceConfiguration();
nekoTraceConfigurationSection.Bind(nekoTraceConfiguration);

using var traces = new TracesRepository(webAppBuilder.Configuration);
using var metrics = new MetricsRepository();

var collectorAppTask = Task.Run(async () =>
{
    var collectorAppBuilder = WebApplication.CreateBuilder(args);
    collectorAppBuilder.Configuration.Sources.Clear();

    collectorAppBuilder.Logging.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "[HH:mm:ss] Collec\\tor: ";
    });

    // Remove pointless message about not having any app parts
    collectorAppBuilder.Logging.AddFilter(
        "Microsoft.AspNetCore.Mvc.Infrastructure.DefaultActionDescriptorCollectionProvider",
        LogLevel.Warning
    );

    collectorAppBuilder.Services.AddGrpc();

    collectorAppBuilder.Services.AddSingleton(traces);
    collectorAppBuilder.Services.AddSingleton(metrics);

    collectorAppBuilder.WebHost.ConfigureKestrel(o =>
        o.ListenAnyIP(
            nekoTraceConfiguration.CollectionPort,
            c => c.Protocols = HttpProtocols.Http2
        )
    );

    var collectorApp = collectorAppBuilder.Build();

    collectorApp.MapGrpcService<LogsServiceImplementation>();
    collectorApp.MapGrpcService<MetricsServiceImplementation>();
    collectorApp.MapGrpcService<ProfilesServiceImplementation>();
    collectorApp.MapGrpcService<TraceServiceImplementation>();

    await collectorApp.RunAsync();
});

var webAppTask = Task.Run(async () =>
{
    webAppBuilder.Logging.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "[HH:mm:ss] Web: ";
    });

    webAppBuilder.Services.AddSingleton(traces);
    webAppBuilder.Services.AddSingleton(metrics);

    webAppBuilder.Services.AddApexCharts();
    webAppBuilder.Services.AddHttpContextAccessor();
    webAppBuilder.Services.AddRazorComponents().AddInteractiveServerComponents();
    webAppBuilder.Services.AddControllers();

    webAppBuilder.WebHost.ConfigureKestrel(
        o =>
        {
            o.ListenAnyIP(nekoTraceConfiguration.WebApplicationPort);
        }
    );

    var webApp = webAppBuilder.Build();

    webApp.UseAntiforgery();

    webApp.MapStaticAssets();
    webApp.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    webApp.MapControllers();

    webApp.Lifetime.ApplicationStarted.Register(
        () =>
        {
            Console.WriteLine($"\nBrowse here: http://localhost:{nekoTraceConfiguration.WebApplicationPort}");
        }
    );

    await webApp.RunAsync();
});

await Task.WhenAny(collectorAppTask, webAppTask);
