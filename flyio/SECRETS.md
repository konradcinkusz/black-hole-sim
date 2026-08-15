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
| `JWT_SIGNING_KEY` | **secret** | Fly secret `Jwt__PrivateKeyPem` on `blackholesim-auth` | `flyio.yml`, from the `JWT_SIGNING_KEY` GitHub environment secret |
| `ConnectionStrings__DefaultConnection` | **secret** (embeds the password) | Fly secret on `blackholesim-auth` | `flyio.yml`, *assembled* from the password + the known host |
| `POSTGRES_USER`, `POSTGRES_DB`, `PGDATA` | config | `blackholesim-postgres.fly.toml` `[env]` | committed file |
| `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS` | config | `blackholesim-api.fly.toml` `[env]` | committed file |
| `Cors__AllowedOrigins__0` | config | `blackholesim-api.fly.toml` `[env]` | committed file |
| `PORT`, `API_BASE_URL`, `AUTH_BASE_URL` | config | `blackholesim-web.fly.toml` `[env]` | committed file |
| `Auth__Authority`, `Auth__Issuer`, `Auth__Audience` | config | `blackholesim-api.fly.toml` `[env]` | committed file |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__PublicBaseUrl` | config | `blackholesim-auth.fly.toml` `[env]` | committed file |

There are exactly **two root secrets**: the Postgres password and the token signing key.
Every other secret is derived from one of them — a connection string stored separately
from the password it contains is a connection string that will disagree with the password
after the first rotation.

The signing key is the second root rather than another derivation because it is a different
kind of thing: it is the private half of the keypair the identity service signs tokens with.
Nothing else in the deployment holds it. In particular **the API does not** — it fetches the
public half from `https://blackholesim-auth.fly.dev/.well-known/jwks.json`, which lets it
verify a token and not mint one. Handing the API a copy would make "can verify" and "can
forge" the same capability again, which is the whole reason the identity service signs with
RS256 rather than a shared secret (authservice ADR 0002).

## One-time human setup

Done once per repository, by a human, and then never again:

1. `fly tokens create org` → store the value as `FLY_API_TOKEN` in a GitHub
   **environment** named `fly` (an environment can be reviewed and restricted;
   a repository secret cannot).
2. Add `POSTGRES_PASSWORD` to that same environment. Generate it, do not invent it:

   ```bash
   openssl rand -base64 32 | tr -d '\n/+=' | head -c 40
   ```

3. Add `JWT_SIGNING_KEY` to that environment — a PKCS#8 RSA private key in PEM form,
   pasted whole, `BEGIN`/`END` lines and newlines included:

   ```bash
   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048
   ```

   Keep a copy somewhere durable and out of the repository. Losing it signs every user
   out; leaking it lets the holder mint a token as anyone, including an administrator.
   This is *not* the same key as the one `./scripts/setup.sh` generates for local
   development, and the two must never be the same file — a local keypair on a developer
   laptop is not a production trust root.

4. Nothing else. No `fly launch`, no app creation, no volume creation — the deploy
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

## Rotation — the database password

1. Generate a new password.
2. Update it in the GitHub environment.
3. Change it in Postgres itself (`ALTER ROLE blackhole WITH PASSWORD '…'`), reaching
   the database through `fly proxy 15432:5432 --app blackholesim-postgres`.
4. Re-run the deploy workflow so the API's connection string is reassembled.

Order matters: the API keeps working on the old string until step 4, so step 3 and
step 4 should not be far apart.

## Rotation — the signing key

Unlike the password, this one is a rolling change and nobody has to be signed out for it.
The identity service will sign with the new key while still *accepting* the old one, so
tokens already in the wild keep working until they expire on their own:

1. Generate a new keypair, and extract the **public** half of the outgoing one:

   ```bash
   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-signing-new.pem
   openssl rsa -in jwt-signing-old.pem -pubout -out jwt-signing-old.pub
   ```

2. Set both on the identity service — the new private key signs, the old public key stays
   in the validation set and in the published JWKS:

   ```bash
   fly secrets set -a blackholesim-auth \
     "Jwt__PrivateKeyPem=$(cat jwt-signing-new.pem)" \
     "Jwt__PreviousPublicKeyPem=$(cat jwt-signing-old.pub)"
   ```

3. Update `JWT_SIGNING_KEY` in the GitHub environment to the new private key, or the next
   deploy will quietly put the old one back.
4. Once every token signed with the old key has expired — one access-token lifetime is
   enough — remove `Jwt__PreviousPublicKeyPem`.

Nothing has to change on the API at any point. It re-reads the JWKS and picks the key by
the `kid` in each token's header, and because that id is derived from the key itself, a
rotated key cannot be confused with its predecessor.

## If a secret lands in git history

Rotate first, clean history second. The moment a commit is pushed the value is public;
scrubbing the history without rotating is theater. `gitleaks` runs both as a pre-commit
hook and as a CI job (`.github/workflows/ci.yml`) specifically so this stays hypothetical.

## Local development

Local secrets come from a gitignored `.env`; `.env.example` documents every variable
and its tier. The values there are local-only defaults against a throwaway container —
they are not, and must never become, the deployed ones.

The local token signing key is the same story: `./scripts/setup.sh` generates one into
`secrets/jwt-signing.pem`, which is gitignored, mounted into the local identity service,
and has nothing to do with `JWT_SIGNING_KEY` above. If it is missing, the identity service
falls back to symmetric signing, publishes an empty key set, and the API rejects every
token it issues — the AppHost fails with that message rather than letting you find out
from a sign-in that appears to work.
