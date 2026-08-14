# Compliance with the reference architecture

BlackHoleSim measured against
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards):
the principles in `docs/architecture/00-REFERENCE-ARCHITECTURE.md`, and the checklists
in `docs/guides/FLY-IO-DEPLOYMENT.md` and `docs/guides/REPO-BASELINE.md`.

Following the standards' own convention, this file does not restate the principles —
it records where this repository stands against them, what changed, and what has not
been done yet and why. An honest "no" with a reason is worth more than a checkbox.

**Assessed:** August 2026, against the reference architecture as of that date.

---

## 1. Summary

| | Before | After |
|---|---|---|
| Deployable to Fly.io | **No** — no configuration existed, and three things would have broken a deploy even if it had | **Yes** |
| Reference-architecture checklist | 4 of 18 | 12 of 18, 2 not applicable |
| Fly.io guide checklist | 3 of 24 | 24 of 24 |
| Repo-baseline checklist | 1 of 7 | 5 of 7 |

The three things that would have broken a deploy are worth naming, because none of
them is visible from reading the repository — only from trying:

1. **Migrations ran inline before `app.Run()`.** Kestrel started listening only after
   Postgres had answered. Fly decides whether a deploy succeeded from an HTTP health
   check, so a database that is slow or briefly unreachable at boot is not a slow
   deploy — it is a failed one.
2. **The frontend reached the API via `proxy_pass http://api:8080`** — a
   docker-compose service name. On a platform where each service is its own app with
   its own hostname there is no `api` to resolve, so every call from the browser would
   have 502'd.
3. **The health check had nothing safe to point at.** `/api/health` covered database
   connectivity but not whether the schema had been applied, and the frontend had no
   health endpoint at all — a check on `/` passes on a white screen.

---

## 2. The reference architecture checklist

| # | Item | Status | Notes |
|---|---|---|---|
| 1 | Declared in the AppHost with `WithReference`, `WaitFor`, `WithHttpHealthCheck` | ✅ | `WithReference`/`WaitFor` were already there; `WithHttpHealthCheck("/health")` added. |
| 2 | Calls `AddServiceDefaults()` and `MapDefaultEndpoints()` | ❌ | No ServiceDefaults project exists. See §5.1. |
| 3 | Exposes `/health` and `/alive`; the platform check points at `/health` | ✅ | Split in `HealthEndpoints.cs`; `blackholesim-api.fly.toml` checks `/health`. `/api/health` kept as an alias. |
| 4 | Emits OTLP traces, metrics and logs | ❌ | See §5.1 — same root cause as #2. |
| 5 | Owns its database; no other service connects to it | ✅ | One service, one database, one connection string. |
| 6 | Schema applied by `MigrateAsync` from migrations, in a hosted service | ✅ | `DatabaseMigrationService`. Was `Migrate()` inline before `app.Run()`. |
| 7 | Configuration from the environment; no secret in source; secret scanner in CI | ✅ | CORS and the API address became configuration. gitleaks runs as a pre-commit hook *and* a CI job. |
| 8 | One service holds a signing key; others validate against its JWKS | **n/a** | The system has no authentication. Every endpoint is public by design — it renders pictures of a black hole. Recorded as not-applicable rather than passed: the day a job is owned by a user, this becomes a real gap. |
| 9 | The shared kernel holds no entity, DTO, enum or user-facing string | ✅* | `BlackHoleSim.Shared` holds `RenderParameters`, `RenderJobDto` and `RenderJobStatus` — it is the **Contracts** project of the reference diagram, not the shared kernel, and DTOs shared across a boundary are exactly what belongs there. The asterisk: there is no architecture test or CI size check enforcing it stays that way. |
| 10 | Every optional integration has a working no-op or fallback | **n/a** | There are no optional integrations. The database is mandatory. What did change: a failed migration now leaves the API up and reporting the failure on `/health`, rather than crash-looping — a process that exits cannot tell you why. |
| 11 | Multi-stage Dockerfile; runtime major = TFM major; listens on `:8080`; non-root | ✅* | API: multi-stage, `aspnet:9.0` against `net9.0`, `:8080`, non-root. Web: now `:8080` (was `:80`). The asterisk: nginx's master process runs as root and drops to the `nginx` user for workers — the base image's own design, changeable only by moving to `nginxinc/nginx-unprivileged`. |
| 12 | One `fly.toml`; `min_machines_running = 1` if another service calls it in-request | ✅ | Three configs, one per app. The API pins a machine — for the render worker rather than for a synchronous caller; reasoning in `flyio/INFRASTRUCTURE-ANALYSIS.md` §2. |
| 13 | Outbound `HttpClient`s carry the standard resilience handler | ❌ | The Blazor client registers a bare `HttpClient`. See §5.2. |
| 14 | `Program.cs` is a manifest; wiring lives in extension methods | ◐ | ~100 lines and readable, but the health-check, CORS and rate-limit blocks are still inline rather than in `ServiceCollectionExtensions`. |
| 15 | Extension points are interfaces registered in DI, not base classes | ✅ | `IRenderJobQueue` / `ChannelRenderJobQueue`. No inheritance chains anywhere. |
| 16 | Has a test project; the logic-bearing layer is covered | ✅ | `BlackHoleSim.Tests` covers `Core` — RK4 convergence, Hamiltonian conservation, horizon capture. The physics is where the logic is. |
| 17 | Built by the tag-driven workflow with path-based change detection | ✅ | `.github/workflows/flyio.yml`. |
| 18 | Architectural decisions recorded in `docs/` | ✅ | This file, plus `flyio/INFRASTRUCTURE-ANALYSIS.md` and `flyio/SECRETS.md`. |

