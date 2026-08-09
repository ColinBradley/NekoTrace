# Time zones

Spans arrive as Unix nanoseconds and are stored as UTC `DateTimeOffset`s. Nothing in the UI may render one directly: NekoTrace usually runs in a container whose clock is UTC and whose zone data says nothing about where the person reading the traces is, so anything derived from the host is wrong for most viewers. `BrowserTimeZone` (scoped, `Services/BrowserTimeZone.cs`) holds the browser's zone and does the conversion.

| Need | Call |
| --- | --- |
| A time-of-day cell in a grid | `FormatTimeOfDay` — invariant, 24 hour, milliseconds, fixed width |
| Any other timestamp for display | `ToBrowserTime` |
| The value of an `<input type="datetime-local">` | `ParseInputToLocal` |

`ParseInputToLocal` exists because those inputs carry no offset at all. Parsing one straight into a `DateTimeOffset` stamps it with the server's, which silently shifts the filter by the viewer's UTC offset.

## How the zone gets to the server

Blazor Server renders on the server, so the zone has to come from the browser. It arrives by three routes, in the order it becomes available:

1. **A cookie**, written by `scripts/timeZone.ts` from `Intl.DateTimeFormat().resolvedOptions().timeZone`. Read in `BrowserTimeZone`'s constructor via `IHttpContextAccessor`. This is the only route that works during **prerendering**, and it is why timestamps are already correct in the first byte of HTML.
2. **Persisted component state.** A circuit has no `HttpContext`, so it cannot read that cookie. `MainLayout` carries the prerendered value across with `[PersistentState]`; without it, times render correctly and then flip to UTC the moment the circuit takes over.
3. **JS interop**, in `MainLayout.OnAfterRenderAsync`. Authoritative, but only ever has something to correct on a browser's first visit — importing the module is also what writes the cookie for next time. When it does change the zone, `BrowserTimeZone.Changed` fires and the components showing timestamps re-render.

`TimeZoneInfo.FindSystemTimeZoneById` takes the IANA ids `Intl` produces on any host OS, converting to the platform's own format when the id isn't found natively, so no mapping table is needed on Windows.

## Not covered by this

- **ApexCharts** on the metrics page labels its own datetime axis. Points go over as UTC instants and `XAxisLabels.DatetimeUTC` is `false`, which lets the browser localise them; `BrowserTimeZone` isn't involved.
- **The flame graph** works in milliseconds relative to the trace start, so it has no absolute times to place.
- **`TraceFilter`'s `StartTime`/`EndTime`** when parsed from configuration (`TraceIngestFilter`, `TraceSaveFilter`) rather than the UI. Those are server-side config read at startup and stay server-relative; give them an explicit offset if it matters.
- **Number and date *formatting*.** The app has no request localization, so `CultureInfo.CurrentCulture` is whatever the host default is. Adding `UseRequestLocalization` would need the current-culture `double.TryParse` calls behind the numeric filter inputs switched to `InvariantCulture` first — browsers always send those values invariant, whatever locale they display in.
