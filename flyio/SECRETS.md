# Secrets

What is a secret in BlackHoleSim, where each value lives, and how it gets set.

The rule that decides the column: **would you paste it into a pull request?** If yes it
is configuration and belongs in `[env]` in a `fly.toml`, where it is reviewable in a
diff. If no it is a secret, and it never appears in a committed file.

## The inventory

| Value | Kind | Where it lives | Set by |
|---|---|---|---|
| `POSTGRES_PASSWORD` | **secret** | Fly secret on `blackholesim-postgres` | `flyio.yml`, from the `POSTGRES_PASSWORD` GitHub environment secret |
| `ConnectionStrings__Default` | **secret** (embeds the password) | Fly secret on `blackholesim-api` | `flyio.yml`, *assembled* from the password + the known host |
| `FLY_API_TOKEN` | **secret** | GitHub environment `fly` | one-time human setup |
| `POSTGRES_USER`, `POSTGRES_DB`, `PGDATA` | config | `blackholesim-postgres.fly.toml` `[env]` | committed file |
| `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS` | config | `blackholesim-api.fly.toml` `[env]` | committed file |
| `Cors__AllowedOrigins__0` | config | `blackholesim-api.fly.toml` `[env]` | committed file |
| `PORT`, `API_BASE_URL` | config | `blackholesim-web.fly.toml` `[env]` | committed file |

There is exactly **one root secret**: the Postgres password. Everything else that is
secret is derived from it. This is deliberate — a connection string stored separately
from the password it contains is a connection string that will disagree with the
password after the first rotation.

## One-time human setup

Done once per repository, by a human, and then never again:

1. `fly tokens create org` → store the value as `FLY_API_TOKEN` in a GitHub
   **environment** named `fly` (an environment can be reviewed and restricted;
   a repository secret cannot).
2. Add `POSTGRES_PASSWORD` to that same environment. Generate it, do not invent it:

   ```bash
   openssl rand -base64 32 | tr -d '\n/+=' | head -c 40
   ```

3. Nothing else. No `fly launch`, no app creation, no volume creation — the deploy
   workflow does all of that idempotently, which is what lets the whole deployment
   come up from a single tag against an empty Fly organisation.

## How the values reach the apps

```
GitHub environment `fly`
  POSTGRES_PASSWORD ─┬─→ fly secrets set -a blackholesim-postgres  POSTGRES_PASSWORD=…
                     │
                     └─→ assembled into
                         "Host=blackholesim-postgres.internal;Port=5432;Database=blackholesim;
                          Username=blackhole;Password=…"
                         └─→ fly secrets set -a blackholesim-api  ConnectionStrings__Default=…
                                     │
                                     └─→ .NET configuration binds ConnectionStrings:Default
                                         └─→ AppDbContext (Program.cs)
```

The host in that connection string is `blackholesim-postgres.internal` — the private
network address. The database has no public IP and never should: it is reached only
from inside the organisation, and it never scales to zero, so the usual argument for
preferring a public URL (the proxy can wake a stopped machine, `.internal` cannot) does
not apply.

## Working with secrets

```bash
# Set one, staged until the next deploy so a release does not restart the app twice.
fly secrets set -a blackholesim-api "ConnectionStrings__Default=…" --stage

# Names and digests only. Values are never readable back — by design.
fly secrets list -a blackholesim-api
```

`fly secrets set` without `--stage` restarts the app immediately. CI always stages.

## Rotation

1. Generate a new password.
2. Update it in the GitHub environment.
3. Change it in Postgres itself (`ALTER ROLE blackhole WITH PASSWORD '…'`), reaching
   the database through `fly proxy 15432:5432 --app blackholesim-postgres`.
4. Re-run the deploy workflow so the API's connection string is reassembled.

Order matters: the API keeps working on the old string until step 4, so step 3 and
step 4 should not be far apart.

## If a secret lands in git history

Rotate first, clean history second. The moment a commit is pushed the value is public;
scrubbing the history without rotating is theater. `gitleaks` runs both as a pre-commit
hook and as a CI job (`.github/workflows/ci.yml`) specifically so this stays hypothetical.

## Local development

Local secrets come from a gitignored `.env`; `.env.example` documents every variable
and its tier. The values there are local-only defaults against a throwaway container —
they are not, and must never become, the deployed ones.