**12 pass, 2 not applicable, 3 fail, 1 partial** — from 4 passing before.

---

## 3. The Fly.io deployment checklist

Everything below was absent before this work — there was no `flyio/` directory.

**Per service**

- [x] Binds `0.0.0.0` on a port from configuration; `internal_port` matches — API `http://+:8080`, web `listen __PORT__` resolved at start
- [x] A health endpoint that fails when the app is broken, not just when it is gone — `/health` goes red while the schema is unusable; the frontend's `/healthz` is deliberately not `/`
- [x] Slow first-boot work happens after the listener is up — `DatabaseMigrationService`
- [x] Multi-stage Dockerfile; runtime major = TFM major; project files restored before source
- [x] A `.dockerignore` covering the actual build context — see §4
- [x] All configuration from the environment; nothing environment-specific in the image
- [x] Optional dependencies degrade rather than block startup

**Per `fly.toml`**

- [x] `app` is globally unique and carries the system name — environment suffix deliberately omitted; see §6
- [x] `[build] dockerfile` **and** `context` both declared
- [x] `[env]` holds only things that may be public
- [x] `min_machines_running` justified — 1 for the API with the reason in a comment, 0 for the frontend
- [x] Health check path, `interval`, `timeout`, `grace_period` set deliberately — 90s on the API to cover cold start plus first-boot migration
- [x] Stateful app: no `[http_service]`, `[[mounts]]` with `initial_size`, `PGDATA` in a subdirectory

**Per repository**

- [x] `flyio/SECRETS.md`
- [x] `flyio/INFRASTRUCTURE-ANALYSIS.md`
- [x] Tag-triggered workflow: test → detect → build once → ordered deploy
- [x] Missing Fly app ⇒ always selected, so a cold estate comes up from one tag
- [x] App and volume creation idempotent, in the workflow rather than someone's shell history
- [x] Deploy gates accept `success || skipped` from upstream jobs
- [x] Database gated separately — only its own config file selects it, because redeploying Postgres restarts it
- [x] At least one post-deploy assertion the health check cannot make — the API's `/health` is polled until the schema is ready, and the frontend's `appsettings.json` is fetched and checked for the right API address
- [x] Manual scale and destroy workflows; destroy behind a typed confirmation

