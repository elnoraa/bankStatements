FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /app

# Copy project file and restore dependencies
COPY Statements.WebAPI/Statements.WebAPI.csproj ./
RUN dotnet restore

# Copy source code
COPY Statements.WebAPI/ ./

EXPOSE 5213

ENTRYPOINT ["dotnet", "watch", "run", "--launch-profile", "docker"]
