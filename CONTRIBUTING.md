# Contributing to BlackHoleSim

Thanks for your interest in BlackHoleSim! This document describes how to set up a development environment, run tests, and submit changes.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | 9.0 | `dotnet --version` to verify |
| [Docker](https://www.docker.com/) | any recent | needed for Postgres if you run the API outside Aspire |
| Postgres (via Docker, Aspire, or a local install) | 16 | only required for `BlackHoleSim.Api` / `.Web`; the console renderer and `BlackHoleSim.Core` need neither |

## Quick dev loop

```bash
git clone https://github.com/konradcinkusz/BlackHoleSim.git
cd BlackHoleSim

dotnet build BlackHoleSim.sln

# fastest inner loop: physics + renderer only, no database
dotnet run --project BlackHoleSim.ConsoleApp

# full stack (Postgres + Api + Web), orchestrated — see README "Deployment options"
dotnet run --project BlackHoleSim.AppHost
```

## Running tests

```bash
dotnet test BlackHoleSim.sln
```

All tests live in `BlackHoleSim.Tests` and run without Postgres, Docker, or a live API — they exercise `BlackHoleSim.Core` directly (RK4 convergence, Hamiltonian conservation along a geodesic, raytracer smoke tests).

## Project layout

```
BlackHoleSim.Core/         Physics (Schwarzschild metric, RK4), the raytracer, PPM/PNG encoding
BlackHoleSim.Shared/       DTOs shared by Api and Web
BlackHoleSim.ConsoleApp/   One-shot CLI renderer
BlackHoleSim.Api/          Minimal API: render-job queue, EF Core + Postgres persistence
BlackHoleSim.Web/          Blazor WebAssembly UI
BlackHoleSim.AppHost/      Aspire orchestration (Postgres + Api + Web) for local dev
BlackHoleSim.Tests/        xUnit tests
```

## Submitting changes

1. **Fork** the repository and create a feature branch from `master` (the default branch).
2. Keep changes focused — one logical change per PR.
3. Add or update tests for any new logic in `BlackHoleSim.Core` or `BlackHoleSim.Api`.
4. Run `dotnet build` and `dotnet test` before pushing. CI runs both on every PR.
5. Open a pull request against `master`. Explain *why* the change is needed, not just what it does.

## Architecture notes

- **Physics and rendering live entirely in `BlackHoleSim.Core`** and know nothing about jobs, HTTP, or Postgres — `Raytracer.RenderToPixels` is a pure function of `RenderParameters` in, RGB buffer out. Both the console app and the API call the exact same code path.
- **`RenderParameters`, `RenderJobDto`, `RenderJobStatus`** (`BlackHoleSim.Shared`) are the contract between Api and Web — there is no other shared assembly, so any field added to one must be added to the other's usage in lockstep.
- **Jobs are processed out-of-request** by `RenderWorker`, a hosted background service reading off `ChannelRenderJobQueue`. On API restart, any job left `Running` is reset to `Pending` and re-enqueued (`RenderWorker.RecoverStaleJobsAsync`) — don't assume a job that started will run to completion in the same process lifetime.
- **The renderer is 2D** (equatorial-plane geodesics only — no inclination), so the image is radially symmetric. This is a deliberate simplification, not a stopgap; see the README's Theory section before "fixing" the symmetry.

## Code style

- C# 12 / .NET 9 idioms (primary constructors, collection expressions, minimal APIs).
- No XML doc comments except on non-obvious public APIs (see `RenderParameters` for the expected level of detail).
- Prefer records for DTOs; mutable classes only where EF Core or mutation genuinely requires it.

## Questions / ideas

Open an [Issue](https://github.com/konradcinkusz/BlackHoleSim/issues) for bugs, and for design questions or feature proposals — please raise one before writing a large PR, so we can agree on the approach first.
