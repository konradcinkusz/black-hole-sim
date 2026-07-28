# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and all csproj files first (improves layer caching)
COPY BlackHoleSim.sln .
COPY BlackHoleSim.Shared/BlackHoleSim.Shared.csproj         BlackHoleSim.Shared/
COPY BlackHoleSim.Core/BlackHoleSim.Core.csproj             BlackHoleSim.Core/
COPY BlackHoleSim.Api/BlackHoleSim.Api.csproj               BlackHoleSim.Api/

RUN dotnet restore BlackHoleSim.Api/BlackHoleSim.Api.csproj

# Copy source and publish
COPY BlackHoleSim.Shared/ BlackHoleSim.Shared/
COPY BlackHoleSim.Core/   BlackHoleSim.Core/
COPY BlackHoleSim.Api/    BlackHoleSim.Api/

RUN dotnet publish BlackHoleSim.Api/BlackHoleSim.Api.csproj \
    -c Release -o /app --no-restore

# ── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BlackHoleSim.Api.dll"]
