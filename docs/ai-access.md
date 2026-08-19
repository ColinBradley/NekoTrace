# AI access — HTTP API, MCP and CLI

How an agent gets a trace out of NekoTrace without drowning in it. One analysis engine, three front doors: an HTTP API, an MCP server mounted in the same process, and a thin CLI that calls the API.

All three are built: the engine in `Analysis/`, the HTTP API in `Controllers/TraceAnalysisController.cs`, the MCP server in `Mcp/TraceTools.cs` and the CLI in `NekoTrace.Cli/`.

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
| `GET /api/traces/{id}/spans` | Flat span list matching a span predicate, each match with its attributes. | matches |
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

**Attributes.** Per response, compute which keys are carried by *every* span in it with the same value throughout, hoist them into one `common` block printed once, and emit only the varying keys per span. On a span search the response is the set of matches, which makes the hoisted block do double duty: it is compaction, and it is also the answer to "what do these all have in common". A search over 1,975 spans that hoists `url.path` has proved every one of them hit the same URL, over the whole set rather than a sample, in one line — so the block goes *above* the matches in every rendering, not after them.

**`limit` bounds the printing and nothing else.** The match count and the hoisted block both describe every match, not the page. Hoisting over the page instead would make the block's meaning depend on a rendering knob — ask for 50 and it says one thing, ask for 100 and it says another about the same query — and it would leave a caller wanting to know whether 1,975 spans all hit one URL able to learn it only of the 50 they paid to print, with generalising from a sample as the only route to the question they actually asked. So `limit=1` answers "how many are there, and what do they all share" over the whole set in about four lines: on the 230,313 span trace in `TestTraces/` that is 456 bytes, against 4.1 kB for a 30 row page that establishes it of thirty. The cost is a predicate call per non-matching span and an attribute pass over every match — 432ms end to end in the worst case where every span in that trace matches, on a repository the tree and profile views already walk in full. This is the 37% gone, and it adapts on its own — `service.name` is hoisted in a single-service trace and stays inline in a multi-service one. The rule is "every span has it and they all agree", not "one distinct value among the spans that mention it": hoisting a key half the set carries would assert it of the other half.

Hoisting alone is not enough, because it only removes keys that never vary. `otel.library.name` and `otel.library.version` take four distinct values across the 19,379 span trace, so neither hoists, and together they were about a third of every line of a rendered tree. They are therefore **excluded by default**, along with `telemetry.sdk.*`. That is a rendering choice rather than compaction, so unlike hoisting it is stated in the output footer and reversed.

`attributeKeys` takes comma-separated key prefixes — `http.,db.` — and a bare `*` for everything. `*` rather than the word `all` because every other value is matched against attribute keys, and nothing stops a key from starting "all"; a magic word in the same namespace as the data is a collision waiting to happen. There is no spelling for "none" either: switching attributes off is `includeAttributes=false`, which is a question about how much of a span to render rather than about which keys are interesting.

**`attributeKeys` decides what is printed; a *filter* decides what comes back.** The two were both spelled `attributeFilter` at first, on adjacent tools, with different syntaxes — `get_trace_tree` took key prefixes and `search_spans` took `key=value` pairs — which is a trap for anything reading the schemas rather than the source. So every predicate now carries a qualifier (`attributeFilter` on the span search, `spanAttributeFilter` on the trace list, `errorAttributeFilter` on the summary) and the bare `attributeKeys` is always the render-time selector. The rule is: keys are printed, filters are applied.

**Times.** Relative to trace start in the tree (`+1.204s`), with unit-suffixed durations; absolute ISO 8601 timestamps on the summary and on a single span.

Absolute times are **always UTC**, and there is no parameter to ask for anything else. Nothing here has a browser to ask, so a zone could only come from the host — meaningless in a container — or from a caller who already knows the zone and can therefore convert perfectly well without help. Zone rules also change, and a server carrying stale tzdata is worse than one that never claims to know. The browser-zone rule in [localization.md](localization.md) is about rendering to a person and does not reach here. Timestamps are printed in the form `startedAfter` and `startedBefore` accept, so output feeds back into a filter unchanged.

**Ids.** Span ids are shortened to the shortest prefix that is unique within the response, git-style, and printed in square brackets. Any unambiguous prefix is accepted back wherever a span id is taken, so a short id read out of the tree can be handed straight to `get_span` — that reciprocity is the whole point, and it needs saying in the tool descriptions or a reader has no idea why the ids look truncated. `shortenSpanIds=false` prints them whole. A prefix matching several spans is answered with a 400 listing the candidates, not a 404: the caller is close, and the body should say how close.

