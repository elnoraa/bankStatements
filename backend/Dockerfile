FROM mcr.microsoft.com/dotnet/sdk:10.0
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

ENTRYPOINT ["dotnet", "watch", "run", "--non-interactive", "--launch-profile", "docker"]
