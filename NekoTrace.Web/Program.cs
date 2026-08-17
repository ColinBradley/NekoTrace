using ApexCharts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NekoTrace.Web.Analysis;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.Endpoints;
using NekoTrace.Web.GrpcServices;
using NekoTrace.Web.Mcp;
using NekoTrace.Web.Repositories.Metrics;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.Services;
using NekoTrace.Web.UI;
using System.Globalization;

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
using var metrics = new MetricsRepository(webAppBuilder.Configuration);
await using var traceDiskWriter = new TraceDiskWriter(traces, webAppBuilder.Configuration);

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

    collectorAppBuilder.WebHost.ConfigureKestrel(
        o =>
        {
            o.ListenAnyIP(
                nekoTraceConfiguration.GrpcCollectionPort,
                c => c.Protocols = HttpProtocols.Http2
            );

            o.ListenAnyIP(
                nekoTraceConfiguration.HttpCollectionPort,
                c => c.Protocols = HttpProtocols.Http1
            );

            o.AllowSynchronousIO = true;
        }
    );

    var collectorApp = collectorAppBuilder.Build();

    collectorApp.MapGrpcService<LogsServiceImplementation>();
    collectorApp.MapGrpcService<MetricsServiceImplementation>();
    collectorApp.MapGrpcService<ProfilesServiceImplementation>();
    collectorApp.MapGrpcService<TraceServiceImplementation>();

    collectorApp.MapOtlpHttpEndpoints(traces, metrics);

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
    webAppBuilder.Services.AddSingleton<TraceViews>();

    webAppBuilder.Services.AddApexCharts();
    webAppBuilder.Services.AddHttpContextAccessor();
    webAppBuilder.Services.AddScoped<BrowserTimeZone>();
    webAppBuilder.Services.AddRazorComponents().AddInteractiveServerComponents();
    webAppBuilder.Services.AddControllers();

    // Served in process on the web host rather than as a separate stdio binary: NekoTrace is already a
    // server, so there is nothing extra to run and configuring a client is one URL. See docs/ai-access.md.
    webAppBuilder.Services
        .AddMcpServer(options => options.ServerInstructions = McpInstructions.Build())
        .WithHttpTransport()
        .WithTools<TraceTools>();

    webAppBuilder.WebHost.ConfigureKestrel(
        o =>
        {
            o.ListenAnyIP(nekoTraceConfiguration.WebApplicationPort);
        }
    );

    var webApp = webAppBuilder.Build();

    var supportedCultures = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(c => c.Name)
        .ToArray();

    webApp.UseRequestLocalization(
        new RequestLocalizationOptions()
            .AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures)
    );

    webApp.UseAntiforgery();

    webApp.MapStaticAssets();
    webApp.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    webApp.MapControllers();
    webApp.MapMcp("/mcp");

    webApp.Lifetime.ApplicationStarted.Register(
        () =>
        {
            Console.WriteLine($"\nBrowse here: http://localhost:{nekoTraceConfiguration.WebApplicationPort}");
        }
    );

    await webApp.RunAsync();
});

_ = traceDiskWriter.Start();

await Task.WhenAny(collectorAppTask, webAppTask);