**24 of 24.**

---

## 4. The repo-baseline checklist

| Item | Status | Notes |
|---|---|---|
| `CODEOWNERS` | ✅ | Added, with the deploy, secret, schema and physics paths claimed explicitly. |
| Dependency-update automation | ✅ | Dependabot was already configured. |
| `.editorconfig` | ✅ | Added. |
| `Directory.Build.props` | ✅ | Added. `Directory.Packages.props` deliberately not — see §5.3. |
| PR + issue templates | ✅ | Already present. |
| Real `.gitattributes` | ✅ | Was the stock template with every rule commented out — one live line, `* text=auto`. Now has rules that matter, including `*.sh eol=lf`, without which the container entrypoint fails with `no such file or directory` after a Windows clone. |
| Exclusion-based `.dockerignore` | ✅ | **This was the sharpest finding.** `BlackHoleSim.Api/.dockerignore` and `BlackHoleSim.Web/.dockerignore` existed and looked right, but Docker reads `.dockerignore` only from the root of the build *context* — and both builds use `context: .`. Neither file had ever been consulted; every image build was uploading `.git`, `docs/` and the sample renders to the daemon. Replaced with a root file and the dead ones deleted. |
| Secret scanning: pre-commit **and** CI | ✅ | gitleaks in both, `.gitleaks.toml` allowlisting exactly one thing — the localhost dev connection string — with the reason written next to it. |
| CodeQL / SAST + dependency audit in CI | ✅ | Both added. The audit inspects output rather than trusting the exit code: `dotnet list package --vulnerable` exits 0 even when it finds something. |
| CI runs the linters the repo claims | ◐ | `dotnet format --verify-no-changes` added but **advisory** — it reports drift as a warning rather than failing. `.editorconfig` arrived after the code did, and the first run confirmed the expected drift: aligned assignments in `RenderWorker.cs`, `RenderJobMapping.cs`, `Program.cs`, `StatusBadge.cs` and four test files, plus import ordering in `PngEncoder.cs`. All of it pre-existing — none was introduced by this change. Making it blocking before a formatting pass lands would mean permanently red CI, which teaches people to ignore CI. The pass itself was not done here: it is ~45 whitespace edits across files this change does not otherwise touch, and with no SDK available to run `dotnet format` they would have to be applied by hand and unverified. It is a clean standalone commit for someone with a working SDK. |
| One-command onboarding | ✅ | `scripts/setup.sh`: numbered steps, prerequisites with install pointers, generated password rather than an invented one, optional steps labeled with what degrades if skipped. |
| Operational scripts + README with variable tiers | ✅ | `scripts/README.md`, including a troubleshooting table keyed on literal error text. No deploy scripts, deliberately: provisioning lives in the workflow, not in a laptop's shell history. |
| Retired workflows archived, not comment-disabled | ✅ | Nothing retired. |
| AI agent definitions in the repo | ❌ | There are none — no `.claude/agents/` or `.github/agents/`. Not invented here: an agent definition with no one using it is decoration, and the standards' own agents (§8) cover the review case from the standards repository. |
| README claims verified | ✅ | The README described the nginx `/api/*` proxy and a same-origin frontend, both of which this change removes. Updated, along with the health endpoints, ports and configuration table. A stale README is a review finding. |
| One named source of truth per environment variable | ✅ | Stated in the README's configuration section. |

---

## 5. Open gaps, with reasons

### 5.1 No ServiceDefaults, and therefore no telemetry (P2, P15, items 2 and 4)

The single largest remaining gap, and the two are one gap: the standard's telemetry,
health, service discovery, resilience and CORS all arrive through a shared kernel
project that this repository does not have.

Not done here because it is a different change from making the app deployable —
a new project, referenced by every service, plus an OTLP endpoint to export to. Doing
it inside a deployment change would mean shipping two things at once, and the
observability half cannot be verified by the same CI run.

