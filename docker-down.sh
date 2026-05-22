#!/usr/bin/env sh
set -e

cd "$(dirname "$0")"

docker compose down

# Clean up any orphaned Testcontainers containers (from interrupted integration test runs)

docker container prune --force --filter "label=org.testcontainers=true" > /dev/null 2>&1
