#!/usr/bin/env bash
# One-command onboarding for BlackHoleSim.
#
#   ./scripts/setup.sh
#
# Every step is numbered, every optional step says so, and skipping an optional step
# tells you exactly which feature degrades. A fresh clone with every optional step
# skipped still runs.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

bold() { printf '\033[1m%s\033[0m\n' "$1"; }
ok()   { printf '  \033[32m✓\033[0m %s\n' "$1"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$1"; }
die()  { printf '  \033[31m✗\033[0m %s\n' "$1" >&2; exit 1; }

# ── 1. Prerequisites ─────────────────────────────────────────────────────────
bold "1/6  Checking prerequisites"

if command -v dotnet >/dev/null 2>&1; then
  ok "dotnet $(dotnet --version)"
else
  die "dotnet SDK 9.0 not found. Install it: https://dotnet.microsoft.com/download/dotnet/9.0"
fi

if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then
    ok "docker (daemon running)"
  else
    warn "docker is installed but the daemon is not running — start Docker Desktop before 'docker compose up'."
  fi
else
  warn "docker not found (optional — needed for 'docker compose up' and for running Postgres locally)."
  warn "Without it, run the API against your own Postgres and set ConnectionStrings__Default."
fi

# ── 2. Local environment file ────────────────────────────────────────────────
bold "2/6  Local environment"

if [ -f .env ]; then
  ok ".env already exists — leaving it alone"
else
  cp .env.example .env
  ok "Created .env from .env.example"
fi

# ── 3. The one mandatory secret ──────────────────────────────────────────────
# Generated rather than invented: asking a developer to make up a password is how
# 'changeme' reaches a deployed environment.
bold "3/6  Database password"

if grep -q '^POSTGRES_PASSWORD=changeme$' .env; then
  if command -v openssl >/dev/null 2>&1; then
    GENERATED="$(openssl rand -base64 32 | tr -d '\n/+=' | head -c 32)"
  else
    GENERATED="$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')"
  fi
  # A portable in-place edit: `sed -i` takes an argument on BSD/macOS and not on GNU.
  sed "s|^POSTGRES_PASSWORD=changeme$|POSTGRES_PASSWORD=${GENERATED}|" .env > .env.tmp
  mv .env.tmp .env
  ok "Generated a local database password into .env"
else
  ok "POSTGRES_PASSWORD is already set"
fi

# ── 4. Token signing key ─────────────────────────────────────────────────────
# The identity service signs with RS256 and publishes the public half at its JWKS; the
# API validates against that and holds no key material. This is the private half, and it
# never leaves the identity service's container.
#
# Not optional: without it the identity service falls back to HS256, serves an empty key
# set, and the API rejects every token it issues.
bold "4/6  Token signing key"

KEY_PATH="secrets/jwt-signing.pem"

if [ -f "$KEY_PATH" ]; then
  ok "$KEY_PATH already exists — leaving it alone"
elif command -v openssl >/dev/null 2>&1; then
  mkdir -p secrets
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY_PATH" 2>/dev/null
  # Readable by its owner only. The file is gitignored, but a keypair sitting world-readable
  # on a shared machine is the same mistake one layer down.
  chmod 600 "$KEY_PATH"
  ok "Generated an RSA keypair into $KEY_PATH (gitignored)"
else
  die "openssl not found, and it is needed to generate the token signing key.
     Use the PowerShell script instead, which needs no external tool, then re-run:
       ./scripts/new-signing-key.ps1
     Or generate one by hand:
       mkdir -p secrets
       openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out $KEY_PATH"
fi

# ── 5. Secret scanning hook (optional) ───────────────────────────────────────
bold "5/6  Pre-commit hook  (optional — catches secrets before they become history)"

if command -v pre-commit >/dev/null 2>&1; then
  pre-commit install >/dev/null
  ok "gitleaks pre-commit hook installed"
else
  warn "pre-commit not found. Skipping."
  warn "Without it you keep the CI secret scan only, which catches a leak after the push rather than before."
  warn "Install with: pip install pre-commit && pre-commit install"
fi

# ── 6. Restore and build ─────────────────────────────────────────────────────
bold "6/6  Restoring and building"
dotnet restore BlackHoleSim.sln
dotnet build BlackHoleSim.sln -c Debug --no-restore
ok "Build succeeded"

cat <<'EOF'

Ready. Pick one:

  Everything in containers (nothing else to install):
      docker compose up --build
      → frontend  http://localhost:8080
      → API       http://localhost:5081

  Orchestrated locally with Aspire (hot reload, dashboard):
      dotnet run --project BlackHoleSim.AppHost
      → frontend  http://localhost:5173
      → API       http://localhost:5080

  Just render a frame, no database, no web:
      dotnet run --project BlackHoleSim.ConsoleApp

Deployment lives in flyio/ — start with flyio/SECRETS.md.
EOF
