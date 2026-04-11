# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution + csproj files for layer caching
COPY BlackHoleSim.sln .
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

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
