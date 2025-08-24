using Microsoft.AspNetCore.Server.Kestrel.Core;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.GrpcServices;
using NekoTrace.Web.Repositories;
using NekoTrace.Web.UI;

var configFilePath = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.Personal,
        Environment.SpecialFolderOption.DoNotVerify
    ),
    ".nekotrace",
    "config.json"
);

Console.WriteLine($"Config path: {configFilePath}");

var webAppBuilder = WebApplication.CreateBuilder(args);
webAppBuilder.Configuration.AddJsonFile(configFilePath, optional: true, reloadOnChange: true);

webAppBuilder.Services.Configure<NekoTraceConfiguration>(webAppBuilder.Configuration.GetSection("NekoTrace"));

var traces = new TracesRepository(webAppBuilder.Configuration);

var collectorAppTask = Task.Run(async () =>
{
    var collectorAppBuilder = WebApplication.CreateBuilder(args);
    collectorAppBuilder.Configuration.Sources.Clear();

    collectorAppBuilder.Services.AddGrpc();

    collectorAppBuilder.Services.AddSingleton(traces);

    collectorAppBuilder.WebHost.ConfigureKestrel(o =>
        o.ListenAnyIP(4317, c => c.Protocols = HttpProtocols.Http2)
    );

    var app = collectorAppBuilder.Build();

    app.MapGrpcService<LogsServiceImplementation>();
    app.MapGrpcService<MetricsServiceImplementation>();
    app.MapGrpcService<ProfilesServiceImplementation>();
    app.MapGrpcService<TraceServiceImplementation>();

    await app.RunAsync();
});

var webAppTask = Task.Run(async () =>
{
    webAppBuilder.Services.AddSingleton(traces);
    webAppBuilder.Services.AddHttpContextAccessor();
    webAppBuilder.Services.AddRazorComponents().AddInteractiveServerComponents();
    webAppBuilder.Services.AddControllers();

    var app = webAppBuilder.Build();

    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    app.MapControllers();

    await app.RunAsync();
});

await Task.WhenAny(collectorAppTask, webAppTask);
