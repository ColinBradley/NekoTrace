# NekoTrace

[Collects](https://opentelemetry.io/docs/collector/) and displays [Open Telemetry](https://opentelemetry.io/) data.

- Delightful [tracing](https://opentelemetry.io/docs/concepts/signals/traces/) flame graph-esk viewer with multiple span layout options.
- Filtering and ordering of traces and spans in tables.
- View by trace or span type.
- Simple to run - [portable executable](https://github.com/ColinBradley/NekoTrace/releases/latest) with no infrastructure dependencies (like a database).
- All stored in memory (with retention option(s)).

Available on [Docker Hub](https://hub.docker.com/r/colinbradley/nekotrace).

- Safe releases: `docker run -p 4317:4317 -p 4318:4318 -p 8347:8347 -d --pull=always colinbradley/nekotrace:latest`
- Brave releases: `docker run -p 4317:4317 -p 4318:4318 -p 8347:8347 --rm --pull=always colinbradley/nekotrace:dev`

Check out a demo [here](https://www.youtube.com/watch?v=FP72uz0fI4Y).

![Screenshot of app](Content/Screenshot-2025-11-11.png)

## Notes

This is a work in progress, but very usable (well, I think so - despite it being a little ugly).

Neko means cat in Japanese, cats are small and cute. They are not dogs with lots of data. A lot of good names were taken.

## Trace Viewer Tips

- Click and drag to pan.
- `MouseWheel` to zoom in and out.
- `Alt + MouseWheel` to scroll vertically.
- `Alt + Shift + MouseWheel` to scroll horizontally.
- Double click to reset zoom and location.
