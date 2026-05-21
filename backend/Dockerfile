# ── Test stage ──────────────────────────────────────────────
# Builds and runs unit tests. The runtime stage depends on this
# completing successfully (exit code 0).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test
WORKDIR /app

# Copy project files and restore
COPY Statements.WebAPI/Statements.WebAPI.csproj ./Statements.WebAPI/
COPY Statements.WebAPI.Tests/Statements.WebAPI.Tests.csproj ./Statements.WebAPI.Tests/
RUN dotnet restore Statements.WebAPI.Tests/Statements.WebAPI.Tests.csproj

# Copy source code
COPY Statements.WebAPI/ ./Statements.WebAPI/
COPY Statements.WebAPI.Tests/ ./Statements.WebAPI.Tests/

ENTRYPOINT ["dotnet", "test", "Statements.WebAPI.Tests/Statements.WebAPI.Tests.csproj", "--filter", "Category!=Integration", "--logger", "console;verbosity=detailed"]

# ── Integration test stage ───────────────────────────────────
# Runs integration tests (requires Docker socket for Testcontainers).
# Does NOT block the build gate — runs alongside the API.
FROM test AS test-integration
# Install Docker CLI so Testcontainers' ResourceReaper (ryuk) can manage containers
RUN apt-get update && apt-get install -y --no-install-recommends docker.io && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["dotnet", "test", "Statements.WebAPI.Tests/Statements.WebAPI.Tests.csproj", "--filter", "Category=Integration", "--logger", "console;verbosity=detailed"]

# ── Runtime stage ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# Copy project file and restore dependencies
COPY Statements.WebAPI/Statements.WebAPI.csproj ./
RUN dotnet restore

# Copy source code
COPY Statements.WebAPI/ ./

EXPOSE 5213

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail http://localhost:5213/api/health || exit 1

ENTRYPOINT ["dotnet", "run", "--launch-profile", "docker"]
