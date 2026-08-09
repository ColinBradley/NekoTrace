# Localization

One rule, two mechanisms: what the UI renders is shaped by the viewer's browser, never by the host. NekoTrace usually runs in a container whose clock is UTC and whose locale is invariant, and which in any case knows nothing about whoever is reading the traces.

The two are independent. The time zone is carried by a service NekoTrace owns; the culture is carried by `CultureInfo.CurrentCulture`, which the framework sets per request.

## Time zones

Spans arrive as Unix nanoseconds and are stored as UTC `DateTimeOffset`s. Nothing in the UI may render one directly. `BrowserTimeZone` (scoped, `Services/BrowserTimeZone.cs`) holds the browser's zone and does the conversion.

| Need | Call |
| --- | --- |
| A time-of-day cell in a grid | `FormatTimeOfDay` — invariant, 24 hour, milliseconds, fixed width |
| Any other timestamp for display | `ToBrowserTime` |
| The value of an `<input type="datetime-local">` | `ParseInputToLocal` |

`ParseInputToLocal` exists because those inputs carry no offset at all. Parsing one straight into a `DateTimeOffset` stamps it with the server's, which silently shifts the filter by the viewer's UTC offset.

### How the zone gets to the server

Blazor Server renders on the server, so the zone has to come from the browser. It arrives by three routes, in the order it becomes available:

1. **A cookie**, written by `scripts/timeZone.ts` from `Intl.DateTimeFormat().resolvedOptions().timeZone`. Read in `BrowserTimeZone`'s constructor via `IHttpContextAccessor`. This is the only route that works during **prerendering**, and it is why timestamps are already correct in the first byte of HTML.
2. **Persisted component state.** A circuit has no `HttpContext`, so it cannot read that cookie. `MainLayout` carries the prerendered value across with `[PersistentState]`; without it, times render correctly and then flip to UTC the moment the circuit takes over.
3. **JS interop**, in `MainLayout.OnAfterRenderAsync`. Authoritative, but only ever has something to correct on a browser's first visit — importing the module is also what writes the cookie for next time. When it does change the zone, `BrowserTimeZone.Changed` fires and the components showing timestamps re-render.

`TimeZoneInfo.FindSystemTimeZoneById` takes the IANA ids `Intl` produces on any host OS, converting to the platform's own format when the id isn't found natively, so no mapping table is needed on Windows.

## Cultures

`Program.cs` puts `UseRequestLocalization` in front of the **web** app only, offering every specific culture and letting the `Accept-Language` header choose. None of this is translation — there are no resource strings, so there is no list of languages to curate and nothing to be missing. The collector app has no localization: it parses machine formats, where a culture could only do harm.

The culture is captured when the circuit is created and holds for its lifetime, so interactive renders format the same way the prerender did. Verify a change to this by giving the host a distinctive default (`CultureInfo.DefaultThreadCurrentCulture`) and watching whether a circuit-driven re-render drifts towards it.

**Values from form controls are invariant and must be parsed as such.** An `<input type="number">` showing a German user `1,5` still reports `1.5`. Go through `Utilities/InputValues.cs` rather than calling `double.TryParse` directly: with `CurrentCulture` now set from `Accept-Language`, a bare parse reads that `1.5` as fifteen hundred under any culture that groups with a dot. It fails silently, which is what makes it worth a helper. The same reasoning covers `BrowserTimeZone.ParseInputToLocal`.

Writing the other half of that round trip is already safe: `GetUriWithQueryParameter` formats invariantly, and `[SupplyParameterFromQuery]` reads back invariantly. `InputValuesTests` pins that down, since the pages depend on it.

`App.razor` sets `<html lang>` from the negotiated culture so assistive technology announces numbers and dates the way they are formatted. It deliberately does not set `dir`: nothing here is laid out for right-to-left, and flipping it would break more than it fixed.

**Watch for date formats without a provider, even ones with no separators in them.** `yy` renders in the culture's *calendar*, so `{trace.Start:yyMMddTHHmmss}` — which looks culture-proof — gave a Thai viewer a Buddhist year and a Saudi one a Hijri date. Anything that names a file, keys a dictionary or crosses a wire wants `CultureInfo.InvariantCulture` spelled out. Note that the analyzers can't help here: CA1305 doesn't run on `.razor` files, which are generated code.

## Not covered by this

- **ApexCharts** on the metrics page labels its own datetime axis. Points go over as UTC instants and `XAxisLabels.DatetimeUTC` is `false`, which lets the browser place them in the right zone; `BrowserTimeZone` isn't involved. The label *text* is formatted in JavaScript against ApexCharts' bundled `en` locale, so a range wide enough to show month names shows them in English whatever the request culture. Fixable with its `Locales`/`DefaultLocale` options if it ever grates.
- **The flame graph** works in milliseconds relative to the trace start, so it has no absolute times to place, and it draws its own labels in TypeScript.
- **Span attribute values**, which `TraceItem.TryGetRootSpanAttribute` renders invariantly. Those are data an exporter chose, not measurements NekoTrace took, so they stay as they arrived.
- **`TraceFilter.Parse`**, fed by configuration (`TraceIngestFilter`, `TraceSaveFilter`) rather than the UI. Read at startup, outside any request, so no request culture applies. Its numbers are already invariant; its `StartTime`/`EndTime` stay server-relative, so give them an explicit offset if it matters.
- **Trace file names**, both `TraceDiskWriter`'s and the `Content-Disposition` on a download, which are stamped with `CultureInfo.InvariantCulture` so that the same trace is named the same thing however it left the app.
- **UI text**, which is English only. There are no resource strings and no `IStringLocalizer`; `AddSupportedUICultures` is set for consistency but has nothing to resolve.
