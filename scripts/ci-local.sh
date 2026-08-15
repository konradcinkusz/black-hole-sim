#!/usr/bin/env bash
# Build, test, formatting, secret scan, dependency audit, image builds and a compose
# smoke test. These used to run in .github/workflows/ci.yml on every push; that workflow
# is gone and only the Fly.io deploy remains, so this script is not a local rehearsal of
# CI any more — it is the check itself, and nothing runs it but you.
#
#   ./scripts/ci-local.sh              # everything
#   ./scripts/ci-local.sh test         # one job
#   ./scripts/ci-local.sh docker compose-smoke
#
# Secrets, if any were ever needed here, come from the gitignored .env — never from
# literals in this file. That is not a style preference: a helper script exactly
# like this one is how live credentials have entered a repository before.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

if [ -f .env ]; then
  set -a; . ./.env; set +a
fi

bold() { printf '\n\033[1m── %s\033[0m\n' "$1"; }

need() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required tool not found: $1" >&2
    exit 1
  }
}

job_test() {
  bold "build-and-test"
  need dotnet
  dotnet restore BlackHoleSim.sln
  dotnet build BlackHoleSim.sln -c Release --no-restore
  dotnet test BlackHoleSim.sln -c Release --no-build
}

job_format() {
  bold "format (advisory)"
  need dotnet
  dotnet format BlackHoleSim.sln --verify-no-changes --severity error || {
    echo "Formatting drift found. 'dotnet format BlackHoleSim.sln' fixes it."
    return 0
  }
}

job_secret_scan() {
  bold "secret-scan"
  if command -v gitleaks >/dev/null 2>&1; then
    gitleaks detect --source . --config .gitleaks.toml --redact --verbose
  else
    echo "gitleaks not installed; skipping. https://github.com/gitleaks/gitleaks"
  fi
}

job_audit() {
  bold "dependency-audit"
  need dotnet
  dotnet restore BlackHoleSim.sln
  dotnet list BlackHoleSim.sln package --vulnerable --include-transitive
}

job_docker() {
  bold "docker-build-api / docker-build-web"
  need docker
  docker build -f BlackHoleSim.Api/Dockerfile -t blackholesim-api:ci .
  docker build -f BlackHoleSim.Web/Dockerfile -t blackholesim-web:ci .
}

job_compose_smoke() {
  bold "compose-smoke"
  need docker
  local web_port="${WEB_PORT:-8080}" api_port="${API_PORT:-5081}"

  docker compose up -d --build --wait --wait-timeout 300
  trap 'docker compose down -v' EXIT

  curl -fsS "http://localhost:${api_port}/health"  && echo "  api /health ok"
  curl -fsS "http://localhost:${web_port}/healthz" && echo "  web /healthz ok"

  local config
  config="$(curl -fsS "http://localhost:${web_port}/appsettings.json")"
  echo "  runtime config: ${config}"
  echo "$config" | grep -q "localhost:${api_port}" \
    || { echo "Frontend was not given the API address"; return 1; }
}

run_all() {
  job_test
  job_format
  job_secret_scan
  job_audit
  job_docker
  job_compose_smoke
}

if [ $# -eq 0 ]; then
  run_all
else
  for job in "$@"; do
    case "$job" in
      test)          job_test ;;
      format)        job_format ;;
      secret-scan)   job_secret_scan ;;
      audit)         job_audit ;;
      docker)        job_docker ;;
      compose-smoke) job_compose_smoke ;;
      *) echo "Unknown job: $job" >&2
         echo "Known: test format secret-scan audit docker compose-smoke" >&2
         exit 1 ;;
    esac
  done
fi

echo
echo "Done."
