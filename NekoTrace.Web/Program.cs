using NekoTrace.Web.GrpcServices;
using NekoTrace.Web.Repositories;
using NekoTrace.Web.UI;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var traces = new TracesRepository();

var collectorAppTask = Task.Run(async () =>
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.Sources.Clear();

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
    builder.Services.AddControllers();

    var app = builder.Build();

    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    app.MapControllers();

    await app.RunAsync();
});

await Task.WhenAny(collectorAppTask, webAppTask);
