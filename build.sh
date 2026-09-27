#!/usr/bin/env bash
# ────────────────────────────────────────────────────────────────────────────────
# build.sh  — Fast Docker build using BuildKit + NuGet layer cache
#
# Usage:
#   ./build.sh          → build + start all services
#   ./build.sh --no-up  → build only, do not start
#   ./build.sh --clean  → force full rebuild (clears NuGet cache)
# ────────────────────────────────────────────────────────────────────────────────
set -euo pipefail

CLEAN=false
NO_UP=false

for arg in "$@"; do
  case "$arg" in
    --clean)  CLEAN=true ;;
    --no-up)  NO_UP=true ;;
  esac
done

# Enable BuildKit (required for --mount=type=cache)
export DOCKER_BUILDKIT=1
export COMPOSE_DOCKER_CLI_BUILD=1

if [[ "$CLEAN" == "true" ]]; then
  echo "⚠️  Clearing BuildKit NuGet cache..."
  docker builder prune --filter type=exec.cachemount --force
fi

echo "🔨 Building image with BuildKit cache..."
docker compose build app

if [[ "$NO_UP" == "false" ]]; then
  echo "🚀 Starting services..."
  docker compose up -d
  echo ""
  echo "✅ App running at http://localhost:5000"
  echo "   Logs: docker compose logs -f app"
fi
