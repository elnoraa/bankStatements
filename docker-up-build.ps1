$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

Write-Host "=== Building all images ===" -ForegroundColor Cyan
docker compose build

Write-Host "=== Starting infrastructure services ===" -ForegroundColor Cyan
docker compose up -d db rabbitmq clamav

Write-Host "=== Starting backend + frontend (unit tests gate backend) ===" -ForegroundColor Cyan
docker compose up -d backend frontend

Write-Host "=== Running integration tests (output shown live, auto-exits when done) ===" -ForegroundColor Cyan
docker compose run --rm test-integration

# Testcontainers containers stay running after tests exit (RYUK is disabled),
# so force-remove them before the final prune of any stragglers
Write-Host "=== Cleaning up Testcontainers containers ===" -ForegroundColor Cyan
docker ps -q --filter "label=org.testcontainers=true" | ForEach-Object { docker rm -f $_ }
docker container prune --force --filter "label=org.testcontainers=true"
