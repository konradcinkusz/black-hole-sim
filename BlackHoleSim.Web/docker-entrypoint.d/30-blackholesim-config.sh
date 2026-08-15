#!/bin/sh
# Runs before nginx starts (the nginx image's entrypoint executes every
# /docker-entrypoint.d/*.sh in name order, then execs the CMD).
#
# Three things are deployment data rather than build data, and all are resolved here
# so that one image is promotable across every environment:
#
#   PORT              the port nginx listens on          (default 8080)
#   API_BASE_URL      where the browser should call the API
#                     (default "": same origin, which is what a local
#                      `docker compose` run behind a proxy wants)
#   AUTH_BASE_URL     where the browser should reach the identity service.
#                     No default: it is always its own app on its own hostname,
#                     and the app refuses to start rather than post credentials
#                     at its own origin.
#
# Baking any of them into the bundle at build time is what produces one image per
# environment, and a frontend that serves the wrong API address after a promotion.
set -eu

PORT="${PORT:-8080}"
API_BASE_URL="${API_BASE_URL:-}"
AUTH_BASE_URL="${AUTH_BASE_URL:-}"

CONF=/etc/nginx/conf.d/default.conf
HTML=/usr/share/nginx/html

# ── Listen port ───────────────────────────────────────────────────────────────
sed -i "s/__PORT__/${PORT}/g" "$CONF"

# ── Runtime configuration the Blazor client reads on boot ─────────────────────
# WebAssemblyHostBuilder.CreateDefault fetches wwwroot/appsettings.json before the
# first component renders, so writing it here reaches the app with no build step.
# Escape any quote or backslash so a hostile value cannot break out of the string.
escape_json() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

escaped_api=$(escape_json "$API_BASE_URL")
escaped_auth=$(escape_json "$AUTH_BASE_URL")

cat > "$HTML/appsettings.json" <<JSON
{
  "ApiBaseUrl": "${escaped_api}",
  "AuthBaseUrl": "${escaped_auth}"
}
JSON

# Loud, and before nginx serves a single request. Without it the bundle throws on boot and the
# browser shows a blank page, which is a slow way to discover a missing environment variable.
if [ -z "$AUTH_BASE_URL" ]; then
  echo "blackholesim-web: WARNING — AUTH_BASE_URL is not set; nobody will be able to sign in." >&2
fi

echo "blackholesim-web: listening on ${PORT}, ApiBaseUrl='${API_BASE_URL}', AuthBaseUrl='${AUTH_BASE_URL}'"
