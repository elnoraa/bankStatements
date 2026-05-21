#!/usr/bin/env sh
set -e

cd "$(dirname "$0")"

echo "=== Starting all services (unit tests gate backend, integration tests run alongside) ==="
docker compose up --build -d --remove-orphans

echo "=== Integration test output (Ctrl+C to stop following) ==="
docker compose logs -f test-integration

# Clean up any orphaned Testcontainers containers
echo "=== Cleaning up Testcontainers containers ==="
docker container prune --force --filter "label=org.testcontainers=true" > /dev/null 2>&1
