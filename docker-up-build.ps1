$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

Write-Host "=== Starting all services (unit tests gate backend, integration tests run alongside) ===" -ForegroundColor Cyan
docker compose up --build -d --remove-orphans

Write-Host "=== Integration test output (Ctrl+C to stop following) ===" -ForegroundColor Cyan
docker compose logs -f test-integration

# Clean up any orphaned Testcontainers containers
Write-Host "=== Cleaning up Testcontainers containers ===" -ForegroundColor Cyan
docker container prune --force --filter "label=org.testcontainers=true" 2>&1 | Out-Null
