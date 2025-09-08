# NekoTrace

An in-memory [Open Telemetry](https://opentelemetry.io/) [tracing](https://opentelemetry.io/docs/concepts/signals/traces/) [collector](https://opentelemetry.io/docs/collector/) and flame graph-esk viewer.

Available on [Docker Hub](https://hub.docker.com/r/colinbradley/nekotrace).

- Safe releases: `docker run -p 4317:4317 -p 8347:8347 -d colinbradley/nekotrace:latest`
- Brave releases: `docker run -p 4317:4317 -p 8347:8347 --rm --pull=always colinbradley/nekotrace:dev`

VERY work in progress.

![Screenshot of app](Content/Screenshot-2025-07-19.png)

Neko means cat, cats are small and cute. They are not dogs with lots of data.

## Trace View Tips

- Click and drag to pan.
- `MouseWheel` to zoom in and out.
- `Alt + MouseWheel` to scroll vertically.
- `Alt + Shift + MouseWheel` to scroll horizontally.
- Double click to reset zoom and location.
