# Configuration

`Configuration/NekoTraceConfiguration.cs` is the source of truth for keys. Everything lives under a `NekoTrace` section.

Sources, in order: `appsettings.json` shipped with the app, then `~/.nekotrace/config.json` (`Environment.SpecialFolder.Personal`), added with `reloadOnChange: true`. The resolved path is printed to the console at startup.

| Key | Default | Notes |
| --- | --- | --- |
| `MaxSpanAge` | null (keep forever) | `TimeSpan`; traces older than this are trimmed |
| `MaxMetricAge` | null | `TimeSpan` |
| `GrpcCollectionPort` | 4317 | HTTP/2 only |
| `HttpCollectionPort` | 4318 | HTTP/1 |
| `WebApplicationPort` | 8347 | UI |
| `TraceSaveDirectory` | null (disabled) | enables `TraceDiskWriter` |
| `TraceSaveInterval` | 5s | |
| `TraceSaveFilter` | null | `TraceFilter` query string |
| `TraceIngestFilter` | null | `TraceFilter` query string |

## Hot reload is the point

Config is read via `NekoTraceConfiguration.Get(configuration)` *at each use site* — inside the timer tick, inside the writer loop — rather than captured once at startup or injected as `IOptions`. That is what makes edits to `~/.nekotrace/config.json` take effect without a restart.

Preserve this when adding options. Anything expensive derived from a config value (like a parsed `TraceFilter`) should be cached alongside the raw string it came from and recomputed only when that string changes.

Ports are the exception — they are bound once at startup, so changing them does need a restart.
