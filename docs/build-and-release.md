# Build, release and vendored files

## Releasing

`<Version>` in `Directory.Build.props` is the release mechanism — it names the artifacts, the GitHub tag and the Docker tag, and every project inherits it. Bump it, then run the workflow.

- `./Publish.ps1` builds the four publish profiles (Portable, Win64, Win64SelfContained, Linux64SelfContained) into `artifacts/{profile}/`, then zips each into `publish/` as `NekoTrace-{version}-{profile}.zip`. Each zip holds the server and `NekoTrace.Cli` beside it.
- `.github/workflows/publish.yml` (manual dispatch only) does the same on CI, creates the GitHub release `v{version}` with generated notes, then retags the existing `:{sha}` Docker image as `:latest` and `:v{version}`.
- `.github/workflows/docker-ci.yml` runs on every push to `main`, building the image and pushing `:{sha}` and `:dev`.

So `:dev` is whatever is on `main`, and `:latest` only moves when a release is dispatched.

Publish output goes to `artifacts/{profile}/` at the repository root rather than under `NekoTrace.Web/bin`, because it stops being one project’s output the moment two projects contribute to it. Only the *publish* tree moved: each project keeps its own `bin` and `obj`, which is what keeps an incremental rebuild touching one tree instead of racing another project for the same files.

Publish profiles live in `NekoTrace.Web/Properties/PublishProfiles/*.pubxml`; the `.pubxml.user` files are git-ignored. NekoTrace.Cli has none of its own — the `PublishNekoTraceCli` target in `NekoTrace.Web.csproj` publishes it into the server's output, and that file documents the constraints it carries.

The CLI ships inside the server's artifact rather than as its own download, because the two are on one machine essentially always — that is the premise a thin HTTP client is built on — and a separate zip is a second thing to find, match a version to and put somewhere. Publishing it from the server's own target rather than from `Publish.ps1` means a plain `dotnet publish` brings it too.

**Nothing is published single-file**, and that is what makes shipping two executables together cheap: a single-file build bundles the runtime into the executable, so two of them carry two copies, where two ordinary self-contained apps in one folder share the framework assemblies between them. The trade is a folder of files rather than one icon.

## Docker

The `Dockerfile` is a standard SDK-build / chiselled-aspnet-runtime split, with Node.js installed in the build stage for the TypeScript compile. It exposes 8347 and 4317 — note it does *not* expose 4318, so OTLP/HTTP needs an explicit `-p 4318:4318`.

It copies only `NekoTrace.Web` into the build stage, so the image carries no CLI. That is deliberate rather than incidental: an agent talking to a NekoTrace in Docker is not inside the container, so a CLI there would reach nothing it could not already reach over HTTP.

## Vendored and generated files

`Protos/` holds copies of the upstream OpenTelemetry proto definitions, compiled by `Grpc.Tools` at build time (`Protobuf Include="Protos\**" GrpcServices="Server"`). Treat them as external: update wholesale from upstream rather than hand-editing.

`wwwroot/js/` is TypeScript build output and is git-ignored.

PNG files are stored in Git LFS, so CI checkouts need `lfs: true`.

NuGet versions are centrally managed in `Directory.Packages.props` (`ManagePackageVersionsCentrally`), so `PackageReference` entries in the csproj carry no version.
