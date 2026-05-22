$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

docker compose down

# Clean up any orphaned Testcontainers containers (from interrupted integration test runs)
docker ps -q --filter "label=org.testcontainers=true" | ForEach-Object { docker rm -f $_ 2>$null }
docker container prune --force --filter "label=org.testcontainers=true" 2>&1 | Out-Null
