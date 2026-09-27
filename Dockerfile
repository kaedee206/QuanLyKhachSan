# syntax=docker/dockerfile:1.4
# ────────────────────────────────────────────────────────────────────────────────
# Build stage — uses BuildKit cache mounts so NuGet packages are NOT re-downloaded
# on every rebuild. Only re-downloaded when the .csproj actually changes.
# ────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1. Restore dependencies (cached in /root/.nuget/packages across builds)
COPY ["QuanLyKhachSan.csproj", "./"]
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "QuanLyKhachSan.csproj"

# 2. Copy source & publish (reuses the restored packages from cache above)
COPY . .
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "QuanLyKhachSan.csproj" \
        -c Release \
        -o /app/publish \
        --no-restore \
        -p:UseAppHost=false

# ────────────────────────────────────────────────────────────────────────────────
# Runtime stage — minimal Alpine image
# ────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Enable full globalization (Vietnamese text / Npgsql needs it)
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
# Cache Alpine packages across builds
RUN --mount=type=cache,id=apk,target=/var/cache/apk \
    apk add --no-cache icu-libs tzdata

# Create writable runtime directories
RUN mkdir -p /app/logs /app/keys

# Copy published output from build stage
COPY --from=build /app/publish .

# ── Port & Environment ────────────────────────────────────────────────────────
EXPOSE 5000
ENV ASPNETCORE_HTTP_PORTS=5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "QuanLyKhachSan.dll"]
