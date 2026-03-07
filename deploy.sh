#!/usr/bin/env bash
# deploy.sh – Pull latest code and restart the Docker stack.
# Run on the server as a non-root user with Docker access.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

HTTPS=false
for arg in "$@"; do
    case $arg in
        --https) HTTPS=true ;;
        --help|-h)
            echo "Usage: ./deploy.sh [--https]"
            echo "  --https   Use docker-compose.https.yml overlay (requires ./certs/)"
            exit 0 ;;
    esac
done

echo "=== HackIT Sentry Deploy ==="
echo ""

# Check .env exists
if [[ ! -f ".env" ]]; then
    echo "ERROR: .env not found. Run ./setup.sh first."
    exit 1
fi

# Pull latest code
echo "[1/3] Pulling latest code..."
git pull --ff-only

# Build & restart
echo ""
echo "[2/3] Building Docker images..."
if [[ "$HTTPS" == true ]]; then
    COMPOSE_FILES="-f docker-compose.yml -f docker-compose.https.yml"
else
    COMPOSE_FILES="-f docker-compose.yml"
fi

docker compose $COMPOSE_FILES build --pull

echo ""
echo "[3/3] Restarting containers..."
docker compose $COMPOSE_FILES up -d --remove-orphans

echo ""
echo "=== Deploy complete ==="
docker compose $COMPOSE_FILES ps
