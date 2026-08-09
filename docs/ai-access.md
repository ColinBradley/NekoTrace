# AI access — HTTP API, MCP and CLI

How an agent gets a trace out of NekoTrace without drowning in it. One analysis engine, three front doors: an HTTP API, an MCP server mounted in the same process, and a thin CLI that calls the API.

The engine (`Analysis/`), the HTTP API (`Controllers/TraceAnalysisController.cs`) and the MCP server (`Mcp/TraceTools.cs`) are built. The CLI is not.

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
- **Outliers** — for each span name with enough samples, p50/p95/max with the span id of the worst instance. Judged on **self time, not duration**: a span that merely contains a slow one is not itself slow, and measured on duration the outermost instance of a recursive name always looks like an enormous outlier. Against the 19,379 span trace that filled the entire list with the four names forming its recursion, each claiming a thousandfold tail.
- **Shape** — max depth, widest fan-out, and detected recursion. The 230k trace repeats `ExecFlowTask → PerformExecute → TryExecuteMacroCore → Execute` down 25 levels; saying so once beats an agent inferring it from 988 paths.
- **Dead time** — stretches inside the root span where nothing was running. Cheap to compute and often the actual answer.
- **Common attributes** — the trace-constant block hoisted once, so every other endpoint can omit it and the agent still knows it.

### Errors

Errors are grouped into **classes** by span name, `error.type` and `http.response.status_code`, each with a count. Four thousand identical 404s become one line, which matters because ASP.NET reports plenty of ordinary 4xx responses as errors.

Up to `errorLimit` (default 10, caller settable) error spans are then rendered **in full** — every attribute and event, since that is where exception type, message, stack and code location live. The budget is spent round-robin across classes, so ten slots show ten different problems rather than ten copies of one.

Every error line carries its span id. `errorAttributeFilter` excludes noise classes using the `SpanAttributeFilter` syntax already in `TraceFilter`.

## Span events

Events matter more than their rarity in `TestTraces/` suggests, because the OpenTelemetry convention is to record an exception as an event named `exception` carrying `exception.type`, `exception.message` and `exception.stacktrace`. Instrumentation that follows it — most SDKs — puts nothing on the span's own attributes, so anything classifying errors by attribute alone misses every error those SDKs raise. Whatever produced the traces in `TestTraces/` writes them as span attributes instead, so both spellings are live.

The summary therefore reads exception type and message from either place, and separately counts **handled exceptions**: spans carrying an exception event but not marked as failed. Those appear in no error count at all and are regularly what explains a slow trace with nothing wrong in it.

Beyond that, events are a slice two concern: rendering them in the tree and flat views, filtering and searching on event name and event attributes, and folding event-carrying spans into the error classes rather than counting them separately.

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

**Attributes.** Per response, compute which keys are carried by *every* span in it with the same value throughout, hoist them into one `common` block printed once, and emit only the varying keys per span. This is the 37% gone, and it adapts on its own — `service.name` is hoisted in a single-service trace and stays inline in a multi-service one. The rule is "every span has it and they all agree", not "one distinct value among the spans that mention it": hoisting a key half the set carries would assert it of the other half.

Hoisting alone is not enough, because it only removes keys that never vary. `otel.library.name` and `otel.library.version` take four distinct values across the 19,379 span trace, so neither hoists, and together they were about a third of every line of a rendered tree. They are therefore **excluded by default**, along with `telemetry.sdk.*`. That is a rendering choice rather than compaction, so unlike hoisting it is stated in the output footer and reversed.

`attributeFilter` takes comma-separated key prefixes — `http.,db.` — and a bare `*` for everything. `*` rather than the word `all` because every other value is matched against attribute keys, and nothing stops a key from starting "all"; a magic word in the same namespace as the data is a collision waiting to happen. There is no spelling for "none" either: switching attributes off is `includeAttributes=false`, which is a question about how much of a span to render rather than about which keys are interesting.

**Times.** Relative to trace start in the tree (`+1.204s`), with unit-suffixed durations; absolute ISO 8601 timestamps on the summary and on a single span.

Absolute times are **always UTC**, and there is no parameter to ask for anything else. Nothing here has a browser to ask, so a zone could only come from the host — meaningless in a container — or from a caller who already knows the zone and can therefore convert perfectly well without help. Zone rules also change, and a server carrying stale tzdata is worse than one that never claims to know. The browser-zone rule in [localization.md](localization.md) is about rendering to a person and does not reach here. Timestamps are printed in the form `startedAfter` and `startedBefore` accept, so output feeds back into a filter unchanged.

**Ids.** Span ids are shortened to the shortest prefix that is unique within the response, git-style, and printed in square brackets. Any unambiguous prefix is accepted back wherever a span id is taken, so a short id read out of the tree can be handed straight to `get_span` — that reciprocity is the whole point, and it needs saying in the tool descriptions or a reader has no idea why the ids look truncated. `shortenSpanIds=false` prints them whole. A prefix matching several spans is answered with a 400 listing the candidates, not a 404: the caller is close, and the body should say how close.

**Budget.** Endpoints take `limit`, `depth` and `collapse`. Every truncation is reported along with the parameter that widens it — never a silent cut.

## Formats and detail

`format=text` (default) or `format=json`. Indented text is the default because nested JSON costs roughly two to three times the tokens for the same structure; JSON is there for when the caller wants to post-process locally.

`includeAttributes` (default true), `includeEvents` (default false — most spans have none and a stack trace is long) and `shortenSpanIds` (default true). Name, timings, id and error status are always printed; nothing useful comes of hiding those.

