## Commands

Requires the .NET 10 SDK. Node.js is needed on non-Windows to run the TypeScript compiler that MSBuild invokes.

```powershell
dotnet build NekoTrace.slnx
```

```powershell
dotnet run --project NekoTrace.Web/NekoTrace.Web.csproj
```

Running serves the UI on <http://localhost:8347> and listens for OTLP on 4317 (gRPC) / 4318 (HTTP).

```powershell
dotnet test NekoTrace.slnx
```

Verification is `dotnet build` (warnings are meaningful, see below) plus `dotnet test`. Neither covers the Blazor UI or the gRPC services, so a change to either still wants the app run and telemetry pushed at it.

`TestTraces/` is gitignored, so it is often absent — but when sample traces exist locally, that is where they are: downloaded `.json.gz` trace files, useful for exercising the upload path (`POST /api/trace-files`) against real data rather than synthesised spans. Files saved by older builds can carry base64 ids instead of hex, which makes them worth keeping around.

TypeScript under `NekoTrace.Web/scripts/` is compiled as part of `dotnet build`; never edit the generated JS in `wwwroot/js/`.

## Architecture

Two projects: `NekoTrace.Web`, and `NekoTrace.Tests` covering it. The app itself is an in-memory OpenTelemetry collector plus a Blazor Server UI for browsing what it collected. No database, no external dependencies.

**Two web hosts, one process.** `Program.cs` builds *two* independent `WebApplication`s on separate `Task`s:

- the **collector app** — gRPC services (`GrpcServices/`) on port 4317 and the OTLP/HTTP endpoints of `Endpoints/OtlpHttpEndpoints.cs` (`/v1/traces`, `/v1/metrics`) on 4318, accepting both protobuf and JSON bodies;
- the **web app** — Blazor Server UI + `Controllers/` on port 8347.

`TracesRepository` and `MetricsRepository` are shared between both.

Logs and profiles gRPC services exist so exporters don't error, but they discard everything. Only traces and metrics are stored (for now).

Two rules cut across everything:

- **Repositories mutate under a lock and publish immutable snapshots.** Readers never lock. Produce a new immutable collection rather than mutating in place.
- **UI state lives in the URL.** Pages use `[SupplyParameterFromQuery]` and navigate with `replace: true`, so views stay shareable. Keep new view options query-parameter-driven.

## Conventions

`.editorconfig` is large and enforced (`EnforceCodeStyleInBuild`, `AnalysisMode=All`), so style deviations surface as build warnings. The non-obvious ones:

- private/internal fields are `mCamelCase`, statics are `sCamelCase`, consts are `ALL_UPPER`;
- `this.` is required for properties, methods and events, but *not* for fields (hence `mFoo` bare, `this.Bar` qualified);
- file-scoped namespaces, `using` directives *inside* the namespace, System directives not sorted first;
- open brace on a new line; 4 spaces.

When an analyzer rule genuinely doesn't apply, suppress it narrowly with `#pragma warning disable CAxxxx` plus the rule name in the comment, matching existing usage — don't relax the project-wide settings.

Blazor components use the `.razor` + `.razor.cs` partial-class split with scoped `.razor.css`; put logic in the code-behind.

## Details

Read these only when working in the area they cover.

| File | Read it when |
| --- | --- |
| [docs/data-model.md](docs/data-model.md) | Touching how traces/spans/metrics are stored, indexed, locked, trimmed or written to disk. |
| [docs/filtering.md](docs/filtering.md) | Adding or changing a filter dimension, or anything using `TraceFilter`. |
| [docs/configuration.md](docs/configuration.md) | Adding a config option, or changing how config is read. |
| [docs/trace-viewer.md](docs/trace-viewer.md) | Working on the flame graph canvas, `scripts/*.ts`, or the .NET↔JS interop. |
| [docs/build-and-release.md](docs/build-and-release.md) | Cutting a release, changing publish/Docker/CI, or touching `Protos/`. |
| [docs/testing.md](docs/testing.md) | Adding or changing tests, or wondering why something isn't covered. |