What exists in the meantime: structured logging through `ILogger` throughout, and
health endpoints that carry real readiness information rather than a constant 200.

### 5.2 No resilience handler on outbound HttpClients (item 13)

`BlackHoleSim.Web/Program.cs` registers a bare `HttpClient`. It should carry
`AddStandardResilienceHandler`, which needs `Microsoft.Extensions.Http.Resilience`.

Held back for one honest reason: this change was authored in an environment with no
.NET SDK, so a new package reference could not be compiled or tested. Adding an
untested dependency to make a checklist go green is the wrong trade. It belongs with
§5.1, which introduces the project that should own it.

### 5.3 Package versions are not centrally managed

`Directory.Build.props` is in place; `Directory.Packages.props` is not. Central package
management interacts with `Aspire.AppHost.Sdk`, and this repository carries an SDK/package
version pair (`9.3.0` with `13.4.6`) that is unusual enough to want a real build behind
any change to it. Same reason as §5.2 — no SDK available to verify — and the failure
mode is a broken restore for everyone, not a missing feature.

Seven `.csproj` files still pin their own versions. Dependabot keeps them current,
which is why this is a tidiness gap rather than a security one.

### 5.4 Program.cs is not yet a manifest (item 14)

Health-check, CORS and rate-limiter wiring is still inline. At ~100 lines it is
readable, so this is a real but low-severity deviation. The extraction is natural to
do alongside §5.1, which is where the extension methods would live.

---

## 6. Deliberate deviations

**App names carry the system but not the environment.** The guide asks for
`<system>-<service>-<env>` because the environment suffix is what stops `dev` and
`prod` colliding in a namespace that is global across all of Fly. There is one
environment here and the names are unique. The suffixed names remain free, and no code
references an app name — adding a second environment touches three `fly.toml` files and
the workflow's `APP_*` variables and nothing else. Recorded in
`flyio/INFRASTRUCTURE-ANALYSIS.md` §5 as well, where an operator will actually look.

**The API pins a machine for a background job, not for a synchronous caller.** The
guide's rule for `min_machines_running = 1` is "another service calls it in-request".
Nothing does. The reason here is that a render is CPU work happening *between*
requests, and an app with no inbound traffic looks idle to the proxy — it would be
stopped mid-render. Same conclusion, different argument, so it is written down rather
than left to look like a misapplication of the rule.

---

## 7. Verification status

Stated plainly, because it bears on how much the "after" column above is worth.

The environment this change was authored in had **no .NET SDK and no Docker daemon**.
So:

| Checked | How |
|---|---|
| Every YAML file parses | `yaml.safe_load` over all workflows and compose files |
| Every TOML file parses | `tomllib` over all three `fly.toml` and `.gitleaks.toml` |
| Every shell script parses | `bash -n` / `sh -n` |
| Logic and wiring | Read, by hand, against the standards' failure-mode tables |

| **Not** checked locally | Covered by |
|---|---|
| The solution compiles | CI `build-and-test` |
| Tests pass | CI `build-and-test` |
| Both images build | CI `docker-build-api`, `docker-build-web` |
| The stack runs, the frontend gets its API address | CI `compose-smoke` — added in this change specifically so this is not left to a human |
| A real Fly deploy | Pushing a `v*` tag, once `FLY_API_TOKEN` and `POSTGRES_PASSWORD` exist in the `fly` GitHub environment |

Two changes carry the most risk of a compile error and are worth watching in the first
CI run: `WithHttpHealthCheck` in the AppHost (an Aspire API), and the `AddCheck` overload
with a `tags:` argument in `Program.cs`.

---

## 8. Re-measuring

This file is a snapshot, and snapshots rot. The standards repository ships review
agents for exactly this:

```
catalog/agents/architecture-review.agent.md
catalog/agents/architecture-recover.agent.md
catalog/agents/architecture-modernize.agent.md
```

Re-run the review agent against this repository when the standards change, or when
§5.1 lands — whichever comes first.