Still to do: `flat` (one line per span) and `folded` (`a;b;c 1234`, Brendan Gregg's collapsed-stack format — near-zero overhead and it feeds existing flamegraph tooling).

## MCP

Served in-process on the web host at `/mcp` via `ModelContextProtocol.AspNetCore`'s `MapMcp()`. Nothing extra to run — NekoTrace is already a server — and client configuration is one URL.

Six tools mirroring the endpoints one to one: `list_traces`, `get_trace_summary`, `get_trace_profile`, `get_trace_tree`, `get_span`, `search_spans`. Distinct tool names rather than one tool with a mode parameter, because models choose between names far more reliably than between enum values.

Tools return the compact text formats with a one-line legend, and close with a hint naming the exact follow-up call. That is what makes an agent drill rather than dump.

**Every filter dimension is its own described parameter**, which is where the MCP surface deliberately diverges from the HTTP one. Taking a `TraceFilter` as a raw query string is right over HTTP — a URL from the address bar pastes straight in — but a model handed one opaque string has nothing in the schema telling it what may go in there, and has to encode the whole filter itself. So `list_traces` spells out `hasError`, `minSpans`, `minDurationSeconds`, `maxDurationSeconds`, `startedAfter`, `startedBefore`, `rootSpanNames`, `excludeRootSpanNames` and `spanAttributeFilter`. The cost is that it has to keep pace with `TraceFilter`; [filtering.md](filtering.md) lists it among the places a new dimension must touch.

`startedAfter` and `startedBefore` are named for what `TraceFilter.StartTime` and `EndTime` actually do — both bound the trace's *start*, and `EndTime` never looks at when the trace ended. The old names invite exactly the wrong reading.

**No detail level.** `minimal|standard|full` bundled unrelated decisions — whether ids are shortened has nothing to do with whether events are shown — so a caller wanting one had to take the others, and no single word said what they would get. It is three booleans instead: `includeAttributes`, `includeEvents`, `shortenSpanIds`.

## CLI

A separate `NekoTrace.Cli` project, and a **thin HTTP client** — no embedded analysis engine. NekoTrace is local and cheap to run, so repeated calls to it beat downloading a trace and analysing it client-side.

`--file foo.json.gz` works by POSTing to the existing `api/trace-files` upload and then querying normally, which gives saved traces the full feature set for no duplicated code and no `NekoTrace.Core` extraction.

Adding the project means a new publish profile in `Publish.ps1` and a new artifact in the release workflow — see [build-and-release.md](build-and-release.md).

## Code layout

`NekoTrace.Web/Analysis/` holds pure functions over `TraceItem` and `SpanData`:

Three folders, split by what a type is for. One type per file throughout.

| Folder | Holds | Rule |
| --- | --- | --- |
| `Analysis/` | `SpanTree`, `TraceProfile`, `TreeView`, `TraceSummary`, `AttributeSummary`, `TraceViews` | Anything that walks a trace |
| `Analysis/Queries/` | `SpanQuery`, `AttributeMatcher`, `TraceSummaryOptions`, `TreeViewOptions` | What a caller asks for |
| `Analysis/Results/` | `SpanNode`, `ProfileNode`, `TreeNode`/`TreeSpan`/`TreeGroup`, `TreeViewResult`, `TraceView`, `NameCost`, `ErrorClass`, `DeadTime`, `DurationStatistics`, `TraceListEntry`, `SpanSearchResult` | What comes back |

`Results/` depends on nothing in `Analysis/` — only on `Repositories/Traces`. That is the invariant worth keeping, and it is why `DurationStatistics` sits there despite having a factory: `NameCost` and `ProfileNode` hold one, and putting it a level up would point the dependency back the wrong way.

`Formatting/` holds `TextFormatter`, plus units, id shortening, the attribute selector and the span render options. Then `Controllers/TraceAnalysisController.cs` and `Mcp/TraceTools.cs` are thin wrappers over `TraceViews`.

## Text and model, from one place

Each view is assembled once, by `TraceViews`, and returned as a `TraceView` carrying both the analysis result and its text rendering. That is what stops the HTTP API and the MCP server drifting into different answers to the same question.

The two are **not** parallel representations. `Model` is the result; `Text` is a function of it. So the rendering is deferred behind a `Lazy<string>` rather than done up front — `format=json` serves the model directly, and formatting a 230,000 span trace only to throw the string away is real work wasted. MCP never asks for the model; the HTTP API asks for one or the other, never both.

Everything in `Analysis/` is internal apart from `TraceViews`, which is public only because dependency injection and the MCP tool type have to reach it — its methods stay internal, since the option records they take are analysis internals and widening those would make an API out of something only built here.

None of it depends on ASP.NET or Blazor, so it tests the way [testing.md](testing.md) prescribes — `TestData/Fake.cs` grows the fixtures. The UI can later grow a Profile tab over `TraceProfile` for free.

Every walk over a tree uses an explicit stack rather than recursion. Nesting depth is a property of whatever was collected, not of anything NekoTrace controls, so a pathological trace must not be able to overflow a request thread's stack. For the same reason `SpanTree.Build` detects parent cycles — which no SDK produces but a hand-edited file can — and severs both the parent and the child link, since clearing only the parent leaves the ring intact in the child lists and every later walk circles it.

## Unrelated win found on the way

`SpanData.Duration` and `DurationText` are computed properties that System.Text.Json serialises into every saved trace. Marking them `[JsonIgnore]` shrinks trace files by roughly 8% and needs no `CURRENT_VERSION` bump, since read-only properties are ignored when deserialising anyway.

## Deliberately not doing

A TraceQL-style query language. The span predicate stays flag shaped (`name`, `minDuration`, `maxDuration`, `status`, `kind`, `attr`) — a second query language to learn is a cost paid by every caller for a case that filtering already covers.
