#!/usr/bin/env sh
set -e

cd "$(dirname "$0")"

docker compose down

# Clean up any orphaned Testcontainers containers (from interrupted integration test runs)
docker rm -f $(docker ps -q --filter "label=org.testcontainers=true") 2>/dev/null
docker container prune --force --filter "label=org.testcontainers=true"
