# BlackHoleSim

[![CI](https://github.com/konradcinkusz/BlackHoleSim/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/BlackHoleSim/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/9.0)

**A Schwarzschild black hole raytracer, as a full-stack app.**

Photon geodesics in general relativity, numerically integrated in C#, rendered
into an image of a black hole with a thin accretion disk. What started as a
single console renderer is now four pieces: the physics/rendering core, a
one-shot console app, a REST API that runs renders as background jobs, and a
Blazor web UI on top of it — all sharing the same integrator, so the picture
you get from the CLI and the picture you get from the browser come from the
same code path.

![Example render](docs/example_blackhole.png)

```bash
git clone https://github.com/konradcinkusz/BlackHoleSim.git
cd BlackHoleSim
cp .env.example .env
docker compose up --build
# web UI:  http://localhost:8080
# API:     http://localhost:8080/api  (proxied by the web container's nginx)
```

---

## Projects

| Project | Role | Depends on |
|---|---|---|
| `BlackHoleSim.Core` | Physics (Schwarzschild metric, RK4 integrator), the raytracer, PPM/PNG encoding | — |
| `BlackHoleSim.Shared` | DTOs shared by API and Web (`RenderParameters`, `RenderJobDto`, `RenderJobStatus`) | — |
| `BlackHoleSim.ConsoleApp` | One-shot CLI renderer → `.ppm` file | Core |
| `BlackHoleSim.Api` | ASP.NET Core minimal API: submits render jobs to a channel-backed queue, a hosted `RenderWorker` processes them, Postgres (EF Core) persists job state + the finished PNG | Core, Shared |
| `BlackHoleSim.Web` | Blazor WebAssembly UI: render form with live progress polling, paginated gallery, delete | Shared |
| `BlackHoleSim.Tests` | xUnit: RK4 convergence, Hamiltonian conservation along a geodesic, raytracer smoke tests | Core, Shared |

## Getting started

### Docker (API + Web + Postgres)

```bash
cp .env.example .env      # adjust POSTGRES_* / WEB_PORT if needed
docker compose up --build
```

This starts three containers (`docker-compose.yml`): `db` (Postgres 16),
`api` (ASP.NET Core, auto-migrates on startup in Development), and `web`
(the Blazor WASM app served by nginx, which also proxies `/api/*` to the
API container). Open `http://localhost:${WEB_PORT:-8080}`, submit a render
from the form, and watch it go `Pending → Running → Completed` with a live
progress bar; finished renders land in the gallery.

### From source (no Docker)

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
The API needs a reachable Postgres — either point `ConnectionStrings:Default`
at one you already have, or run just the `db` service from Compose
(`docker compose up db`).

```bash
dotnet restore BlackHoleSim.sln
dotnet build BlackHoleSim.sln -c Release

# API (needs Postgres — see above). Bind it to :5080 to match the Web
# project's dev-time ApiBaseUrl (wwwroot/appsettings.Development.json) —
# there's no launchSettings.json pinning this yet, so it's set explicitly here.
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project BlackHoleSim.Api

# Web (in a second terminal)
dotnet run --project BlackHoleSim.Web
```

### Just the renderer (no API, no Docker, no database)

```bash
dotnet run --project BlackHoleSim.ConsoleApp
```

Produces `blackhole.ppm` (800×600, `bMax=50`) in the current directory.
Convert it with ImageMagick if you want a PNG/JPG:

```bash
magick blackhole.ppm blackhole.png
```

Optional positional args override the defaults:
`dotnet run --project BlackHoleSim.ConsoleApp -- out.ppm <width> <height> <bMax>`.

### Tests

```bash
dotnet test BlackHoleSim.sln -c Release
```

---

## Configuration

| Setting | Where | Purpose |
|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | `.env` (see `.env.example`) | Postgres credentials used by both the `db` and `api` containers |
| `WEB_PORT` | `.env` | Host port the web container is published on (default `8080`) |
| `ConnectionStrings:Default` | `BlackHoleSim.Api/appsettings*.json` | Npgsql connection string, overridden by Compose in containers |
| `ApiBaseUrl` | `BlackHoleSim.Web/wwwroot/appsettings*.json` | Where the WASM app points its `HttpClient`; empty in prod (nginx proxies same-origin), `http://localhost:5080` in dev |

Render parameters (`RenderParameters` in `BlackHoleSim.Shared`), settable per-job via the API/Web form or by editing defaults in code:

* `Rin`, `Rout` — accretion disk inner/outer radius (default 6M–20M; ISCO = 6M).
* `Rcam` — camera distance from the black hole (default 50M).
* `Step` — RK4 integration step size (smaller = more accurate, slower).
* `Width`, `Height` — output resolution (capped at 1920×1080 by the API).
* `BMax` — field-of-view scaling: the maximum impact parameter sampled (default 50). Too small and every ray crosses the disk band before it can escape or reach the horizon — the whole frame renders as flat disk colour with no shadow and no sky.
* `MaxSteps` — RK4 steps per ray before giving up (capped at 20,000 by the API).

## API

| Endpoint | Description |
|---|---|
| `POST /api/render` | Submit a render job (`RenderParameters` body) → `202 Accepted` with a `RenderJobDto`. Rate-limited to 5/minute. |
| `GET /api/jobs` | Paginated job list (`?page=&pageSize=`) |
| `GET /api/jobs/{id}` | Job status/progress |
| `GET /api/jobs/{id}/image` | Finished PNG (404 until `Completed`) |
| `DELETE /api/jobs/{id}` | Cancel (if running) and delete a job |
| `GET /api/health`, `/api/health/db` | Health checks (incl. Postgres connectivity) |

Jobs run on a hosted `RenderWorker` reading off an in-process channel queue
(`ChannelRenderJobQueue`); on API restart, any job left `Running` is reset to
`Pending` and re-enqueued so nothing is silently lost.

---

## 🧮 Theory (brief)

* Schwarzschild metric, equatorial plane (`G = c = 1`):

  $$
  ds^2 = -\left(1 - \frac{2M}{r}\right)dt^2
  + \frac{dr^2}{1 - 2M/r} + r^2 d\phi^2
  $$

* Photon motion from the null-geodesic Hamiltonian:

  $$
  H = \tfrac{1}{2} g^{\mu\nu} p_\mu p_\nu = 0
  $$

* Integrated with Runge–Kutta 4 (`BlackHoleSim.Core/Math/RK4.cs`).
* Event horizon at `r = 2M`; ISCO (inner disk edge) at `r = 6M`.
* The renderer is 2D (equatorial-plane geodesics only, no inclination), so
  the image is radially symmetric around the line of sight — a reasonable
  simplification for a face-on view, not a full 3D relativistic camera.

## References

* Sean Carroll – *Spacetime and Geometry* (2003)
* J.-P. Luminet (1979) – *Image of a spherical black hole with thin accretion disk*
* Kavan's video: *Simulating Black Holes in C++* (YouTube)
* Kip Thorne – *Black Holes and Time Warps* (1994)

## Future work

* Kerr metric (rotating black holes).
* Gravitational redshift / Doppler beaming in the disk shading.
* A real inclined 3D camera instead of the current radially-symmetric 2D model.

## License

MIT — see [LICENSE.txt](LICENSE.txt).

---

## Design

### Component diagram

```mermaid
flowchart LR
  subgraph Core["BlackHoleSim.Core"]
    Physics["Physics\n(State, IMetric, Schwarzschild)"]
    Math["Math\n(RK4)"]
    Rendering["Rendering\n(Raytracer, PPMWriter, PngEncoder)"]
  end

  Shared["BlackHoleSim.Shared\n(RenderParameters, RenderJobDto, RenderJobStatus)"]

  subgraph ConsoleApp["BlackHoleSim.ConsoleApp"]
    CEntry["Program.cs"]
  end

  subgraph Api["BlackHoleSim.Api"]
    Endpoints["Endpoints\n(Render, Jobs, Health)"]
    Worker["RenderWorker\n(hosted background service)"]
    Queue["ChannelRenderJobQueue"]
    Db[("Postgres via EF Core")]
  end

  subgraph Web["BlackHoleSim.Web (Blazor WASM)"]
    Pages["Pages\n(Render, Gallery, About)"]
    ApiClient["RenderApiClient"]
  end

  CEntry --> Rendering
  Endpoints --> Queue --> Worker --> Rendering
  Worker --> Db
  Endpoints --> Db
  Endpoints -.-> Shared
  Pages --> ApiClient -->|HTTP /api/*| Endpoints
  Rendering --> Physics
  Rendering --> Math
```

### Sequence — submitting a render via the API

```mermaid
sequenceDiagram
  autonumber
  participant Web as Web (Blazor)
  participant Api as Api (Endpoints)
  participant Q as ChannelRenderJobQueue
  participant W as RenderWorker
  participant Ray as Raytracer
  participant Db as Postgres

  Web->>Api: POST /api/render
  Api->>Db: insert job (Pending)
  Api->>Q: enqueue(jobId)
  Api-->>Web: 202 Accepted { id, status: Pending }

  loop poll every 500ms
    Web->>Api: GET /api/jobs/{id}
    Api->>Db: read job
    Api-->>Web: RenderJobDto
  end

  W->>Q: dequeue(jobId)
  W->>Db: status = Running
  W->>Ray: RenderToPixels(parameters)
  Ray-->>W: RGB buffer
  W->>Db: png, status = Completed

  Web->>Api: GET /api/jobs/{id}/image
  Api-->>Web: image/png
```

### Class diagram — physics core

```mermaid
classDiagram
direction LR

class State {
  +double t
  +double r
  +double phi
  +double pt
  +double pr
  +double pphi
  +State AddScaled(State k, double a)
  +operator+(State, State)
  +operator*(double, State)
}

class IMetric {
  <<interface>>
  +double H(State s)
  +State RHS(State s)
}

class Schwarzschild {
  <<implements IMetric>>
  +const double M
  +double rs
  +double H(State s)
  +State RHS(State s)
}

class RK4 {
  <<static>>
  +State Step(Func~State,State~ f, State y, double h)
}

class Raytracer {
  <<static>>
  +byte[] RenderToPixels(RenderParameters p, IProgress~double~ progress, CancellationToken ct)
  +void RenderPPM(string path, int width, int height, double bMax, ...)
  +(byte r,byte g,byte b) Trace(IMetric metric, double bImpact, RenderParameters p)
}

IMetric <|.. Schwarzschild
RK4 ..> State : integrates
Raytracer ..> RK4 : uses
Raytracer ..> Schwarzschild : metric
```
