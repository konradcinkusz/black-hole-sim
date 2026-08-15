# Infrastructure analysis

Topology, sizing and cost for the BlackHoleSim deployment on Fly.io — and, more
usefully, the reasoning behind each number, so the next person can tell a deliberate
choice from an accident.

## Topology

```
                                  browser
                                     │
          ┌──────────────────────────┼─────────────────────────────┐
          │ 1. the app bundle + its  │ 2. register / sign-in /     │ 3. every /api/* call,
          │    api/auth URLs to call │    token refresh            │    with a Bearer token
          ▼                          ▼                             ▼
┌────────────────────┐     ┌────────────────────┐        ┌────────────────────┐
│ blackholesim-web   │     │ blackholesim-auth  │        │ blackholesim-api   │
│ nginx + WASM bundle│     │ authservice · RS256│  jwks  │ API + render worker│
│ scale-to-zero      │     │ scale-to-zero      │←───────│ 1 machine always up│
└────────────────────┘     └─────────┬──────────┘        └─────────┬──────────┘
                                     │                             │
                     db authservice  │                             │  db blackholesim
                                     ▼                             ▼
                           ┌──────────────────────────────────────────────────┐
                           │ blackholesim-postgres.internal:5432              │
                           │ postgres:16-alpine · 3 GB volume · one machine   │
                           │ private network only — no public IP              │
                           └──────────────────────────────────────────────────┘
```

Four apps, one per service, which is the platform's grain: an app is the unit that
gets a name, a config, a set of secrets and an IP.

The numbered edges are all browser-originated and cross-origin, at the addresses
written into `appsettings.json` at container start — the web app serves the bundle
and is then out of the loop; nothing proxies through it. The `jwks` edge is the API
fetching the identity service's *public* keys, and it goes over the public hostname
`https://blackholesim-auth.fly.dev` deliberately: auth scales to zero, and only
traffic arriving through the Fly proxy wakes a stopped machine — a fetch to
`.internal` would reach a machine that is not running and fail. Both database edges
use the private address, and the identity service gets its own logical database
(`authservice`) on the shared instance, not a schema in `blackholesim`.

## 1. What runs when nothing is happening

| App | Machines when idle | Size | Memory | Volume |
|---|---|---|---|---|
| `blackholesim-web` | 0 | shared-cpu-1x | 256 MB | — |
| `blackholesim-auth` | 0 | shared-cpu-1x | 512 MB | — |
| `blackholesim-api` | 1 | shared-cpu-2x | 1 GB | — |
| `blackholesim-postgres` | 1 | shared-cpu-1x | 1 GB | 3 GB |

Two machines and one volume is the floor.

## 2. Which services pin a machine, and why

**`blackholesim-api` — pinned, and not for the usual reason.** The standard argument
for pinning is a synchronous caller whose timeout is shorter than the callee's cold
start. That is not the case here; the reason is the render worker.

A render is CPU work that happens *between* requests. The client submits a job, gets
`202 Accepted`, and polls. If the browser is closed, nothing is talking to the API
while the raytracer runs — and to the proxy an app with no traffic is an idle app, so
it stops the machine mid-render. The job is not lost (it stays `Running` in Postgres,
and `RenderWorker` recovers it on the next boot) but it does not *finish* until
something else wakes the app, which for a background job may be never.

Naming the alternative, since "could be optimised" is not a decision anyone can take:
letting the API scale to zero would save roughly the cost of one shared-cpu-2x machine
and would make submitted renders complete only opportunistically — the next visitor's
first request would resume someone else's render. That is a bad trade for a service
whose entire purpose is finishing renders.

**`blackholesim-postgres` — pinned because it is stateful.** A database with a volume
does not scale to zero and does not scale out. Nothing to decide.

**`blackholesim-web` — scales to zero.** It is entered only from a browser. A cold
start there is a slow first page, not a failed call. It has no state and no background
work, so there is nothing to abandon.

**`blackholesim-auth` — scales to zero, like the web app.** Registration, sign-in and
token refresh all happen within a request; nothing runs between requests, so an idle
machine is only cost. The first sign-in after a quiet period pays a cold start. This
posture is also why the API's key discovery goes through the public hostname rather
than `.internal`: a JWKS fetch through the proxy wakes a stopped machine, a direct
private-network fetch would not — the comment in `blackholesim-api.fly.toml` records
the reasoning.

## 3. Sizing, and what each number is actually for

**API: shared-cpu-2x / 1 GB.** The CPU size is the knob that moves render time — the
raytracer integrates a geodesic per pixel with RK4, so wall-clock scales almost
linearly with available CPU. shared-cpu-1x is genuinely usable and roughly halves the
cost; it also roughly doubles render time. Memory is sized for the largest render the
API accepts (1920×1080, capped in `RenderEndpoints.MaxPixels`): an RGB24 buffer of
~6 MB, the same again during PNG encoding, plus the byte array on its way to Postgres.
1 GB is comfortable rather than tight, which matters because an OOM kill mid-render
looks exactly like a crash loop.

**Postgres: 1 GB / 3 GB volume.** Completed renders are stored as PNG blobs in
`RenderJobs`, so the volume is sized for images, not rows — a few thousand renders at
1920×1080. Volumes grow but never shrink, so this starts modest deliberately.

**Web: 256 MB.** nginx serving static files.

**Auth: 512 MB.** ASP.NET Core and EF Core against its own small database. Sign-in
traffic is light and bursty, so shared-cpu-1x; it renders nothing and holds nothing
in memory between requests.

## 4. What is off the table

Recorded so nobody spends an afternoon re-proposing them:

- **A public IP on Postgres.** It is reached at `.internal` from the API and, for
  debugging, through `fly proxy 15432:5432 --app blackholesim-postgres`. There is no
  case where a public listener on the database is the answer.
- **Turning off `force_https`.**
- **Scaling `blackholesim-postgres` past one machine.** A volume is a local disk pinned
  to one machine; a second machine gets a second *empty* volume, not a replica. It is
  excluded from the scale workflow for exactly this reason, and deployed `--ha=false`.
- **Sharing one database between services.** The identity service got exactly what
  this rule prescribes: its own logical database (`authservice`) on the shared
  instance, not a schema in `blackholesim`.
- **Baking `API_BASE_URL` into the Blazor bundle at build time.** It is written at
  container start precisely so one built image can be promoted unchanged.

## 5. Deliberate deviations from the standard

**App names carry the system but not the environment** — `blackholesim-api`, not
`blackholesim-api-prod`. The guide asks for both because the environment suffix is what
keeps `dev` and `prod` from colliding in a namespace that is global across all of Fly.
There is one environment here, and the names are unique. If a second environment is
ever added, the suffixed names are still free and the change is confined to these four
files plus the workflow's `APP_*` variables — no code references an app name.

## 6. Recovering from a teardown

`flyio-destroy.yml` keeps the volume by default. After a destroy, push a tag: change
detection treats a service whose Fly app does not exist as changed, so the whole
deployment rebuilds and redeploys from one tag with no manual `fly launch`. That rule
is what makes the destroy workflow safe to actually use.
