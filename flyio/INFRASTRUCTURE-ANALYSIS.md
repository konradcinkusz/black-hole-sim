# Infrastructure analysis

Topology, sizing and cost for the BlackHoleSim deployment on Fly.io — and, more
usefully, the reasoning behind each number, so the next person can tell a deliberate
choice from an accident.

## Topology

```
                    browser
                       │
        ┌──────────────┴───────────────┐
        │ https://blackholesim-web.fly.dev
        │   nginx + Blazor WASM bundle
        │   scale-to-zero
        └──────────────┬───────────────┘
                       │  the *browser* calls the API directly,
                       │  cross-origin, at the address written into
                       │  appsettings.json at container start
                       ▼
        ┌──────────────────────────────┐
        │ https://blackholesim-api.fly.dev
        │   ASP.NET Core + render worker
        │   1 machine always running
        └──────────────┬───────────────┘
                       │ blackholesim-postgres.internal:5432
                       │ private network only, no public IP
                       ▼
        ┌──────────────────────────────┐
        │ blackholesim-postgres         │
        │   postgres:16-alpine          │
        │   3 GB volume, always running │
        └──────────────────────────────┘
```

Three apps, one per service, which is the platform's grain: an app is the unit that
gets a name, a config, a set of secrets and an IP.

## 1. What runs when nothing is happening

| App | Machines when idle | Size | Memory | Volume |
|---|---|---|---|---|
| `blackholesim-web` | 0 | shared-cpu-1x | 256 MB | — |
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

## 4. What is off the table

Recorded so nobody spends an afternoon re-proposing them:

- **A public IP on Postgres.** It is reached at `.internal` from the API and, for
  debugging, through `fly proxy 15432:5432 --app blackholesim-postgres`. There is no
  case where a public listener on the database is the answer.
- **Turning off `force_https`.**
- **Scaling `blackholesim-postgres` past one machine.** A volume is a local disk pinned
  to one machine; a second machine gets a second *empty* volume, not a replica. It is
  excluded from the scale workflow for exactly this reason, and deployed `--ha=false`.
- **Sharing one database between services.** There is one service that owns data today;
  the second one gets its own database, not a schema in this one.
- **Baking `API_BASE_URL` into the Blazor bundle at build time.** It is written at
  container start precisely so one built image can be promoted unchanged.

## 5. Deliberate deviations from the standard

**App names carry the system but not the environment** — `blackholesim-api`, not
`blackholesim-api-prod`. The guide asks for both because the environment suffix is what
keeps `dev` and `prod` from colliding in a namespace that is global across all of Fly.
There is one environment here, and the names are unique. If a second environment is
ever added, the suffixed names are still free and the change is confined to these three
files plus the workflow's `APP_*` variables — no code references an app name.

## 6. Recovering from a teardown

`flyio-destroy.yml` keeps the volume by default. After a destroy, push a tag: change
detection treats a service whose Fly app does not exist as changed, so the whole
deployment rebuilds and redeploys from one tag with no manual `fly launch`. That rule
is what makes the destroy workflow safe to actually use.
