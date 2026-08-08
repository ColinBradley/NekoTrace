# Trace viewer

The flame graph is a canvas rendered entirely in TypeScript. `UI/Components/TraceViewComponent` draws none of it.

## The .NET side

`TraceViewComponent.razor.cs` imports `/js/traceView.js` on first render, then on each render hands the module a `SpanDataSlim` projection of the trace's spans — deliberately trimmed, because parsing full spans as JSON is the bottleneck for large traces.

Re-initialisation is guarded by reference-comparing the span list against the last one sent (`object.ReferenceEquals(mClientSpans, trace.Spans)`), so a trace that gains spans must publish a *new* immutable list; mutating in place would leave the canvas stale.

Selection flows back the other way: a `DotNetObjectReference` plus the `[JSInvokable] SetSelectedSpanId` method, which writes the id into the query string with `replace: true`.

## The TypeScript side

`scripts/traceView.ts` owns a `TraceRenderer` per canvas (stashed on the element as `traceRenderer`) handling layout into rows, zoom/pan, hit-testing, hover, and colouring. Colour is keyed off a span attribute named by the `data-span-color-selector` attribute on the canvas, defaulting to `otel.library.name`.

Notable behaviours:

- it patches `history.pushState`/`replaceState` to emit a `locationchange` event, because Blazor's client-side navigation is otherwise unobservable — that is how the renderer picks up query-string changes such as the selected span or hidden span names;
- sizes are computed from `devicePixelRatio`, cached because reading it is slow;
- `ResizeObserver` and `MutationObserver` drive resize, attribute changes, and teardown when the element leaves the DOM.

Interaction (documented for users in the README): drag to pan, wheel to zoom, `Alt`+wheel to scroll vertically, `Alt`+`Shift`+wheel horizontally, double click to reset.

`scripts/types.ts` mirrors the C# span DTO and the OTel `SpanKind`/`StatusCode` enums — change both sides together.

## Build

`Microsoft.TypeScript.MSBuild` compiles `scripts/**` to `wwwroot/js/` during `dotnet build`, per `tsconfig.json` (strict, ESNext modules, source maps). The output directory is git-ignored; never edit or commit the generated JS. On non-Windows this needs Node.js on the PATH — that is why the Dockerfile installs it in the build stage.
