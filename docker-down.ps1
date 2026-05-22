$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

docker compose down

# Clean up any orphaned Testcontainers containers (from interrupted integration test runs)
docker ps -q --filter "label=org.testcontainers=true" | ForEach-Object { docker rm -f $_ }
docker container prune --force --filter "label=org.testcontainers=true"
