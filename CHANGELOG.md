# Changelog

Notable changes per release. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [semantic versioning](https://semver.org/spec/v2.0.0.html).

No release has been tagged yet — everything below is unreleased. Once tagged,
releases publish two images to GHCR — `ghcr.io/konradcinkusz/blackholesim-api`
and `-web` (see `.github/workflows/build-containers.yml`).

## [Unreleased]

### Added
- Full-stack implementation: `BlackHoleSim.Api` (render-job queue backed by
  Postgres via EF Core), `BlackHoleSim.Web` (Blazor WebAssembly UI — render
  form with live progress polling, paginated gallery), `BlackHoleSim.Shared`
  (DTOs), and Docker images for both, alongside the original
  `BlackHoleSim.Core` physics library and `BlackHoleSim.ConsoleApp`.
- `BlackHoleSim.AppHost` — Aspire orchestration for local dev: one command
  (`dotnet run --project BlackHoleSim.AppHost`) starts Postgres, the API, and
  the web UI, wired together, with a dashboard showing logs/traces for all
  three.
- GHCR image-publish workflow (`build-containers.yml`) and
  `docker-compose.ghcr.yml` for a no-clone, pull-and-run quick start.
- CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, issue/PR templates.
- README rewritten to document the actual current architecture (it previously
  described only the original Core + ConsoleApp layout).

### Fixed
- The default `BMax` (field-of-view scaling) was `10`, which made every ray
  cross the accretion-disk radius band before it could escape or reach the
  event horizon — the out-of-the-box render was a solid orange square, no
  shadow, no sky. Bumped the default to `50` so the documented quick-start
  actually produces a black hole image.
- `Raytracer.Trace` painted a kinematically-invalid ray (impact parameter
  larger than what's reachable from the camera's finite starting radius) with
  the same colour as a true event-horizon capture, so at wide fields of view
  the rendered "shadow" was partly a geometry artifact rather than actual
  photon capture. The invalid case now renders as background sky; a real
  photon-capture test (`Trace_SmallImpactParameter_IsCapturedByHorizon`)
  confirms the true shadow still renders correctly.
- `docs/example_blackhole.png`, referenced by the README, never existed.
- **The API could never actually persist anything.** The committed EF Core
  migration was missing its `.Designer.cs` companion file (the one carrying
  the `[Migration]` attribute EF uses for discovery), so
  `Database.Migrate()` silently found zero migrations to apply — in
  Development *and* Production. Regenerated the migration properly with
  `dotnet ef migrations add`.
- **The Api host crashed on startup in Production** (`docker-compose.yml`'s
  `ASPNETCORE_ENVIRONMENT=Production`, and therefore every documented Docker
  quick start): auto-migration only ran `if (app.Environment.IsDevelopment())`,
  but `RenderWorker` queries `RenderJobs` unconditionally as soon as the host
  starts. Against a fresh database that query threw, and an unhandled
  exception in a hosted service stops the whole host by default. Migration
  now runs unconditionally on startup; verified end-to-end against a real
  Postgres in Production mode (submit → job completes → PNG downloads).
- **The Web app could never load in Production either.** `ApiBaseUrl` is `""`
  in `wwwroot/appsettings.json` for prod (same-origin via nginx), but
  `Program.cs` only substituted the fallback when the config value was
  *missing* (`??`), not when it was empty — `new Uri("")` throws
  `UriFormatException`, so resolving `RenderApiClient` (which every page
  does immediately) crashed the app. Empty is now treated the same as
  missing.
- `RenderApiClient.GetImageUrl` returned a bare relative path
  (`/api/jobs/{id}/image`). The browser resolves relative `<img>`/`<a>` URLs
  against the *page's* origin, not through the configured `HttpClient` — this
  happened to work in prod (Web and Api share an origin via nginx) but broke
  running Web and Api as separate dev processes on different ports: the
  request landed on the Blazor dev server's own SPA fallback, which 200'd
  with `index.html` instead of the image, so renders and gallery thumbnails
  never displayed. Now builds an absolute URL from the client's
  `BaseAddress`; verified with real browser screenshots
  (`docs/img/web-render-result.png`, `docs/img/web-gallery.png`).
- Missing `launchSettings.json` on `BlackHoleSim.Api` and `BlackHoleSim.Web` —
  neither had one, so there was no pinned dev port for either `dotnet run`,
  an IDE's F5, or Aspire's `AddProject` to rely on, despite the Api's CORS
  policy already hardcoding `5080`/`5173` as if they existed.