**Budget.** Endpoints take `limit`, `depth` and `collapse`. Every truncation is reported along with the parameter that widens it — never a silent cut.

## Formats and detail

`format=text` (default), `format=flat` or `format=json`. Indented text is the default because nested JSON costs roughly two to three times the tokens for the same structure; JSON is there for when the caller wants to post-process locally. A `format` the API does not know is a 400 rather than a quiet fall through to text — a caller who mistyped `flat` and silently got the indented tree would find out from whatever consumed it, a step further from the cause.

`includeAttributes` (default true), `includeEvents` (default false — most spans have none and a stack trace is long) and `shortenSpanIds` (default true). Name, timings, id and error status are always printed; nothing useful comes of hiding those.

`folded` (`a;b;c 1234`, Brendan Gregg's collapsed-stack format) is no longer worth its own format: the flat profile carries the joined path in its last column and the total in its first, so `awk -F'\t' '{print $10, $1}'` is folded output weighted by total time, and the other columns are there for the questions a flamegraph cannot answer.

### flat

One line per span, tab separated, the same field in the same place on every line. This is the format the CLI exists for. `text` carries the tree's structure in its indentation, which is exactly what a `grep` destroys: a matched line arrives with no way to tell what it hung off. `flat` puts the structure in columns instead — `depth` and `parent` say what the leading spaces did, and survive being filtered, sorted and counted.

Four rules make it worth having over "text without the indentation":

- **A fixed field count.** Ten on a tree line: `offsetMs durationMs selfMs depth id parent kind status name attributes`. The variable length part is last and joined into one field, so `cut -f5` means one thing everywhere.
- **Bare invariant numbers in one unit, named by the column.** `Units.Duration`'s per-value unit reads better and sorts wrongly — `340ms` sorts above `1.2s` under every numeric sort there is — and this format exists to be sorted.
- **Everything that is not a span is a comment line starting `#`.** `grep -v '^#'` leaves exactly the data, so `wc -l` counts spans, and none of the notes the tree prints — what was hidden, what was past the depth limit, which attributes were left out — has to be dropped to keep that true.
- **No merging, whatever `collapseThreshold` said.** A `×N` group is a summary and one line per span is the promise; `TraceViews` builds the tree it hands the flat renderer with collapsing off. Nothing else about the request changes, so `HiddenSpanNames`, `maxSpanDepth` and `startAtSpanId` still apply.

Two things that would otherwise need a column of their own go in the trailing attributes field, and are announced in the footer when they appear: a failed span's status message as `status.message=…`, and span events as `event.<eventName>.<attributeKey>=…`. The event name is repeated even when it doubles up into `event.exception.exception.type`, because the traces in `TestTraces/` carry an event named `exception` holding both `message` and `exception.message`, and folding the name in would render two different values under one key.

An orphan's `parent` field reads `orphan:<id>`, with the id left whole — shortening is only unique among the spans that are here, and this one names a span that is not. Without the marker a partially collected trace reads as one that genuinely has 4,043 tops, and the count is stated in the footer.

`flat` is served where the answer is a list: the trace list, the tree, the profile, the span list and the cross-trace search. The profile earns it for the same reason the tree does — its structure is in its indentation, so a `grep` of the text form yields a node with no way to tell what it hung off; the flat form spells the path out in a column. The summary and a single span answer a `format=flat` with a 400 naming the formats they do have: the summary is a fixed size report with no single row type, and a single span is the whole of one span, where flat would truncate away the attributes and events it exists to show. Answering in some near-enough shape instead would leave a caller piping something that only looks like the format it asked for.

## MCP

Served in-process on the web host at `/mcp` via `ModelContextProtocol.AspNetCore`'s `MapMcp()`. Nothing extra to run — NekoTrace is already a server — and client configuration is one URL.

Six tools mirroring the endpoints one to one: `list_traces`, `get_trace_summary`, `get_trace_profile`, `get_trace_tree`, `get_span`, `search_spans`. Distinct tool names rather than one tool with a mode parameter, because models choose between names far more reliably than between enum values.

Tools return the compact text formats with a one-line legend, and close with a hint naming the exact follow-up call. That is what makes an agent drill rather than dump.

**Every filter dimension is its own described parameter**, which is where the MCP surface deliberately diverges from the HTTP one. Taking a `TraceFilter` as a raw query string is right over HTTP — a URL from the address bar pastes straight in — but a model handed one opaque string has nothing in the schema telling it what may go in there, and has to encode the whole filter itself. So `list_traces` spells out `hasError`, `minSpans`, `minDurationSeconds`, `maxDurationSeconds`, `startedAfter`, `startedBefore`, `rootSpanNames`, `excludeRootSpanNames` and `spanAttributeFilter`. The cost is that it has to keep pace with `TraceFilter`; [filtering.md](filtering.md) lists it among the places a new dimension must touch.

`startedAfter` and `startedBefore` are named for what `TraceFilter.StartTime` and `EndTime` actually do — both bound the trace's *start*, and `EndTime` never looks at when the trace ended. The old names invite exactly the wrong reading.

**No detail level.** `minimal|standard|full` bundled unrelated decisions — whether ids are shortened has nothing to do with whether events are shown — so a caller wanting one had to take the others, and no single word said what they would get. It is three booleans instead: `includeAttributes`, `includeEvents`, `shortenSpanIds`.

## CLI

A separate `NekoTrace.Cli` project, and a **thin HTTP client** — no embedded analysis engine. NekoTrace is local and cheap to run, so repeated calls to it beat downloading a trace and analysing it client-side. The binary is `NekoTrace.Cli`; `System.CommandLine` parses it, which is what buys the `--help` below and the completion and value validation with it.

Six subcommands, one per endpoint: `traces`, `summary`, `profile`, `tree`, `span`, `search`. Options are the MCP parameter names in kebab case, carrying the same descriptions, and each maps to one query parameter — including the two the trace viewer spells in PascalCase, `HiddenSpanNames` and `HiddenSpanIds`, so a URL copied out of the UI still transfers. `--server` (or `NEKOTRACE_URL`), `--format` and `--file` are recursive, so they work in front of any subcommand.

**`--help` is the interface.** What reads it is as likely to be an agent as a person, and neither has anything else to go on, so the descriptions say what a thing is for and what to reach for next rather than restating the flag name. That is why the CLI does not append MCP's "Next:" hint lines to its output: standard output belongs to the answer and is going to be piped somewhere, and the guidance belongs where it can be read before the call rather than after it.

Two things are checked in the CLI rather than passed through, because the filter parsers drop what they cannot read instead of erroring — which turns a typo into a wider query with plausible looking results. `--kind` is matched against the five span kinds, and the timestamps are parsed and normalised to `…Z` before sending, which also pins down what a time with no offset on it means.

`--file foo.json.gz` works by POSTing to the existing `api/trace-files` upload and then querying normally, which gives saved traces the full feature set for no duplicated code and no `NekoTrace.Core` extraction. For that to be one command rather than two, the upload has to say which trace it ingested: it answers with the ids when the request carries `Accept: application/json`, and keeps its 204 otherwise. The 204 is not vestigial — the Home page posts that form straight from the browser, so the response is a navigation, and any body at all would take the reader off the app. No browser asks for JSON on a form post.

Exit codes are 0, 1 for a request the server refused or a command line that would not parse, and 2 for nothing answering at all — which is nearly always a NekoTrace that is not running, so it gets its own code and says so.

The CLI ships *inside* the server's artifact rather than as its own download — see [build-and-release.md](build-and-release.md). That is also what lets the MCP server hand out its absolute path. `McpInstructions` puts it in the server instructions, which a client shows the model once at connection: an MCP caller therefore learns the CLI exists *before* doing the work another way, which is the only time it is any use. The path is checked for rather than assumed, so a container without it advertises nothing rather than a path that is not there. `CliLocation` takes a second look in Debug builds only: debugging the server on its own runs it out of `NekoTrace.Web/bin/{configuration}/{framework}`, which no publish puts the CLI into, so it swaps its own project directory for the CLI project's in that path and takes whatever the last CLI build left there. That keeps the CLI advertised while the instructions are being worked on, reads the configuration and framework off the path in hand rather than naming them, and still builds nothing on the CLI's behalf — an unbuilt CLI is simply not advertised.

That is the one place the CLI is advertised. Not a tool description, which is per-tool and repeated, and not appended to tool output, which would spend tokens on every call to say something that only needs saying once.

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
