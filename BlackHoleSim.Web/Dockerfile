# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + csproj files for layer caching
COPY BlackHoleSim.sln .
COPY Directory.Build.props ./
COPY BlackHoleSim.Shared/BlackHoleSim.Shared.csproj         BlackHoleSim.Shared/
COPY BlackHoleSim.Web/BlackHoleSim.Web.csproj               BlackHoleSim.Web/

RUN dotnet restore BlackHoleSim.Web/BlackHoleSim.Web.csproj

COPY BlackHoleSim.Shared/ BlackHoleSim.Shared/
COPY BlackHoleSim.Web/    BlackHoleSim.Web/

RUN dotnet publish BlackHoleSim.Web/BlackHoleSim.Web.csproj \
    -c Release -o /app --no-restore

# ── Runtime stage (nginx) ───────────────────────────────────────────────────
FROM nginx:alpine AS runtime

# Remove the default nginx config
RUN rm /etc/nginx/conf.d/default.conf

COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY BlackHoleSim.Web/nginx.conf /etc/nginx/conf.d/default.conf

# Resolves PORT and API_BASE_URL at container start. chmod here rather than relying
# on the checked-in file mode, which does not survive every clone.
COPY BlackHoleSim.Web/docker-entrypoint.d/30-blackholesim-config.sh \
     /docker-entrypoint.d/30-blackholesim-config.sh
RUN chmod +x /docker-entrypoint.d/30-blackholesim-config.sh

ENV PORT=8080
EXPOSE 8080

# CMD and ENTRYPOINT are inherited from the nginx image: its entrypoint runs
# /docker-entrypoint.d/*.sh, then execs `nginx -g 'daemon off;'`.
