#!/usr/bin/env sh
set -e

cd "$(dirname "$0")"

echo "=== Building all images ==="
docker compose build

echo "=== Starting infrastructure services ==="
docker compose up -d db rabbitmq clamav

echo "=== Starting backend + frontend (unit tests gate backend) ==="
docker compose up -d backend frontend

echo "=== Running integration tests (output shown live, auto-exits when done) ==="
docker compose run --rm test-integration

# Testcontainers containers stay running after tests exit (RYUK is disabled),
# so force-remove them before the final prune of any stragglers
echo "=== Cleaning up Testcontainers containers ==="
docker rm -f $(docker ps -q --filter "label=org.testcontainers=true") 2>/dev/null
docker container prune --force --filter "label=org.testcontainers=true" > /dev/null 2>&1
