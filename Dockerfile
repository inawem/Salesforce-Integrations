# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /src

# Copy solution and project files
COPY ["SalesforceIntegration.Api.csproj", "."]

# Restore dependencies
RUN dotnet restore "SalesforceIntegration.Api.csproj"

# Copy source code
COPY . .

# Build application
RUN dotnet build "SalesforceIntegration.Api.csproj" -c Release -o /app/build

# Publish stage
FROM builder AS publish

RUN dotnet publish "SalesforceIntegration.Api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Create non-root user for security
RUN useradd -m -u 1000 appuser && chown -R appuser:appuser /app
USER appuser

# Expose ports
EXPOSE 5000 5001

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "SalesforceIntegration.Api.dll"]
