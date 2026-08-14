#!/bin/sh
# Runs before nginx starts (the nginx image's entrypoint executes every
# /docker-entrypoint.d/*.sh in name order, then execs the CMD).
#
# Two things are deployment data rather than build data, and both are resolved here
# so that one image is promotable across every environment:
#
#   PORT              the port nginx listens on          (default 8080)
#   API_BASE_URL      where the browser should call the API
#                     (default "": same origin, which is what a local
#                      `docker compose` run behind a proxy wants)
#
# Baking either into the bundle at build time is what produces one image per
# environment, and a frontend that serves the wrong API address after a promotion.
set -eu

PORT="${PORT:-8080}"
API_BASE_URL="${API_BASE_URL:-}"

CONF=/etc/nginx/conf.d/default.conf
HTML=/usr/share/nginx/html

# ── Listen port ───────────────────────────────────────────────────────────────
sed -i "s/__PORT__/${PORT}/g" "$CONF"

# ── Runtime configuration the Blazor client reads on boot ─────────────────────
# WebAssemblyHostBuilder.CreateDefault fetches wwwroot/appsettings.json before the
# first component renders, so writing it here reaches the app with no build step.
# Escape any quote or backslash so a hostile value cannot break out of the string.
escaped=$(printf '%s' "$API_BASE_URL" | sed 's/\\/\\\\/g; s/"/\\"/g')

cat > "$HTML/appsettings.json" <<JSON
{
  "ApiBaseUrl": "${escaped}"
}
JSON

echo "blackholesim-web: listening on ${PORT}, ApiBaseUrl='${API_BASE_URL}'"
