# Docker-First Build Policy

This project uses Docker for all build, run, and test operations. Do NOT use language-specific tooling directly.

## Rules
- **NEVER** run `dotnet`, `npm`, or `npx` commands directly in the terminal
- **ALWAYS** use `docker compose` for all build and run operations
- Delegate build/run/test operations to the `@docker-builder` agent when possible

## Quick Reference
- Full stack build: `docker compose up --build` (from repo root)
- Run tests: `docker build --target test -t backend-tests ./backend && docker run --rm backend-tests`
- Stop services: `docker compose down`
- Platform: use `docker-up-build.ps1` on Windows, `docker-up-build.sh` on Linux/macOS
