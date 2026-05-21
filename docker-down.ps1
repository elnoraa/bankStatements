$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

docker compose down

# Clean up any orphaned Testcontainers containers (from interrupted integration test runs)
docker container prune --force --filter "label=org.testcontainers=true" 2>&1 | Out-Null
