namespace NekoTrace.Tests.Endpoints;

using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NekoTrace.Tests.TestData;
using NekoTrace.Web.Endpoints;
using NekoTrace.Web.Repositories.Metrics;
using NekoTrace.Web.Repositories.Traces;
using OpenTelemetry.Proto.Collector.Trace.V1;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

/// <summary>
/// The OTLP/HTTP endpoints driven over a real request pipeline, so content negotiation and the charset
/// handling are exercised rather than assumed.
/// </summary>
public sealed class OtlpHttpEndpointsTests
{
    [Fact]
    public async Task PostTraces_AcceptsProtobufAndAnswersInProtobuf()
    {
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostProtobuf(
            "/v1/traces",
            Otlp.Request(Otlp.Span(name: "GET /things"))
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);

        var body = ExportTraceServiceResponse.Parser.ParseFrom(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(0, body.PartialSuccess.RejectedSpans);
        Assert.Equal(Otlp.TRACE_ID, Assert.Single(harness.Traces.Traces).Id);
    }

    [Fact]
    public async Task PostTraces_AcceptsOtlpJsonAndAnswersInJson()
    {
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson("/v1/traces", TraceJson(Otlp.ROOT_SPAN_ID));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "partialSuccess",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PostTraces_StoresTheHexIdsAnOtlpJsonSenderWrote()
    {
        // f909517. A 32 character hex trace id is itself valid base64, so before the normalizer these
        // decoded into a different 24 bytes and NekoTrace showed an id the sender had never logged.
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson("/v1/traces", TraceJson(Otlp.ROOT_SPAN_ID));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var trace = Assert.Single(harness.Traces.Traces);

        Assert.Equal(Otlp.TRACE_ID, trace.Id);

        var span = Assert.Single(trace.Spans);

        Assert.Equal(Otlp.ROOT_SPAN_ID, span.Id);
        Assert.Equal(Otlp.TRACE_ID, span.TraceId);
    }

    [Fact]
    public async Task PostTraces_PutsJsonAndProtobufSpansOnTheSameTrace()
    {
        // The sharpest form of the regression: two exporters describing one trace must agree on its id.
        await using var harness = await Harness.StartAsync();

        using var jsonResponse = await harness.PostJson("/v1/traces", TraceJson(Otlp.ROOT_SPAN_ID));
        using var protobufResponse = await harness.PostProtobuf(
            "/v1/traces",
            Otlp.Request(Otlp.Span(spanId: Otlp.CHILD_SPAN_ID, parentSpanId: Otlp.ROOT_SPAN_ID))
        );

        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, protobufResponse.StatusCode);

        var trace = Assert.Single(harness.Traces.Traces);

        Assert.Equal(2, trace.Spans.Count);
        Assert.Equal(Otlp.ROOT_SPAN_ID, trace.RootSpan?.Id);
    }

    [Fact]
    public async Task PostTraces_StillAcceptsAStrictlyProto3JsonSender()
    {
        // Ids that are genuinely base64 are passed through, so a compliant sender is not broken by the fix.
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson(
            "/v1/traces",
            TraceJson(Fake.ToBase64(Otlp.ROOT_SPAN_ID), Fake.ToBase64(Otlp.TRACE_ID))
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Otlp.TRACE_ID, Assert.Single(harness.Traces.Traces).Id);
    }

    [Fact]
    public async Task PostTraces_ReadsAJsonBodyDeclaringItsCharset()
    {
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson(
            "/v1/traces",
            TraceJson(Otlp.ROOT_SPAN_ID),
            contentType: "application/json; charset=utf-8"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(harness.Traces.Traces);
    }

    [Fact]
    public async Task PostTraces_FallsBackToUtf8ForACharsetItCannotResolve()
    {
        // A Content-Type NekoTrace can't read is not a reason to drop telemetry.
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson(
            "/v1/traces",
            TraceJson(Otlp.ROOT_SPAN_ID),
            contentType: "application/json; charset=definitely-not-a-charset"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(harness.Traces.Traces);
    }

    [Fact]
    public async Task PostTraces_RejectsAContentTypeItDoesNotUnderstand()
    {
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson(
            "/v1/traces",
            TraceJson(Otlp.ROOT_SPAN_ID),
            contentType: "text/plain"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(harness.Traces.Traces);
    }

    [Fact]
    public async Task PostMetrics_AcceptsProtobuf()
    {
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostProtobuf("/v1/metrics", Otlp.GaugeRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("test.gauge", Assert.Single(harness.Metrics.Gauges).Name);
    }

    [Fact]
    public async Task PostMetrics_AcceptsJson()
    {
        await using var harness = await Harness.StartAsync();

        using var response = await harness.PostJson(
            "/v1/metrics",
            """
            {
                "resourceMetrics": [{
                    "resource": {
                        "attributes": [
                            { "key": "service.name", "value": { "stringValue": "checkout" } }
                        ]
                    },
                    "scopeMetrics": [{
                        "scope": { "name": "test.scope" },
                        "metrics": [{
                            "name": "test.gauge",
                            "gauge": {
                                "dataPoints": [
                                    { "timeUnixNano": "1775000000000000000", "asDouble": 42 }
                                ]
                            }
                        }]
                    }]
                }]
            }
            """
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var gauge = Assert.Single(harness.Metrics.Gauges);

        Assert.Equal("test.gauge", gauge.Name);
        Assert.Single(gauge.DataPoints);
    }

    /// <summary>An OTLP/JSON export of one span, with whatever id encoding the caller wants to try.</summary>
    private static string TraceJson(string spanId, string traceId = Otlp.TRACE_ID) =>
        $$"""
        {
            "resourceSpans": [{
                "resource": {
                    "attributes": [
                        { "key": "service.name", "value": { "stringValue": "checkout" } }
                    ]
                },
                "scopeSpans": [{
                    "scope": { "name": "test.scope", "version": "1.0.0" },
                    "spans": [{
                        "traceId": "{{traceId}}",
                        "spanId": "{{spanId}}",
                        "name": "GET /things",
                        "kind": 2,
                        "startTimeUnixNano": "1775000000000000000",
                        "endTimeUnixNano": "1775000000100000000",
                        "status": {}
                    }]
                }]
            }]
        }
        """;

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(IHost host, TracesRepository traces, MetricsRepository metrics)
        {
            this.Host = host;
            this.Traces = traces;
            this.Metrics = metrics;
            this.Client = host.GetTestClient();
        }

        public IHost Host { get; }

        public HttpClient Client { get; }

        public TracesRepository Traces { get; }

        public MetricsRepository Metrics { get; }

        public static async Task<Harness> StartAsync()
        {
            var traces = Fake.TracesRepository();
            var metrics = new MetricsRepository(Fake.Configuration());

            var host = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                    webHost
                        .UseTestServer()
                        .ConfigureServices(services => services.AddRouting())
                        .Configure(application =>
                        {
                            application.UseRouting();
                            application.UseEndpoints(
                                endpoints => endpoints.MapOtlpHttpEndpoints(traces, metrics)
                            );
                        })
                )
                .StartAsync(TestContext.Current.CancellationToken);

            return new Harness(host, traces, metrics);
        }

        public async Task<HttpResponseMessage> PostProtobuf(string path, IMessage message)
        {
            using var content = new ByteArrayContent(message.ToByteArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

            return await this.Post(path, content);
        }

        public async Task<HttpResponseMessage> PostJson(
            string path,
            string json,
            string contentType = "application/json"
        )
        {
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));

            // Added without validation so a deliberately unresolvable charset reaches the server.
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);

            return await this.Post(path, content);
        }

        private Task<HttpResponseMessage> Post(string path, HttpContent content) =>
            this.Client.PostAsync(
                new Uri(path, UriKind.Relative),
                content,
                TestContext.Current.CancellationToken
            );

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();

            await this.Host.StopAsync(TestContext.Current.CancellationToken);

            this.Host.Dispose();
            this.Traces.Dispose();
            this.Metrics.Dispose();
        }
    }
}
