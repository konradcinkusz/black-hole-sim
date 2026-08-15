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
bold "1/5  Checking prerequisites"

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
bold "2/5  Local environment"

if [ -f .env ]; then
  ok ".env already exists — leaving it alone"
else
  cp .env.example .env
  ok "Created .env from .env.example"
fi

# ── 3. The one mandatory secret ──────────────────────────────────────────────
# Generated rather than invented: asking a developer to make up a password is how
# 'changeme' reaches a deployed environment.
bold "3/5  Database password"

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

# ── 4. Secret scanning hook (optional) ───────────────────────────────────────
bold "4/5  Pre-commit hook  (optional — catches secrets before they become history)"

if command -v pre-commit >/dev/null 2>&1; then
  pre-commit install >/dev/null
  ok "gitleaks pre-commit hook installed"
else
  warn "pre-commit not found. Skipping."
  warn "Without it you keep the CI secret scan only, which catches a leak after the push rather than before."
  warn "Install with: pip install pre-commit && pre-commit install"
fi

# ── 5. Restore and build ─────────────────────────────────────────────────────
bold "5/5  Restoring and building"
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
