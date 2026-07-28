<!-- Explain *why* the change is needed, not just what it does. -->

## Why

## What changed

## Checks

- [ ] `dotnet build` and `dotnet test` pass locally (CI runs both)
- [ ] New logic in `BlackHoleSim.Core` or `BlackHoleSim.Api` has tests
- [ ] If a physics/rendering constant or default changed (e.g. `BMax`, `Rin`/`Rout`,
      `Step`): the reasoning is in the description, and you've actually looked at a
      rendered image, not just that the build passes
- [ ] If `RenderParameters`, `RenderJobDto`, or `RenderJobStatus` changed: both the
      API (`BlackHoleSim.Api`) and Web (`BlackHoleSim.Web`) usages were updated —
      there's no versioning between them, `BlackHoleSim.Shared` is the whole contract
- [ ] If the EF Core model changed: a migration was added (`dotnet ef migrations add`)
