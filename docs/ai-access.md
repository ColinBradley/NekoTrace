# AI access — HTTP API, MCP and CLI

Status: design. None of this is built yet.

How an agent gets a trace out of NekoTrace without drowning in it. One analysis engine, three front doors: an HTTP API, an MCP server mounted in the same process, and a thin CLI that calls the API.

## The problem, measured

Taken from `TestTraces/`:

| Trace | Spans | JSON | Distinct span names | Nodes after merging siblings by name-path |
| --- | --- | --- | --- | --- |
| `GET _` | 172 | 136 KB | 15 | 20 |
| Plan A macro | 19,379 | 18.8 MB | 33 | 74 |
| `Command.Invoke` | 230,313 | 217.6 MB | 159 | 988 |

The last column is why aggregation is the default rather than an option: a 217 MB trace has under a thousand structurally distinct things in it.

Where the bytes go in the 18.8 MB file:

- **37.2%** is attribute keys whose value is identical on *every* span. `ConvertSpan` concatenates resource and scope attributes onto each span, so `host.name`, `service.version` and `telemetry.sdk.*` are stored 19,379 times.
- **~14%** is timestamps — six encodings of two numbers per span (`StartTime`, `StartTimeMs`, `EndTime`, `EndTimeMs`, and the computed `Duration` and `DurationText`).
- **~6%** is ids, including the 32 character `TraceId` on every span.

Two structural facts that break naive implementations: the 230k span trace is a **forest, not a tree** — 4,043 spans name a parent that was never received — and the workloads are async, so children overlap and self time must be duration minus the *union* of child intervals. Subtracting the sum gives negative self time across most of these traces.

## Endpoints

Routes sit under `api/traces`, matching the existing `api/trace-files` controller.

| Endpoint | Returns | Grows with |
| --- | --- | --- |
| `GET /api/traces` | Trace list — id, root span name, start, duration, span count, error count. | traces |
| `GET /api/traces/{id}/summary` | Fixed-size orientation report. See below. | nothing |
| `GET /api/traces/{id}/profile` | Aggregated call tree, merged by name-path. `count`, `total`, `self`, `p50`, `p95`, `max`, `errors` per node. | distinct call paths |
| `GET /api/traces/{id}/tree` | Literal chronological span tree, repeated siblings collapsed. | distinct siblings |
| `GET /api/traces/{id}/spans` | Flat span list matching a span predicate. | matches |
| `GET /api/traces/{id}/spans/{spanId}` | One span in full — every attribute, event and link, plus its ancestor chain and immediate children. | nothing |
| `GET /api/spans` | Cross-trace search by name, attribute or duration, plus per-name duration statistics from `SpanRepositoriesByName`. | `limit` |

`GET /api/traces` parses **its own query string** through `TraceFilter.Parse`. There is no wrapper parameter and no nesting: `?HasError=true&SpansMinimum=100` is the filter, so a URL copied from the UI's address bar works unchanged. That makes the API the fourth consumer of the one filter language described in [filtering.md](filtering.md) — adding a dimension there must still touch all the places that file lists.

## The summary

The first call an agent should make. Fixed budget regardless of trace size, carrying only what is unusual — no routine spans, no attribute dumps.

- **Identity** — root span name, start, duration, span count, distinct span names, services, and the number of forest tops. 4,043 orphans is itself a finding and gets stated, not hidden.
- **Errors** — see below.
- **Where the time went** — top N by self time aggregated by span name, each with count, total, self and percentage of trace.
- **Outliers** — for each span name with enough samples, p50/p95/p99/max, flagging names whose max badly exceeds their p50, with the span ids of the worst instances so they can be fetched directly.
- **Shape** — max depth, widest fan-out, and detected recursion. The 230k trace repeats `ExecFlowTask → PerformExecute → TryExecuteMacroCore → Execute` down 25 levels; saying so once beats an agent inferring it from 988 paths.
- **Dead time** — stretches inside the root span where nothing was running. Cheap to compute and often the actual answer.
- **Common attributes** — the trace-constant block hoisted once, so every other endpoint can omit it and the agent still knows it.

### Errors

Errors are grouped into **classes** by span name, `error.type` and `http.response.status_code`, each with a count. Four thousand identical 404s become one line, which matters because ASP.NET reports plenty of ordinary 4xx responses as errors.

Up to `errorLimit` (default 10, caller settable) error spans are then rendered **in full** — every attribute and event, since that is where exception type, message, stack and code location live. The budget is spent round-robin across classes, so ten slots show ten different problems rather than ten copies of one.

Every error line carries its span id. `errorAttributeFilter` excludes noise classes using the `SpanAttributeFilter` syntax already in `TraceFilter`.

## Collapsing repeated siblings

The `tree` endpoint keeps chronological order but merges siblings under one parent that share a name, once the group reaches `collapse` members (default 3; `collapse=0` disables). The group is positioned at its earliest member's start time, so ordering still reads correctly.

Collapsing is a space optimisation and **must never be mistakable for the underlying data**:

