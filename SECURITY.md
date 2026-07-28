# Security Policy

## Reporting a vulnerability

Report privately through GitHub's
[security advisory form](https://github.com/konradcinkusz/BlackHoleSim/security/advisories/new).
Please do not open a public issue for a vulnerability.

Expect an acknowledgement within a week. This is a single-maintainer project — if
a fix will take longer than that, you will be told so rather than left waiting.

## Supported versions

The latest release only. There are no maintained back-branches.

## What BlackHoleSim handles

- **No user accounts, no secrets in transit.** The API accepts render parameters
  (numeric ranges only — disk radius, camera distance, resolution, step size) and
  returns a rendered PNG. There is no user-supplied text, file upload, or
  arbitrary code path.
- **Render jobs are stored in Postgres**, including the finished PNG bytes. Nothing
  else about the requester (no IP, no auth identity) is persisted.

## Deployment notes

- `/api/*` has **no authentication** and no rate limiting beyond the fixed-window
  limiter on `POST /api/render` (5 requests/minute, process-wide, not per-client).
  It is designed for a single trusted user on localhost or a private network. Do
  not expose it to the public internet without putting your own authentication
  and per-client rate limiting in front of it.
- Render parameters are bounded server-side (`RenderEndpoints.MaxPixels` = 1920×1080,
  `MaxSteps` = 20,000) specifically to cap CPU and memory per job — those limits
  are the extent of the abuse protection.
- The default Postgres credentials in `.env.example` (`blackhole` / `changeme`)
  are for local development only. Change them before running anywhere reachable
  by anyone else, and never commit a real `.env`.
- The Docker images run as a non-root user (`BlackHoleSim.Api/Dockerfile`,
  `BlackHoleSim.Web/Dockerfile`), but that's defense in depth, not a substitute
  for the network-exposure precautions above.
