# Testing

`NekoTrace.Tests` is xUnit v3 against the real types — no mocking library, and no interfaces introduced just to have something to substitute. The repositories, the disk writer and the controller are all directly constructible, so tests build the actual object graph and assert on what it holds.

```powershell
dotnet test NekoTrace.slnx
```

Most of what matters is `internal` — ingest, the id helpers, the endpoint mapping — so `NekoTrace.Web.csproj` carries `<InternalsVisibleTo Include="NekoTrace.Tests" />`. Prefer widening to `internal` over making something public purely to test it.

## Where the seams are

`Program.cs` starts two `WebApplication`s and blocks on them, so `WebApplicationFactory<Program>` is not available. Tests sit one layer below that instead:

| Under test | Driven through |
| --- | --- |
| OTLP/HTTP ingest, both encodings | A `TestServer` hosting only `MapOtlpHttpEndpoints`, in `Endpoints/` |
| Span decoding, indexing, filtering | `TracesRepository.ProcessTraces` with protobuf built in `TestData/Otlp.cs` |
| Trace file download and upload | `TraceFilesController` on a `DefaultHttpContext`, in `Controllers/` |
| Disk persistence | `TraceDiskWriter.Timer_Tick`, called directly against a temp directory |

`Timer_Tick` is `internal` rather than `private` for that last row: ticks build on each other through `mTrackedTraces` — a rename only happens on the tick *after* the one that first wrote the file — and `DisposeAsync` only ever runs one, so waiting on the background loop cannot exercise it.

## Helpers

`TestData/Otlp.cs` builds the protobuf an exporter would send; `TestData/Fake.cs` builds the storage-side types and the repositories. Both put times in milliseconds from a fixed `ORIGIN`, so a test reads `startMs: 20` rather than a Unix nanosecond literal. Ids are the ones from the W3C Trace Context examples, so they are recognisably well formed.

## What is not covered

- The Blazor components and pages. No bUnit, no browser tests. Services they depend on are fair game though — `BrowserTimeZone` is constructed directly against a `DefaultHttpContext`.
- The gRPC services in `GrpcServices/`. They are thin, but they are untested thin.
- `Program.cs` itself — Kestrel wiring, ports, the two-host startup.
- `MetricsRepository` beyond "an export lands in the right list".

## Adding a regression test

Name the commit it defends in a comment, and say what the bug *was* — the assertion shows the fixed behaviour, but not why anyone thought otherwise. See the notes above `AddSpan_IgnoresASpanIdItAlreadyHolds` for the shape.

Then check it bites: revert the fix, watch the test fail, put the fix back. A regression test that passes against the broken code is worse than none, because it reads as cover.

Watch for a test that only passes because of where it runs. `BrowserTimeZoneTests` asserts two time zones for every conversion precisely so that the old server-offset behaviour cannot satisfy it on a machine sitting in whichever single zone was asserted.
