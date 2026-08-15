# Changelog

Notable changes per release. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [semantic versioning](https://semver.org/spec/v2.0.0.html).

No release has been tagged yet — everything below is unreleased. A tag does one
thing: it runs `.github/workflows/flyio.yml`, which builds each changed image
straight to `registry.fly.io` and deploys it. That is the only automation in the
repository.

## [Unreleased]

### Added
- **Accounts, and renders that belong to one.** The API accepted anything: every
  endpoint was anonymous, and the gallery was a single global namespace in which any
  caller could list, download and delete every render anyone had ever submitted. A GUID
  is hard to guess, but `GET /api/jobs` handed them out twenty at a time.

  Tokens are now minted by this deployment's **own instance** of
  [`konradcinkusz/authservice`](https://github.com/konradcinkusz/authservice) — its own
  machine, its own logical database, its own signing key — and only *verified* here.
  Nothing in this repository holds key material: the API fetches public keys from the
  identity service's JWKS, which lets it check a token and never mint one. That is what
  the identity service moving to RS256 (its ADR 0002) makes possible; under a shared
  symmetric secret, "can verify" and "can forge" are the same capability, and giving this
  API the ability to validate a token would have given it the ability to issue one for
  any account, including an administrator.

  The dependency is on a pinned image, not on source. Nothing here compiles against that
  repository.

  Jobs carry the submitting account's `sub`, and every read, image fetch and delete
  filters on it. Someone else's job answers **404, not 403** — 403 confirms the id names a
  real render, which is exactly the enumeration answer the filter exists to withhold.
  Rows predating this change have no owner and are visible to nobody; backfilling them
  onto a sentinel account would have handed one arbitrary user everyone else's renders.
  `DELETE FROM "RenderJobs" WHERE "OwnerId" IS NULL;` clears them.

  The frontend gained sign-in and registration, a rotating token pair kept in
  localStorage, and a refresh-on-401 that is serialised behind a lock — a page firing
  several API calls at once would otherwise race several refreshes against a single-use
  refresh token, where the first wins and every other is a replay the identity service is
  entitled to treat as an attack.

- **`BlackHoleSim.ServiceDefaults` — the shared kernel**, and with it the
  telemetry the repository previously only claimed to have. `AddServiceDefaults()`
  wires OpenTelemetry (ASP.NET Core, `HttpClient` and runtime instrumentation for
  traces and metrics, plus OTel logging), the `self` liveness check, service
  discovery, and `AddStandardResilienceHandler` on every outbound `HttpClient`;
  `MapDefaultEndpoints()` maps `/health` and `/alive` so every service answers the
  same two paths the same way.

  The exporter is registered **only** when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so
  a bare `dotnet run` neither exports nor spends its life retrying a collector that
  was never started. The Aspire AppHost sets it automatically — which is what makes
  the dashboard's traces real. Until this, the README advertised traces the app had
  no ability to emit.

  Health probes are filtered out of traces. Left in, a check every ten seconds
  forever becomes nearly every span and pushes out the traces anyone wants.

  It is a shared *kernel*, not a shared *domain*: plumbing only, all of it extension
  methods a service opts into line by line. No entities, no physics, no base classes.
- **Fly.io deployment.** `flyio/` holds one `fly.toml` per app —
  `blackholesim-web` (scales to zero), `blackholesim-api` (one machine always
  up) and `blackholesim-postgres` (private network only, no public IP) — plus
  `SECRETS.md` and `INFRASTRUCTURE-ANALYSIS.md`. Deploying is pushing a `v*`
  tag: `flyio.yml` tests, detects what changed since the *previous tag*, builds
  each image once and deploys postgres → api → web. A service whose Fly app
  does not exist is always treated as changed, so the first tag against an
  empty Fly organisation provisions everything with no manual `fly launch`.
  Manual `Fly.io scale` and `Fly.io destroy` (typed confirmation, keeps the
  volume by default) workflows alongside it.
- `/health` (readiness — red until migrations have been applied) and `/alive`
  (liveness only) on the API, and a dedicated `/healthz` on the frontend.
  `/api/health` and `/api/health/db` still work.
- `docs/architecture/COMPLIANCE.md` — this repository measured against
  `konradcinkusz/architecture-standards`, including the gaps not closed and why.
- Repository baseline: root `.dockerignore`, `CODEOWNERS`, `.editorconfig`,
  `Directory.Build.props`, real `.gitattributes` rules, gitleaks as a pre-commit
  hook, and `scripts/` — one-command onboarding plus `ci-local.sh`, which runs
  tests, formatting, the secret scan, a dependency audit, both image builds and a
  compose smoke test. Those checks are local only; see *Removed*.
- Full-stack implementation: `BlackHoleSim.Api` (render-job queue backed by
  Postgres via EF Core), `BlackHoleSim.Web` (Blazor WebAssembly UI — render
  form with live progress polling, paginated gallery), `BlackHoleSim.Shared`
  (DTOs), and Docker images for both, alongside the original
  `BlackHoleSim.Core` physics library and `BlackHoleSim.ConsoleApp`.
- `BlackHoleSim.AppHost` — Aspire orchestration for local dev: one command
  (`dotnet run --project BlackHoleSim.AppHost`) starts Postgres, the API, and
  the web UI, wired together, with a dashboard showing logs/traces for all
  three.
- CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, issue/PR templates.
- README rewritten to document the actual current architecture (it previously
  described only the original Core + ConsoleApp layout).

### Changed
- **The render rate limit is per account.** `AddFixedWindowLimiter` builds one window
  shared by every caller, so "5 renders a minute" was five for the entire deployment and a
  single enthusiastic client starved everybody else. It only became fixable once a request
  carried an identity to partition on.
- **Finished renders are fetched, not linked.** The browser attaches no `Authorization`
  header to an `<img src>` or to a download link, so against an authenticated endpoint both
  fetched a 401 and rendered as a broken image. The bytes now come through the API client
  and reach the page as a data URL — one auth model for every request, rather than one
  endpoint left open so the `img` tag keeps working.

- Migrations moved off the startup path. They ran inline before `app.Run()`,
  so Kestrel only began listening once Postgres had answered — on a platform
  that judges a deploy by an HTTP health check, that turns a slow database into
  a failed deploy. `DatabaseMigrationService` now applies them after the
  listener is up, retrying while Postgres comes up, behind a gate that keeps
  `RenderWorker` off the tables until they exist (which is what the inline call
  was protecting). A migration that ultimately fails leaves the API up and
  reporting why on `/health` instead of crash-looping.
- The frontend calls the API directly instead of through nginx. `proxy_pass
  http://api:8080` hardcoded a docker-compose service name, which resolves to
  nothing once each service is its own app. The API address is now written into
  `wwwroot/appsettings.json` when the container starts (from `API_BASE_URL`),
  so one image is promotable across environments, and the API's CORS allowlist
  became configuration (`Cors__AllowedOrigins__0`) rather than a hardcoded dev
  list. `docker compose up` now publishes the API on `${API_PORT:-5081}`.
- The web container listens on 8080 (was 80), from `PORT`.

### Removed
- **Every workflow except the Fly.io ones.** `ci.yml` (build and test, an advisory
  `dotnet format` check, gitleaks, CodeQL, a dependency audit, both image builds and a
  compose smoke test) and `build-containers.yml` (which published
  `ghcr.io/konradcinkusz/blackholesim-api` and `-web` on a tag) are gone, along with
  `docker-compose.ghcr.yml` and the pull-and-run quick start that depended on those
  images. What remains is `flyio.yml`, `flyio-scale.yml` and `flyio-destroy.yml`.

  What this costs, stated plainly rather than left to be discovered: **nothing checks a
  pull request now.** Build and tests still gate a deploy — `flyio.yml` runs them in its
  `test` job — but that is at tag time, after merge. CodeQL is gone outright; gitleaks
  survives only as a pre-commit hook, which a clone that never ran `pre-commit install`
  does not have. The formatting check, dependency audit, image builds and compose smoke
  test survive only in `./scripts/ci-local.sh`, which nothing runs but a person.
  `docs/architecture/COMPLIANCE.md` records the standards this moves out of compliance.

  Dependabot is still configured and still opens pull requests; those pull requests now
  arrive with no automated verification of any kind.

### Fixed
- **The frontend downloaded itself instead of loading.** Visiting the deployed site
  saved `index.html` to disk rather than starting the app. nginx's `types` directive
  *replaces* the MIME map inherited from the enclosing context instead of adding to it,
  so the two-line `types { application/wasm wasm; }` block at server level — added to
  teach nginx about `.wasm` — discarded every other mapping the base image's
  `mime.types` provides. `.wasm` was then the only extension nginx could name, and
  everything else fell through to the http-level `default_type`,
  `application/octet-stream`: a download, per the browser's reading. The mapping is now
  scoped to a `location ~ \.wasm$`, where replacing the map affects only requests that
  are already `.wasm`. `.html`, `.css`, `.js` and `.json` are served correctly again.

  The health check could not catch this. It answers from `return 200` and touches no
  file on disk, so it stayed green while every static asset was mistyped — the check is
  right that a served index page proves little, but it also proves nothing about MIME
  types.

- **`/healthz` sent two `Content-Type` headers**, `application/octet-stream` from
  `default_type` and `text/plain` from an `add_header` — `return` already emits one, so
  adding another duplicates rather than overrides it, and a client is free to believe
  the first. It now sets `default_type text/plain` and sends one header.

- **`/appsettings.json` sent two `Cache-Control` headers.** `expires -1` emits its own
  `no-cache` alongside the explicit `no-store, no-cache, must-revalidate`. Since
  `no-cache` still permits a cache to *store* the response, the weaker of the two is
  gone and the stricter one stands alone.

- **The identity service could never be deployed to a Fly organisation that did not
  already contain it.** Every other app gets created as a side effect of something that
  runs before the deploy — Postgres by its own volume step, the API and Web by the build
  job, which must create an app before it can push to `registry.fly.io/<app>`. The
  identity service builds nothing by design: it runs a pinned upstream image and is
  deliberately absent from the build matrix. So it rode on neither, and the first command
  to name the app was `flyctl secrets set`, which does not create one — it fails with
  `Could not find App "blackholesim-auth"`. `deploy-auth` now ensures the app exists
  first, exactly as `deploy-postgres` and `build` already did.

- **`.env` and `secrets/` were not gitignored**, though `.env.example` has claimed `.env`
  was since it was written. Nothing yet written to disk had mattered; the identity
  service's signing key does.

- **The API container's healthcheck could never pass**, so the container was
  reported unhealthy while the API was serving correctly and `web`'s
  `depends_on: condition: service_healthy` never came up — meaning the
  documented `docker compose up` quick start had been broken. Nothing caught
  it because nothing exercised compose until a smoke test did — which now lives
  in `./scripts/ci-local.sh compose-smoke` rather than in CI.

  The fix that mattered was the address: the probe asked for `localhost`, and
  Kestrel logs `Now listening on: http://[::]:8080`, so whether that socket
  also answers on `127.0.0.1` depends on dual-stack support in the container
  while `localhost` leaves the choice of the two to the resolver. Probes now
  try `127.0.0.1` and then `[::1]` explicitly. The runtime image also installs
  `curl` and the API probe uses it rather than `wget`, so the probe does not
  depend on which HTTP client a slim base image happens to include — that was
  a hardening rather than the cure, and swapping the tool alone did not fix
  it.

  The smoke test's failure path now prints the probe's own output and exit
  code from `docker inspect`, so the next occurrence is answerable in one run
  instead of three.
- **Neither `.dockerignore` was ever read.** `BlackHoleSim.Api/.dockerignore`
  and `BlackHoleSim.Web/.dockerignore` looked correct, but Docker only reads a
  `.dockerignore` from the root of the build *context* — and both images build
  with `context: .`. Every build had been uploading `.git`, `docs/` and the
  sample renders to the daemon. Replaced with a root `.dockerignore`; the two
  dead files are deleted.
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
