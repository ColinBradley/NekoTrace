using ApexCharts;
using Google.Protobuf;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NekoTrace.Web.Configuration;
using NekoTrace.Web.GrpcServices;
using NekoTrace.Web.Repositories.Metrics;
using NekoTrace.Web.Repositories.Traces;
using NekoTrace.Web.UI;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

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

    collectorApp.MapPost("/v1/traces", async (HttpContext context) =>
    {
        ExportTraceServiceRequest exportReq;

        var isProtobuf = context.Request.ContentType?.Contains("application/x-protobuf", StringComparison.OrdinalIgnoreCase) is true;
        if (isProtobuf)
        {
            using var stream = new MemoryStream();
            await context.Request.Body.CopyToAsync(stream, context.RequestAborted);
            var bytes = stream.ToArray();

            exportReq = ExportTraceServiceRequest.Parser.ParseFrom(bytes);
        }
        else if (context.Request.HasJsonContentType())
        {
            var encoding = System.Text.Encoding.UTF8;
            try
            {
                var mediaType = Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
                var charset = mediaType.Charset.HasValue ? mediaType.Charset.Value.Trim('"') : null;
                if (!string.IsNullOrEmpty(charset))
                {
                    encoding = System.Text.Encoding.GetEncoding(charset);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
#pragma warning restore CA1031
            {
                // Ignore and keep UTF-8
            }

            using var stream = new StreamReader(context.Request.Body, encoding, detectEncodingFromByteOrderMarks: true);
            var body = await stream.ReadToEndAsync(context.Request.HttpContext.RequestAborted);

            exportReq = ExportTraceServiceRequest.Parser.ParseJson(body);
        }
        else
        {
            return Results.BadRequest("Unknown contennt type");
        }

        var result = traces.ProcessTraces(exportReq);

        if (isProtobuf)
        {
            using var memoryStream = new MemoryStream();
            result.WriteTo(memoryStream);
            return Results.Bytes(memoryStream.ToArray(), "application/x-protobuf");
        }
        else
        {
            return Results.Text(
                Google.Protobuf.JsonFormatter.Default.Format(result),
                "application/json"
            );
        }
    });

    collectorApp.MapPost("/v1/metrics", async (HttpContext context) =>
    {
        ExportMetricsServiceRequest exportReq;

        var isProtobuf = context.Request.ContentType?.Contains("application/x-protobuf", StringComparison.OrdinalIgnoreCase) is true;
        if (isProtobuf)
        {
            using var stream = new MemoryStream();
            await context.Request.Body.CopyToAsync(stream, context.RequestAborted);
            var bytes = stream.ToArray();

            exportReq = ExportMetricsServiceRequest.Parser.ParseFrom(bytes);
        }
        else if (context.Request.HasJsonContentType())
        {
            var encoding = System.Text.Encoding.UTF8;
            try
            {
                var mediaType = Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
                var charset = mediaType.Charset.HasValue ? mediaType.Charset.Value.Trim('"') : null;
                if (!string.IsNullOrEmpty(charset))
                {
                    encoding = System.Text.Encoding.GetEncoding(charset);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
#pragma warning restore CA1031
            {
                // Ignore and keep UTF-8
            }

            using var stream = new StreamReader(context.Request.Body, encoding, detectEncodingFromByteOrderMarks: true);
            var body = await stream.ReadToEndAsync(context.Request.HttpContext.RequestAborted);

            exportReq = ExportMetricsServiceRequest.Parser.ParseJson(body);
        }
        else
        {
            return Results.BadRequest("Unknown content type");
        }

        var result = metrics.ProcessMetrics(exportReq);

        if (isProtobuf)
        {
            using var memoryStream = new MemoryStream();
            result.WriteTo(memoryStream);
            return Results.Bytes(memoryStream.ToArray(), "application/x-protobuf");
        }
        else
        {
            return Results.Text(
                Google.Protobuf.JsonFormatter.Default.Format(result),
                "application/json"
            );
        }
    });

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
