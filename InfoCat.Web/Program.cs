using InfoCat.Web.GrpcServices;
using InfoCat.Web.Repositories;
using InfoCat.Web.UI;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var traces = new TracesRepository();

var collectorAppTask = Task.Run(async () =>
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddGrpc();

    builder.Services.AddSingleton(traces);

    builder.WebHost.ConfigureKestrel(o =>
        o.ListenAnyIP(4317, c => c.Protocols = HttpProtocols.Http2)
    );

    var app = builder.Build();

    app.MapGrpcService<LogsServiceImplementation>();
    app.MapGrpcService<MetricsServiceImplementation>();
    app.MapGrpcService<ProfilesServiceImplementation>();
    app.MapGrpcService<TraceServiceImplementation>();

    await app.RunAsync();
});

var webAppTask = Task.Run(async () =>
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSingleton(traces);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();

    var app = builder.Build();

    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

    await app.RunAsync();
});

await Task.WhenAny(collectorAppTask, webAppTask);