- in text the node is prefixed `×2394`;
- in JSON it is a distinct node kind (`"kind": "group"`), not a span object with an extra field;
- the group carries `count`, `total`, `self`, `p50`, `p95`, `max`, `errors`, the span id of the slowest member, and `shapes` — how many distinct child subtree shapes were merged, so divergence inside the group is never silently lost.

A large group is frequently the finding rather than noise, so it reads as a signal, not an ellipsis.

`expand=Name|spanId|…` lists the members of the named groups individually.

## Hiding subtrees

`HiddenSpanNames` and `HiddenSpanIds` — pipe separated, dropping each matching span *and all its descendants*. Deliberately the same parameter names, syntax and semantics as `arrangeSpans` in `scripts/traceView.ts`, so a URL from the trace viewer transfers to the API unchanged.

What was removed is reported (`hidden: 3 names, 8,421 spans, 12.4s`) rather than silently shrinking the output.

## Compaction rules

These apply to every endpoint, and are lossless.

**Attributes.** Per response, compute which keys hold a single distinct value across the whole result set, hoist them into one `common` block printed once, and emit only the varying keys per span. This is the 37% gone, and it adapts on its own — `service.name` is hoisted in a single-service trace and stays inline in a multi-service one. `attributes=http.*,db.*` selects explicitly; `attributes=none` drops them.

**Times.** Relative to trace start by default (`+1.204s`), with unit-suffixed durations. Absolute timestamps only at `detail=full` or with `absolute=true`. There is no browser here, so times are **UTC** unless `timeZone` names one — nothing may read the host clock's zone, per [time-zones.md](time-zones.md).

**Ids.** Span ids are shortened to the shortest prefix that is unique within the response, git-style, and any unambiguous prefix is accepted back as input. Full ids at `detail=full`.

**Budget.** Every endpoint takes `limit` and `maxBytes`. Truncation is always reported along with the parameter that widens it — never a silent cut.

## Formats and detail

`format=text` (default), `json` (nested `children`, for local `jq`), `flat` (one line per span), `folded` (`a;b;c 1234`, Brendan Gregg's collapsed-stack format — near-zero overhead and it feeds existing flamegraph tooling).

`detail=minimal` (name and timings), `standard` (default — plus short id, kind, status and varying attributes), `full` (everything, events included).

Indented text is the default because nested JSON costs roughly two to three times the tokens for the same structure. JSON is there for when the agent wants to post-process locally.

## MCP

Served in-process on the web host at `/mcp` via `ModelContextProtocol.AspNetCore`'s `MapMcp()`. Nothing extra to run — NekoTrace is already a server — and client configuration is one URL.

Six tools mirroring the endpoints one to one: `list_traces`, `get_trace_summary`, `get_trace_profile`, `get_trace_tree`, `get_span`, `search_spans`. Distinct tool names rather than one tool with a mode parameter, because models choose between names far more reliably than between enum values.

Tools return the compact text formats with a one-line legend, and close with a hint naming the exact follow-up call. That is what makes an agent drill rather than dump.

## CLI

A separate `NekoTrace.Cli` project, and a **thin HTTP client** — no embedded analysis engine. NekoTrace is local and cheap to run, so repeated calls to it beat downloading a trace and analysing it client-side.

`--file foo.json.gz` works by POSTing to the existing `api/trace-files` upload and then querying normally, which gives saved traces the full feature set for no duplicated code and no `NekoTrace.Core` extraction.

Adding the project means a new publish profile in `Publish.ps1` and a new artifact in the release workflow — see [build-and-release.md](build-and-release.md).

## Code layout

`NekoTrace.Web/Analysis/` holds pure functions over `TraceItem` and `SpanData`:

| File | Responsibility |
| --- | --- |
| `SpanTree.cs` | Forest construction, orphan attachment, chronological ordering, subtree hiding |
| `SelfTime.cs` | Duration minus the union of child intervals |
| `TraceProfile.cs` | Merge by name-path |
| `TraceSummary.cs` | The summary report |
| `AttributeSummary.cs` | Constant/varying split |
| `SpanQuery.cs` | Span predicate parsed from a query string, mirroring `TraceFilter`'s shape |
| `Formatters/` | Text, Json, Flat, Folded |

Then `Controllers/TraceAnalysisController.cs` and `Mcp/TraceTools.cs` as thin wrappers.

All of it is directly constructible with no ASP.NET or Blazor dependency, so it tests the way [testing.md](testing.md) prescribes — `TestData/Fake.cs` grows the fixtures. The UI can later grow a Profile tab over `TraceProfile` for free.

## Unrelated win found on the way

`SpanData.Duration` and `DurationText` are computed properties that System.Text.Json serialises into every saved trace. Marking them `[JsonIgnore]` shrinks trace files by roughly 8% and needs no `CURRENT_VERSION` bump, since read-only properties are ignored when deserialising anyway.

## Deliberately not doing

A TraceQL-style query language. The span predicate stays flag shaped (`name`, `minDuration`, `maxDuration`, `status`, `kind`, `attr`) — a second query language to learn is a cost paid by every caller for a case that filtering already covers.
