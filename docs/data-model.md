# Data model and storage

Everything lives in memory in `Repositories/`, owned by singletons created in `Program.cs` before either web host starts and shared between both.

## Indexes

`TracesRepository` holds a `Dictionary<string, TraceItem>` keyed by lowercase hex trace id, plus a `ConcurrentDictionary<string, SpanRepository>` indexing every span by *name* — that second index is what powers the by-span-type views and their duration statistics.

Ids are hex everywhere — storage keys, display, URLs and downloaded files — matching W3C Trace Context and OTLP/JSON, so an id copied from an application's own logs pastes straight in. `Utilities/TraceIds.cs` owns the conversions; ingest goes through `TraceIds.ToHex` on the raw protobuf bytes. The one exception is described under [Disk persistence](#disk-persistence).

Both indexes must stay in sync: removing a trace also removes its spans from the per-name repositories (`RemoveTraceSpans`), and a `SpanRepository` that empties is dropped entirely.

A `TraceItem` keeps its spans ordered by start time, tracks `Start`/`End`/`Duration`/`HasError` incrementally as spans arrive, and treats a span with no parent id as `RootSpan`. Spans can arrive in any order and the root may arrive last, so nothing may assume `RootSpan` is set.

`MetricsRepository` is shaped the same way, splitting `Sums`, `Gauges` and `Histograms` per resource + scope + metric name.

## Concurrency

The pattern throughout: a `BetterReaderWriterLock` (`Utilities/`, a `using`-friendly wrapper over `ReaderWriterLockSlim`) guards mutation, while readers get lock-free immutable snapshots — `ImmutableList`/`ImmutableDictionary` properties, and `TracesRepository.Traces` rebuilt as a fresh array-backed `IQueryable` on every structural change so QuickGrid can query it safely.

Follow this rather than introducing new locking styles. Upgradeable read + double-check is the idiom for lazy computation (see `SpanRepository.AverageDuration`).

## Change notification and trimming

Notification is coarse: repositories raise `TracesChanged`/`Updated` with no payload, and pages debounce/coalesce their own re-renders.

A 1-minute timer in each repository drops data past `MaxSpanAge`/`MaxMetricAge` and re-applies the ingest filter to everything held.

## Disk persistence

Optional and off unless `TraceSaveDirectory` is set.

`TraceDiskWriter` polls every `TraceSaveInterval` and writes each qualifying trace as gzipped JSON (`TraceSerializableData`) to that directory. It tracks a per-trace `LastWrittenSpanCount` so it only rewrites when the trace has grown, writes to a `.tmp` file and moves it into place, and renames the file once a root span gives it a better name (`NekoTrace-{yyMMddTHHmmss}-{rootSpanName}-{shortId}.json.gz`). Traces the save filter definitively rejects have their file deleted. Disposal does one final tick.

`Controllers/TraceFilesController.cs` serves the same format: `GET /api/trace-files?traceId=…` downloads one trace, `POST` accepts uploads back in and replays the spans through `GetOrAddTrace`/`AddSpans`.

Upload is the **only** place base64 ids are still accepted: files downloaded before the move to hex carry base64 throughout, so `TraceIds.NormalizeToHex` rewrites every id in the file — the trace's own plus each span's `Id`, `TraceId` and `ParentSpanId`. Converting only the trace id would leave spans pointing at a key that no longer matches. The two encodings are told apart by length (a 16 byte id is 32 hex characters but 24 base64 ones), and anything matching neither is passed through untouched so an odd file still loads as a self-consistent opaque key. Don't add base64 handling anywhere else.
