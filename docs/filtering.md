# Filtering

`Repositories/Traces/TraceFilter.cs` is one filter language serving four consumers:

- the UI's `[SupplyParameterFromQuery]` page state (Home page trace table);
- the `TraceIngestFilter` config value — applied on ingest and on each trim tick, discarding traces outright;
- the `TraceSaveFilter` config value — applied by `TraceDiskWriter` to decide what reaches disk;
- `GET /api/traces`, which parses its own query string, so a URL copied out of the UI's address bar filters the API identically.

## Syntax

It parses a *query string*, so the UI's address bar and the config values use identical syntax:

| Key | Form |
| --- | --- |
| `SpansMinimum` | int > 0 |
| `DurationMinimum` / `DurationMaximum` | seconds, invariant culture, > 0 |
| `HasError` | bool |
| `IgnoredTraceNames` / `ExclusiveTraceNames` | root span names, `|`-separated |
| `SpanAttributeFilter` | `key=value;key=value`, matched case-insensitively against *any* span |
| `StartTime` / `EndTime` | ISO 8601; read as UTC when it carries no offset |

Every value is read with the invariant culture, and a time with no offset of its own is UTC. None of the three consumers of `Parse` has a browser to ask, so the host's culture and zone are the only other candidates and both make one string mean different things on different machines. The UI does not come through `Parse` at all — it builds a `TraceFilter` directly through `BrowserTimeZone.ParseInputToLocal`, which is where a viewer's zone belongs.

Unparseable or out-of-range values are silently ignored rather than erroring.

## `Matches` vs `IsRejected`

These are deliberately different and both need updating when a dimension is added.

- `Matches` — "show/save this trace now". Used for display and for deciding what to write to disk.
- `IsRejected` — the stricter "this can never qualify, throw it away". Used for ingest-time discard, trim-time removal, and deleting already-written files.

A trace that is merely incomplete must not be rejected: it may still have no root span, or still be growing toward `SpansMinimum` or `DurationMinimum`. Only criteria that can no longer be satisfied belong in `IsRejected` — which is why, for example, `DurationMaximum` rejects but `DurationMinimum` does not.

## Adding a dimension

Touch all of: the record properties, `IsEmpty`, `Parse`, `Matches`, `IsRejected`, the UI controls plus their query parameters, **`Mcp/TraceTools.ListTraces`** and **`NekoTrace.Cli/Commands/TracesCommand.cs`**.

Those last two are the easy ones to forget, because unlike the other consumers neither takes a query string. Both spell every dimension out as its own described parameter — a model handed one opaque `filter` string has nothing in the schema telling it what may go in there, and a `--filter` flag taking one is no better on a command line — so a dimension added here and not there is one an MCP or CLI caller simply cannot reach. `TraceTools` carries the same note. The two use the same names as each other and map them onto the keys above, so `--started-after` and `startedAfter` both mean `StartTime`.

Callers cache the parsed filter and re-parse only when the raw config string changes (compare with `StringComparison.Ordinal`); keep that if you add another consumer.
