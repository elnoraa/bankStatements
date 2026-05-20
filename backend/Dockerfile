FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# Create non-root user for running the application
RUN useradd --create-home --shell /bin/bash appuser && \
    chown -R appuser:appuser /app && \
    chown -R appuser:appuser /home/appuser

# Copy project file and restore dependencies
COPY Statements.WebAPI/Statements.WebAPI.csproj ./
RUN dotnet restore

# Copy source code
COPY Statements.WebAPI/ ./

EXPOSE 5213

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail http://localhost:5213/api/health || exit 1

# Switch to non-root user
USER appuser

ENTRYPOINT ["dotnet", "watch", "run", "--non-interactive", "--launch-profile", "docker"]
