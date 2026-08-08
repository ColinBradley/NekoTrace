# Build, release and vendored files

## Releasing

`<Version>` in `NekoTrace.Web/NekoTrace.Web.csproj` is the release mechanism — it names the artifacts, the GitHub tag and the Docker tag. Bump it, then run the workflow.

- `./Publish.ps1` builds the four publish profiles (Portable, Win64, Win64SelfContained, Linux64SelfContained) and zips each into `publish/` as `NekoTrace-{version}-{profile}.zip`.
- `.github/workflows/publish.yml` (manual dispatch only) does the same on CI, creates the GitHub release `v{version}` with generated notes, then retags the existing `:{sha}` Docker image as `:latest` and `:v{version}`.
- `.github/workflows/docker-ci.yml` runs on every push to `main`, building the image and pushing `:{sha}` and `:dev`.

So `:dev` is whatever is on `main`, and `:latest` only moves when a release is dispatched.

Publish profiles live in `Properties/PublishProfiles/*.pubxml`; the `.pubxml.user` files are git-ignored.

## Docker

The `Dockerfile` is a standard SDK-build / chiselled-aspnet-runtime split, with Node.js installed in the build stage for the TypeScript compile. It exposes 8347 and 4317 — note it does *not* expose 4318, so OTLP/HTTP needs an explicit `-p 4318:4318`.

## Vendored and generated files

`Protos/` holds copies of the upstream OpenTelemetry proto definitions, compiled by `Grpc.Tools` at build time (`Protobuf Include="Protos\**" GrpcServices="Server"`). Treat them as external: update wholesale from upstream rather than hand-editing.

`wwwroot/js/` is TypeScript build output and is git-ignored.

PNG files are stored in Git LFS, so CI checkouts need `lfs: true`.

NuGet versions are centrally managed in `Directory.Packages.props` (`ManagePackageVersionsCentrally`), so `PackageReference` entries in the csproj carry no version.
