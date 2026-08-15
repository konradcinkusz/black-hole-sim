# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and all csproj files first (improves layer caching)
COPY BlackHoleSim.sln .
COPY Directory.Build.props ./
COPY BlackHoleSim.Shared/BlackHoleSim.Shared.csproj         BlackHoleSim.Shared/
COPY BlackHoleSim.Core/BlackHoleSim.Core.csproj             BlackHoleSim.Core/
COPY BlackHoleSim.ServiceDefaults/BlackHoleSim.ServiceDefaults.csproj BlackHoleSim.ServiceDefaults/
COPY BlackHoleSim.Api/BlackHoleSim.Api.csproj               BlackHoleSim.Api/

RUN dotnet restore BlackHoleSim.Api/BlackHoleSim.Api.csproj

# Copy source and publish
COPY BlackHoleSim.Shared/         BlackHoleSim.Shared/
COPY BlackHoleSim.Core/           BlackHoleSim.Core/
COPY BlackHoleSim.ServiceDefaults/ BlackHoleSim.ServiceDefaults/
COPY BlackHoleSim.Api/            BlackHoleSim.Api/

RUN dotnet publish BlackHoleSim.Api/BlackHoleSim.Api.csproj \
    -c Release -o /app --no-restore

# ── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl exists here for exactly one reason: the container healthcheck.
#
# This base image is Debian-slim and ships neither curl nor wget, so the
# `wget -qO- .../health` healthcheck in docker-compose.yml could never run — it
# exited 127 every time, the container was marked unhealthy while the API was
# perfectly healthy, and `web`'s `depends_on: service_healthy` therefore never
# came up. Nothing caught it because nothing exercised compose until the
# compose-smoke CI job existed.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Non-root user for security. Ownership is set on the way in, and the switch
# happens after the copy — COPY runs as root regardless of a preceding USER.
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build --chown=appuser:appgroup /app .

USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BlackHoleSim.Api.dll"]
